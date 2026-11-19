const {app,BrowserWindow}=require('electron');const path=require('node:path');const fs=require('node:fs');
const folder=path.join(__dirname,'../.local');fs.mkdirSync(folder,{recursive:true});app.setPath('userData',path.join(folder,'ui-profile'));
require('../desktop/main.cjs');
app.whenReady().then(async()=>{
 try{
  let win;for(let i=0;i<100;i++){win=BrowserWindow.getAllWindows()[0];if(win&&!win.webContents.isLoading()&&win.webContents.getURL())break;await new Promise(r=>setTimeout(r,100));}
  await new Promise(r=>setTimeout(r,1300));
  const text=await win.webContents.executeJavaScript('document.body.innerText');if(!text.includes('你好，今天想聊点什么'))throw Error('Welcome UI not rendered');
  fs.writeFileSync(path.join(folder,'desktop.png'),(await win.webContents.capturePage()).toPNG());
  await win.webContents.executeJavaScript("[...document.querySelectorAll('button')].find(b=>b.textContent==='设置').click()");await new Promise(r=>setTimeout(r,250));
  const modal=await win.webContents.executeJavaScript("document.querySelector('.modal').innerText");if(!modal.includes('API Key'))throw Error('Settings missing');
  await win.webContents.executeJavaScript("document.querySelector('.modal-close').click()");
  win.setMinimumSize(320,500);win.setSize(410,860);await new Promise(r=>setTimeout(r,500));
  fs.writeFileSync(path.join(folder,'mobile.png'),(await win.webContents.capturePage()).toPNG());
  await win.webContents.executeJavaScript("document.querySelector('.mobile-menu').click()");await new Promise(r=>setTimeout(r,150));
  if(!(await win.webContents.executeJavaScript("document.querySelector('.sidebar').classList.contains('open')")))throw Error('Mobile menu did not open');
  console.log('PASS: desktop welcome, API settings, mobile layout and sidebar');app.quitting=true;app.quit();
 }catch(e){console.error(e);app.quitting=true;app.exit(1);}
});
