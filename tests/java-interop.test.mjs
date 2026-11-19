import test from 'node:test';import assert from 'node:assert/strict';
import {spawn,spawnSync}from 'node:child_process';import {existsSync,mkdirSync}from 'node:fs';import path from 'node:path';import {createRequire}from 'node:module';import {randomBytes,randomUUID}from 'node:crypto';import http from 'node:http';
const require=createRequire(import.meta.url);const {syncOnce,seal}=require('../desktop/sync.cjs');
const jar=path.resolve('tools/json-20240303.jar');
test('真实 Android Java 协议与 Windows Node IPv6 互通、错误密钥和重放防护',{skip:!existsSync(jar)},async()=>{
 const java=process.env.JAVA_HOME?path.join(process.env.JAVA_HOME,'bin','java.exe'):'java';const javac=process.env.JAVA_HOME?path.join(process.env.JAVA_HOME,'bin','javac.exe'):'javac';
 const out=path.resolve('.local/java-test');mkdirSync(out,{recursive:true});const source='android/app/src/main/java/app/chataibox/mobile/';
 const compile=spawnSync(javac,['-encoding','UTF-8','-cp',jar,'-d',out,'tests/java/android/util/Base64.java',source+'SyncCrypto.java',source+'PeerServer.java',source+'ChatNetwork.java',source+'Providers.java','tests/java/PeerHarness.java','tests/java/StreamHarness.java'],{encoding:'utf8'});assert.equal(compile.status,0,compile.stderr);
 const streamCheck=spawnSync(java,['-cp',out+path.delimiter+jar,'StreamHarness'],{encoding:'utf8'});assert.equal(streamCheck.status,0,streamCheck.stderr);
 const key=randomBytes(32).toString('hex'),child=spawn(java,['-cp',out+path.delimiter+jar,'PeerHarness','39290',key],{stdio:['ignore','pipe','pipe']});
 try{
  await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Java host startup timed out')),12000);child.stdout.once('data',()=>{clearTimeout(timer);resolve();});child.once('error',reject);child.once('exit',code=>{clearTimeout(timer);reject(Error(`Java host exited ${code}`));});});
  const row={id:'cross-runtime',rev:'0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa',type:'chat',title:'中文与 emoji 🙂',created:1};
  const first=await syncOnce({address:'::1',port:39290,key,records:[row]});assert.equal(first[0].title,row.title);
  const second=await syncOnce({address:'::1',port:39290,key,records:[]});assert.equal(second.length,1);
  const unchanged=await syncOnce({address:'::1',port:39290,key,records:[],known:[[row.id,row.rev]]});assert.equal(unchanged.length,0);
  const deleted=await syncOnce({address:'::1',port:39290,key,records:[{...row,rev:'0000000000000002:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa',deleted:true}]});assert.equal(deleted[0].deleted,true);
  await assert.rejects(syncOnce({address:'::1',port:39290,key:randomBytes(32).toString('hex'),records:[]}));
  const request=JSON.stringify(seal({kind:'request',id:randomUUID(),at:Date.now(),records:[]},key));
  const post=()=>new Promise((resolve,reject)=>{const req=http.request('http://[::1]:39290/sync',{method:'POST',headers:{'Content-Length':Buffer.byteLength(request)},timeout:3000},res=>{res.resume();res.on('end',()=>resolve(res.statusCode));});req.on('error',reject);req.on('timeout',()=>req.destroy(Error('Timeout')));req.end(request);});
  assert.equal(await post(),200);await assert.rejects(post());
 }finally{child.kill();}
});
