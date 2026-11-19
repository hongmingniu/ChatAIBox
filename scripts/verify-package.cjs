const {spawn}=require('node:child_process');const path=require('node:path');const fs=require('node:fs');
async function main(){
 const profile=path.resolve('.local/package-profile'),exe=process.env.CHATAIBOX_PACKAGE_EXE||path.resolve(`release/${require('../package.json').version}/win-unpacked/ChatAIBox.exe`);
 const child=spawn(exe,['--remote-debugging-port=19329',`--user-data-dir=${profile}`],{stdio:'ignore',windowsHide:true});let socket;
 try{
  let page;for(let i=0;i<80;i++){try{const pages=await(await fetch('http://127.0.0.1:19329/json')).json();page=pages.find(p=>p.type==='page'&&p.url.startsWith('file:'));if(page)break;}catch{}await new Promise(r=>setTimeout(r,150));}if(!page)throw Error('Packaged app did not start');
  socket=new WebSocket(page.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});let sequence=0;const pending=new Map();socket.onmessage=event=>{const msg=JSON.parse(event.data);if(msg.id&&pending.has(msg.id)){pending.get(msg.id)(msg);pending.delete(msg.id);}};
  const evaluate=expression=>new Promise(resolve=>{const id=++sequence;pending.set(id,resolve);socket.send(JSON.stringify({id,method:'Runtime.evaluate',params:{expression,awaitPromise:true,returnByValue:true}}));});
  let loaded;for(let i=0;i<40;i++){loaded=await evaluate('document.body.innerText');if(loaded.result?.result?.value?.includes('你好，今天想聊点什么'))break;await new Promise(r=>setTimeout(r,200));}if(!loaded.result?.result?.value?.includes('你好，今天想聊点什么'))throw Error('Packaged renderer did not load: '+JSON.stringify(loaded));
  const result=await evaluate(`(async()=>{const before=await window.desktopHost.call('read');const row={id:'package-smoke',type:'chat',title:'temporary verification',created:1,rev:'0000000000000001:'+before.state.deviceId};await window.desktopHost.call('write',{records:[row]});return(await window.desktopHost.call('read')).state.records.some(r=>r.id==='package-smoke')})()`);if(!result.result?.result?.value)throw Error('Packaged native storage bridge failed');
  if(fs.existsSync(path.join(profile,'records.json')))throw Error('Windows unexpectedly persisted messages');
  console.log('PASS: packaged Windows app, native bridge, messages held in memory');
 }finally{socket?.close();spawn('taskkill',['/PID',String(child.pid),'/T','/F'],{stdio:'ignore',windowsHide:true});}
}
main().catch(e=>{console.error(e.message);process.exitCode=1;});
