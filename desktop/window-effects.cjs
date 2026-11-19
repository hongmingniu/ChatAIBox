const os=require('node:os'),path=require('node:path');
// Native composition sits behind Chromium; renderer text stays fully opaque.
function attachWindowEffects(win,nativeTheme,onChange=()=>{},getPreference=()=> 'acrylic'){
 const state={material:'none',corners:'square',radius:0};
 if(process.platform!=='win32')return {state,refresh(){},close(){}};
 let k,api,composition,disposed=false,lastShape='',timer;
 const build=Number(os.release().split('.')[2]||0);
 const handle=()=>{const b=win.getNativeWindowHandle();return b.length===8?b.readBigUInt64LE():BigInt(b.readUInt32LE());};
 try{
  k=require('koffi');const user=k.load('user32.dll'),dwm=k.load('dwmapi.dll'),gdi=k.load('gdi32.dll');
  const Accent=k.struct('ChatAccent',{state:'int32',flags:'uint32',color:'uint32',animation:'uint32'});
  k.struct('ChatCompositionData',{attribute:'int32',data:'void *',size:'size_t'});
  k.struct('ChatWindowRect',{left:'int32',top:'int32',right:'int32',bottom:'int32'});
  api={Accent,setComposition:user.func('int __stdcall SetWindowCompositionAttribute(void *hwnd, ChatCompositionData *data)'),setAttribute:dwm.func('int __stdcall DwmSetWindowAttribute(void *hwnd, uint32 attribute, void *value, uint32 size)'),rect:user.func('int __stdcall GetWindowRect(void *hwnd, _Out_ ChatWindowRect *rect)'),dpi:user.func('uint32 __stdcall GetDpiForWindow(void *hwnd)'),round:gdi.func('void * __stdcall CreateRoundRectRgn(int,int,int,int,int,int)'),setRegion:user.func('int __stdcall SetWindowRgn(void *hwnd, void *region, int redraw)'),delete:gdi.func('int __stdcall DeleteObject(void *object)')};
  if(build>=18362&&build<22621){
   const dll=k.load(path.join(__dirname.replace(/app\.asar(?=[\\/])/,'app.asar.unpacked'),'native/bin/win32-x64/backdrop.dll'));
   api.create=dll.func('void * CreateBackdrop(void *)');api.size=dll.func('void ResizeBackdrop(void *,float,float,float,int)');api.destroy=dll.func('void DestroyBackdrop(void *)');
   composition=api.create(handle());
  }
 }catch{ /* Keep opaque native corners if the composition helper is unavailable. */ }
 const setAccent=accent=>{if(!api)return false;const ptr=k.alloc(api.Accent,1);try{k.encode(ptr,api.Accent,{state:accent,flags:0,color:0,animation:0});return !!api.setComposition(handle(),{attribute:19,data:ptr,size:k.sizeof(api.Accent)});}finally{k.free(ptr);}};
 const corners=()=>{
  if(disposed||win.isDestroyed()||!api)return;
  try{
   const hwnd=handle(),max=win.isMaximized()||win.isFullScreen(),r=Buffer.alloc(4);r.writeUInt32LE(max?1:2);
   const native=build>=22000&&api.setAttribute(hwnd,33,r,4)===0,rect={};if(!api.rect(hwnd,rect))return;
   const scale=(api.dpi(hwnd)||96)/96,width=rect.right-rect.left,height=rect.bottom-rect.top,radius=max?0:native?8:12;
   if(composition)api.size(composition,width,height,radius*scale,state.material!=='none'?1:0);
   if(native||max){if(lastShape!=='native-'+max)api.setRegion(hwnd,null,1);lastShape='native-'+max;state.corners=max?'square':'native';state.radius=radius;return;}
   const key=width+':'+height+':'+scale;if(key===lastShape)return;
   const region=api.round(0,0,width+1,height+1,Math.round(24*scale),Math.round(24*scale));
   if(region&&api.setRegion(hwnd,region,1)){lastShape=key;state.corners='region';state.radius=12;}else if(region)api.delete(region);
  }catch{state.corners='square';state.radius=0;}
 };
 const refresh=()=>{
  if(disposed||win.isDestroyed())return;const previous=JSON.stringify(state);
  try{
   const preference=getPreference(),reduced=preference==='opaque'||nativeTheme.shouldUseHighContrastColors||(preference==='system'&&nativeTheme.prefersReducedTransparency);
   state.material='none';
   if(build>=22621){win.setBackgroundMaterial(reduced?'none':'acrylic');if(!reduced)state.material='acrylic';}
   else if(composition){if(setAccent(reduced?0:5)&&!reduced)state.material='acrylic';}
   win.setBackgroundColor(state.material==='none'?(nativeTheme.shouldUseDarkColors?'#252321':'#e8e3df'):'#00000000');
  }catch{state.material='none';try{setAccent(0);}catch{}win.setBackgroundColor(nativeTheme.shouldUseDarkColors?'#252321':'#e8e3df');}
  corners();if(JSON.stringify(state)!==previous)onChange();
 };
 const resize=()=>{if(!timer)timer=setTimeout(()=>{timer=undefined;const old=JSON.stringify(state);corners();if(old!==JSON.stringify(state))onChange();},16);};
 const reveal=()=>{lastShape='';resize();};
 win.on('resize',resize);win.on('show',reveal);win.on('focus',reveal);win.on('maximize',refresh);win.on('unmaximize',refresh);win.on('restore',refresh);nativeTheme.on('updated',refresh);refresh();
 return {state,refresh,close(){if(disposed)return;disposed=true;clearTimeout(timer);nativeTheme.removeListener('updated',refresh);win.removeListener('resize',resize);win.removeListener('show',reveal);win.removeListener('focus',reveal);win.removeListener('maximize',refresh);win.removeListener('unmaximize',refresh);win.removeListener('restore',refresh);if(composition){api.destroy(composition);composition=null;}}};
}
module.exports={attachWindowEffects};
