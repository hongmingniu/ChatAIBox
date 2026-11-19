import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire}from 'node:module';
import {randomBytes}from 'node:crypto';
import {mergeRecords,validateRecords}from '../shared/records.mjs';
import {shareId,parseShare}from '../shared/import.mjs';
const require=createRequire(import.meta.url);const {seal,open,peerUrl,startSync,syncOnce}=require('../desktop/sync.cjs');
const device='aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa';
const record=(id,clock=1)=>({id,type:'chat',title:'对话',rev:`${String(clock).padStart(16,'0')}:${device}`,created:1});
test('并发创建、删除墓碑和重连合并不丢失记录',()=>{
 const a=[record('a'),record('b')],b=[{...record('a',2),deleted:true},record('c')];
 const merged=mergeRecords(a,b);assert.deepEqual(merged,mergeRecords(b,a));assert.equal(merged.length,3);assert.equal(merged.find(r=>r.id==='a').deleted,true);assert.deepEqual(merged,mergeRecords(merged,a));
});
test('只同步聊天字段，不携带配置凭据',()=>{assert.equal(validateRecords([{...record('a'),apiKey:'secret',syncKey:'secret'}])[0].apiKey,undefined);assert.throws(()=>validateRecords([{id:'a',type:'message',rev:'invalid'}]));});
test('AES-GCM 密文不能被篡改或用错误密钥打开',()=>{
 const key=randomBytes(32).toString('hex');const encrypted=seal({records:[record('a')],text:'中文🙂'},key);assert.equal(open(encrypted,key).text,'中文🙂');assert.throws(()=>open(encrypted,randomBytes(32).toString('hex')));const bytes=Buffer.from(encrypted.ciphertext,'base64');bytes[0]^=1;assert.throws(()=>open({...encrypted,ciphertext:bytes.toString('base64')},key));
});
test('分享地址限制为官方 HTTPS 分享页，拒绝本地和伪造域名',()=>{assert.equal(shareId('https://chat.deepseek.com/share/abcdef?foo=1'),'abcdef');for(const url of ['http://chat.deepseek.com/share/abcdef','https://chat.deepseek.com.evil.test/share/abcdef','https://127.0.0.1/share/abcdef','https://chat.deepseek.com:8080/share/abcdef'])assert.throws(()=>shareId(url));});
test('分享导入兼容文字和 fragments，不把思考误当回答',()=>{
 const result=parseShare({code:0,data:{biz_code:0,biz_data:{title:'示例',chat_messages:[{role:'USER',content:'你好'},{role:'ASSISTANT',fragments:[{type:'THINK',content:'思考'},{type:'RESPONSE',content:'你好！'}]}]}}});assert.equal(result.messages.length,2);assert.equal(result.messages[1].content,'你好！');assert.equal(result.messages[1].reasoning,'思考');assert.throws(()=>parseShare({data:{biz_code:100}}));
});
test('IPv6 地址校验',()=>{assert.equal(peerUrl('::1',39281),'http://[::1]:39281/sync');assert.throws(()=>peerUrl('example.com',39281));assert.throws(()=>peerUrl('::1',80));assert.throws(()=>peerUrl('[::1]',39281));});
test('IPv6 回环端到端：双方新增、删除补同步与拒绝错误密钥',async()=>{
 let phone=[record('phone')];const key=randomBytes(32).toString('hex');
 const server=startSync({port:39289,key,read:()=>phone,merge:rows=>{phone=mergeRecords(phone,rows);},onStatus:()=>{}});await new Promise((resolve,reject)=>{server.once('listening',resolve);server.once('error',reject);});
 try{let result=await syncOnce({address:'::1',port:39289,key,records:[record('desktop')]});assert.equal(result.length,2);assert.equal(phone.length,2);result=await syncOnce({address:'::1',port:39289,key,records:[{...record('phone',2),deleted:true}]});assert.equal(result.find(r=>r.id==='phone').deleted,true);await assert.rejects(syncOnce({address:'::1',port:39289,key:randomBytes(32).toString('hex'),records:[]}));}finally{await new Promise(resolve=>server.close(resolve));}
});
