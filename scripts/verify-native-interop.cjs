const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),assert=require('node:assert/strict');
const {spawn,execFileSync}=require('node:child_process');const {createHash,randomUUID}=require('node:crypto');
const {seal,open,syncOnce}=require('../desktop/sync.cjs');const {roomId,RelayClient}=require('../desktop/relay.cjs');const {createRelay}=require('../server/relay.cjs');
const dotnet=process.env.DOTNET_EXE||'dotnet',dll=path.resolve('native/ChatAIBox.Core.Checks/bin/Debug/net10.0/ChatAIBox.Core.Checks.dll');
const folder=fs.mkdtempSync(path.join(os.tmpdir(),'chataibox-native-')),key='a'.repeat(64),children=[];let relay,desktop;
const record={id:'fixture',rev:'0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa',type:'message',chatId:'chat',role:'user',content:'跨端同步🙂',created:1};
function native(value){const input=path.join(folder,'in.json'),output=path.join(folder,'out.json');fs.writeFileSync(input,JSON.stringify(value));execFileSync(dotnet,[dll,'--crypto',input,output],{stdio:'pipe',windowsHide:true});return JSON.parse(fs.readFileSync(output,'utf8'));}
async function start(args){const child=spawn(dotnet,[dll,...args],{stdio:['ignore','pipe','pipe'],windowsHide:true});children.push(child);await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Native test server startup timeout')),15000);child.once('exit',code=>{clearTimeout(timer);reject(Error('Native test process exited '+code));});child.stdout.on('data',raw=>{if(raw.toString().includes('READY')){clearTimeout(timer);resolve();}});child.stderr.on('data',()=>{});});return child;}
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{try{
 assert.equal(native({operation:'room',key}),roomId(key));
 const value={kind:'request',id:randomUUID(),at:Date.now(),records:[record]};assert.deepEqual(native({operation:'open',key,value:seal(value,key)}),value);assert.deepEqual(open(native({operation:'seal',key,value}),key),value);console.log('PASS JavaScript / C# AES-GCM and HMAC compatibility');
 await start(['--serve-direct','39471']);const received=await syncOnce({address:'::1',port:39471,key,records:[record],known:[]});assert.equal(received[0].content,record.content);const empty=await syncOnce({address:'::1',port:39471,key,records:[],known:[[record.id,record.rev]]});assert.equal(empty.length,0);console.log('PASS legacy Windows client / native IPv6 phone and delta acknowledgement');
 const access='native_fixture_'+randomUUID().replaceAll('-','');relay=createRelay({accessKeyHash:createHash('sha256').update(access).digest('hex')});await new Promise(resolve=>relay.server.listen(0,'127.0.0.1',resolve));const base='http://127.0.0.1:'+relay.server.address().port;
 await start(['--serve-relay',base,access]);desktop=new RelayClient({url:base,key,accessKey:access,proxy:'',allowLocal:true});for(let i=0;i<150&&!desktop.online;i++)await sleep(100);assert.equal(desktop.online,true);const rows=await desktop.request({records:[record],known:[]});assert.equal(rows[0].content,record.content);console.log('PASS protected relay / legacy Windows client / native phone');
 console.log('ALL_NATIVE_INTEROP_CHECKS_PASSED');
}finally{desktop?.close();for(const child of children)child.kill();if(relay)await relay.close();fs.rmSync(folder,{recursive:true,force:true});}})().catch(error=>{console.error(error.message);process.exitCode=1;});
