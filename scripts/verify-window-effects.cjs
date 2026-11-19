const {app,BrowserWindow,desktopCapturer,screen}=require('electron'),{EventEmitter}=require('node:events'),assert=require('node:assert/strict'),path=require('node:path');
const {attachWindowEffects}=require('../desktop/window-effects.cjs');
app.setPath('userData',path.resolve('.local/window-effects-'+Date.now()));
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
app.whenReady().then(async()=>{
 let front,back,effects;
 try{
  const bounds={x:150,y:100,width:900,height:650};
  back=new BrowserWindow({...bounds,width:920,height:670,frame:false,alwaysOnTop:true,show:false});
  await back.loadURL('data:text/html,<html style="background:%23ff3030"></html>');back.show();
  front=new BrowserWindow({...bounds,frame:false,backgroundColor:'#00000000',alwaysOnTop:true,show:false});
  const theme=Object.assign(new EventEmitter(),{shouldUseDarkColors:false,shouldUseHighContrastColors:false,prefersReducedTransparency:false});let preference='acrylic';
  effects=attachWindowEffects(front,theme,()=>{},()=>preference);
  await front.loadURL('data:text/html,<html style="background:transparent"><body style="background:transparent"></body></html>');front.show();front.moveTop();
  const shot=async()=>{await pause(500);const b=front.getBounds(),d=screen.getDisplayMatching(b),sources=await desktopCapturer.getSources({types:['screen'],thumbnailSize:{width:d.size.width*d.scaleFactor,height:d.size.height*d.scaleFactor}}),src=sources.find(s=>s.display_id===String(d.id))||sources[0],f=src.thumbnail.getSize().width/d.bounds.width,img=src.thumbnail.crop({x:Math.round((b.x-d.bounds.x)*f),y:Math.round((b.y-d.bounds.y)*f),width:Math.round(b.width*f),height:Math.round(b.height*f)}),z=img.getSize(),bits=img.toBitmap(),pixel=(x,y)=>{const i=(y*z.width+x)*4;return[bits[i+2],bits[i+1],bits[i]];};return{center:pixel(Math.floor(z.width*.5),Math.floor(z.height*.5)),corners:[[0,0],[z.width-1,0],[0,z.height-1],[z.width-1,z.height-1]].map(([x,y])=>pixel(x,y))};};
  assert.equal(effects.state.material,'acrylic','Run on a Windows version supporting native glass');
  const restoredRadius=effects.state.radius;const red=await shot();await back.webContents.executeJavaScript("document.documentElement.style.background='#2040ff'");front.moveTop();const blue=await shot(),distance=(a,b)=>a.reduce((n,v,i)=>n+Math.abs(v-b[i]),0);
  assert.ok(distance(red.center,blue.center)>25,'Underlying window must remain visible');for(const corner of blue.corners)assert.ok(distance(corner,[32,64,255])<4,'Backdrop must not leak outside native corners');
  preference='opaque';effects.refresh();assert.equal(effects.state.material,'none');const opaqueBlue=await shot();await back.webContents.executeJavaScript("document.documentElement.style.background='#ff3030'");front.moveTop();const opaqueRed=await shot();assert.ok(distance(opaqueRed.center,opaqueBlue.center)<3);
  preference='system';theme.prefersReducedTransparency=true;effects.refresh();assert.equal(effects.state.material,'none');theme.prefersReducedTransparency=false;effects.refresh();assert.equal(effects.state.material,'acrylic');
  preference='acrylic';theme.prefersReducedTransparency=true;effects.refresh();assert.equal(effects.state.material,'acrylic');theme.shouldUseHighContrastColors=true;effects.refresh();assert.equal(effects.state.material,'none');theme.shouldUseHighContrastColors=false;effects.refresh();
  front.maximize();await pause(300);assert.equal(effects.state.radius,0);front.unmaximize();await pause(300);assert.equal(effects.state.radius,restoredRadius);front.setSize(960,700);await pause(300);assert.equal(effects.state.radius,restoredRadius);assert.ok(front.isResizable());
  console.log(JSON.stringify({pass:true,underlyingWindowVisible:true,fourCornersClipped:true,opaque:true,followSystem:true,highContrast:true,maximizeAndResize:true}));effects.close();front.destroy();back.destroy();app.quit();
 }catch(error){console.error(error.message);effects?.close();front?.destroy();back?.destroy();app.exit(1);}
});setTimeout(()=>app.exit(2),60000).unref();
