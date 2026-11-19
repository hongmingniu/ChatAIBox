const {app,BrowserWindow,ipcMain,safeStorage,Menu,dialog,shell,clipboard}=require('electron');
const fs=require('node:fs');const path=require('node:path');const os=require('node:os');
const {randomUUID,randomBytes}=require('node:crypto');
const {createClient}=require('./deepseek.cjs');
const {syncOnce,peerUrl}=require('./sync.cjs');
let win,store,settings,root,syncTimer,syncBusy=false,syncEpoch=0,stateVersion=0,pending=false,status={listening:false,lastSync:0,error:''},mergeRecords;
const client=createClient(()=>settings);
const media=require('./media.cjs');
const acknowledged=new Map();
const {RelayClient}=require('./relay.cjs');let relayClient;
function atomic(file,data){fs.writeFileSync(file+'.tmp',data);fs.renameSync(file+'.tmp',file);}
function saveSettings(){if(!safeStorage.isEncryptionAvailable())throw Error('系统凭据加密不可用，无法保存 API Key');atomic(path.join(root,'settings.bin'),safeStorage.encryptString(JSON.stringify(settings)));}
function notify(){if(win&&!win.isDestroyed())win.webContents.send('changed');}
function merge(rows){const next=mergeRecords(store.records,rows);if(next.length!==store.records.length||next.some((r,i)=>r.id!==store.records[i].id||r.rev!==store.records[i].rev)){store.records=next;stateVersion++;notify();}return store;}
async function synchronize(){
  if(syncBusy||!settings.syncEnabled)return;syncBusy=true;
  const sent=store.records.filter(r=>acknowledged.get(r.id)!==r.rev),known=store.records.map(r=>[r.id,r.rev]),epoch=syncEpoch;
  try{const remote=await ((settings.syncMode||'relay')==='relay'?relayClient.request({records:sent,known}):syncOnce({address:settings.peerAddress,port:Number(settings.port),key:settings.syncKey,records:sent,known}));if(epoch!==syncEpoch)return;merge(remote);for(const row of [...sent,...remote])acknowledged.set(row.id,row.rev);pending=store.records.some(r=>acknowledged.get(r.id)!==r.rev);status={...status,lastSync:Date.now(),error:'',pending};}
  catch(e){if(epoch===syncEpoch)status={...status,error:e.code?`连接手机失败：${e.code}，请检查 IPv6 与后台服务`:e.message,pending};}finally{syncBusy=false;notify();}
}
function restartSync(){syncEpoch++;clearInterval(syncTimer);relayClient?.close();relayClient=undefined;status={...status,error:'',relayConnected:false,phoneOnline:false};if(settings.syncEnabled){if((settings.syncMode||'relay')==='relay'&&!settings.relayUrl){status.error='请填写转发服务地址';notify();return;}if((settings.syncMode||'relay')==='relay')relayClient=new RelayClient({url:settings.relayUrl,key:settings.syncKey,accessKey:settings.relayAccessKey||"",proxy:settings.proxy||process.env.HTTPS_PROXY||process.env.HTTP_PROXY,onPresence:value=>{status={...status,relayConnected:value.connected,phoneOnline:value.phoneOnline};notify();if(value.phoneOnline)setTimeout(synchronize,100);}});syncTimer=setInterval(synchronize,20000);synchronize();}}
app.whenReady().then(async()=>{
  ({mergeRecords}=await import('../shared/records.mjs'));root=app.getPath('userData');fs.mkdirSync(root,{recursive:true});
  const settingFile=path.join(root,'settings.bin');
  settings=fs.existsSync(settingFile)?JSON.parse(safeStorage.decryptString(fs.readFileSync(settingFile))):{apiKey:'',model:'deepseek-flash',proxy:'',syncKey:randomBytes(32).toString('hex'),syncEnabled:false,port:39281,peerAddress:''};
  store={schema:1,deviceId:randomUUID(),records:[]};
  ipcMain.handle('host',async(event,{method,args})=>{
    if(event.sender!==win?.webContents||event.senderFrame!==win.webContents.mainFrame)throw Error('不允许的访问');
    switch(method){
      case 'read':return {state:args.knownVersion===stateVersion?undefined:store,stateVersion,settings,platform:'windows',status:{...status,addresses:Object.values(os.networkInterfaces()).flat().filter(a=>a&&!a.internal&&a.family==='IPv6'&&!a.address.startsWith('fe80:')).map(a=>a.address)}};
      case 'write':{const baseVersion=stateVersion;pending=true;status.pending=true;const updated=merge(args.records);const result={state:args.returnState===false?undefined:updated,baseVersion,stateVersion};synchronize();return result;}
      case 'sync':await synchronize();return {};
      case 'settings':{
        const next={...settings,...args};const {validateProviders}=await import('../shared/providers.mjs');next.providers=validateProviders(next.providers??[]);if(!next.model||next.model.length>160)throw Error('模型无效');
        if(!/^[a-f0-9]{64}$/.test(next.syncKey)||!Number.isInteger(Number(next.port))||next.port<1024||next.port>65535)throw Error('同步参数无效');
        if(next.proxy&&!/^http:\/\/(127\.0\.0\.1|localhost):\d{1,5}\/?$/.test(next.proxy))throw Error('代理格式应为 http://127.0.0.1:端口');
        if(!['relay','direct'].includes(next.syncMode||'relay'))throw Error('同步模式无效');const {relayUrl}=await import('../shared/relay.mjs');next.relayUrl=relayUrl(next.relayUrl||undefined);if(next.syncEnabled&&(next.syncMode||'relay')==='relay'&&!next.relayUrl)throw Error('请填写转发服务地址');if(next.syncEnabled&&next.syncMode==='direct')peerUrl(next.peerAddress,Number(next.port));
        if(next.relayAccessKey&&!/^[A-Za-z0-9_-]{32,256}$/.test(next.relayAccessKey))throw Error("服务器访问密钥须为 32–256 位字母、数字、下划线或短横线");
        const pairingChanged=next.port!==settings.port||next.syncKey!==settings.syncKey||next.peerAddress!==settings.peerAddress;
        const changed=pairingChanged||next.syncEnabled!==settings.syncEnabled||next.syncMode!==settings.syncMode||next.relayUrl!==settings.relayUrl||next.relayAccessKey!==settings.relayAccessKey||next.proxy!==settings.proxy;
        if(pairingChanged&&settings.peerAddress&&pending&&store.records.length)throw Error('请先同步当前消息，再更改配对；或退出后重新配对');
        if(pairingChanged&&settings.peerAddress&&!pending){store.records=[];stateVersion++;acknowledged.clear();}
        settings=next;saveSettings();if(changed)restartSync();return {settings};
      }
      case 'request':return client.request(args,chunk=>{if(win&&!win.isDestroyed())win.webContents.send('stream-chunk',chunk);});
      case 'cancelRequest':client.cancel(args.requestId);media.cancel(args.requestId);return {};
      case 'edgeTts':return media.edge(args,settings);
      case 'asr':case 'fetchImage':return media.media(method,args,settings);
      case 'microphone':return {};
      case 'clipboardRead':return {text:clipboard.readText()};
      case 'clipboardWrite':clipboard.writeText(String(args.text));return {};
      case 'download':{const filename=path.basename(String(args.filename||'download.txt'));const choice=await dialog.showSaveDialog(win,{defaultPath:filename});if(!choice.canceled)fs.writeFileSync(choice.filePath,args.base64?Buffer.from(args.base64,'base64'):String(args.text??''));return {saved:!choice.canceled};}
      case 'openLink':{const url=new URL(args.url);if(!['https:','http:'].includes(url.protocol))throw Error('链接协议无效');await shell.openExternal(url.href);return {};}
      case 'export':{const choice=await dialog.showSaveDialog(win,{defaultPath:'ChatAIBox-backup.json',filters:[{name:'JSON',extensions:['json']}]});if(!choice.canceled)atomic(choice.filePath,JSON.stringify({schema:1,records:store.records},null,2));return {saved:!choice.canceled};}
      case 'import':{const choice=await dialog.showOpenDialog(win,{filters:[{name:'JSON',extensions:['json']}],properties:['openFile']});if(choice.canceled)return {};if(fs.statSync(choice.filePaths[0]).size>64000000)throw Error('备份文件过大');const data=JSON.parse(fs.readFileSync(choice.filePaths[0],'utf8'));if(data.schema!==1)throw Error('备份版本不支持');pending=true;status.pending=true;const result={state:merge(data.records)};synchronize();return result;}
      default:throw Error('未知操作');
    }
  });
  win=new BrowserWindow({width:1250,height:850,minWidth:760,minHeight:600,title:'ChatAIBox',backgroundColor:'#f6f7fb',webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
  win.webContents.session.webRequest.onHeadersReceived((details,callback)=>callback({responseHeaders:{...details.responseHeaders,'Content-Security-Policy':["default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; media-src 'self' blob: data:; worker-src 'self' blob:; object-src 'none'; frame-src 'none'; base-uri 'none'"]}}));
  win.webContents.session.setPermissionRequestHandler((contents,permission,callback)=>callback(contents===win.webContents&&permission==='media'));
  win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',event=>event.preventDefault());
  Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'ChatAIBox',submenu:[{label:'显示窗口',click:()=>win.show()},{label:'退出',click:()=>{app.quitting=true;app.quit();}}]},{label:'编辑',submenu:[{role:'undo'},{role:'redo'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]}]));
  win.on('close',e=>{if(settings.syncEnabled&&!app.quitting){e.preventDefault();win.minimize();}});
  await win.loadFile(path.join(__dirname,'../dist/index.html'));restartSync();
}).catch(e=>{dialog.showErrorBox('启动失败',e.message);app.quit();});
app.on('window-all-closed',()=>app.quit());app.on('before-quit',event=>{
  if(pending&&win&&!win.isDestroyed()) {const response=dialog.showMessageBoxSync(win,{type:'warning',buttons:['返回并同步','放弃未同步消息并退出'],defaultId:0,cancelId:0,message:'部分消息尚未保存到手机',detail:'电脑只保留内存副本。退出后，未同步到手机的消息将丢失。'});if(response===0){event.preventDefault();app.quitting=false;win.show();return;}}
  app.quitting=true;clearInterval(syncTimer);relayClient?.close();
});
