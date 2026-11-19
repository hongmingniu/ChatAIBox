import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {randomBytes,createHash} from 'node:crypto';
import {WebSocket} from 'ws';
const require=createRequire(import.meta.url),{createRelay}=require('../server/relay.cjs'),{RelayClient}=require('../desktop/relay.cjs');
test('中转默认拒绝无鉴权配置，未携带或错误密钥在 WebSocket 握手前被拒绝',async()=>{
 assert.throws(()=>createRelay(),/refusing to start/);
 const accessKey=randomBytes(32).toString('hex'),relay=createRelay({accessKeyHash:createHash('sha256').update(accessKey).digest('hex')});
 await new Promise(r=>relay.server.listen(0,'127.0.0.1',r));
 const url=`ws://127.0.0.1:${relay.server.address().port}/relay`;
 async function denied(key){return new Promise((resolve,reject)=>{const ws=new WebSocket(url,{headers:key?{Authorization:'Bearer '+key}:{}});ws.once('open',()=>{ws.terminate();reject(Error('Unauthorized connection accepted'));});ws.once('unexpected-response',(_req,res)=>{res.resume();ws.terminate();resolve(res.statusCode);});ws.on('error',()=>{});});}
 let client;
 try{
  assert.equal(await denied(),401);assert.equal(await denied(randomBytes(32).toString('hex')),401);assert.equal(relay.rooms.size,0);
  client=new RelayClient({url:url.replace('ws:','http:').replace('/relay',''),key:randomBytes(32).toString('hex'),accessKey,allowLocal:true});
  const until=Date.now()+3000;while(!client.joined&&Date.now()<until)await new Promise(r=>setTimeout(r,20));assert.ok(client.joined);assert.equal(relay.rooms.size,1);
 }finally{client?.close();await relay.close();}
});
