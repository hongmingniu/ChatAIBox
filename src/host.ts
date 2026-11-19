import { Capacitor,registerPlugin } from '@capacitor/core';
export type Source={title:string;url:string};
export type ContextSummary={text:string;covered:number;prefixHash:string;partial?:{id:string;rev:string;offset:number}};
export type ImageAttachment={id:string;name:string;mime:string;dataUrl:string;width?:number;height?:number};
export type SearchTrace={id:string;kind:string;queries:string[];url?:string;status:string};
export type Provider={id:string;name:string;baseUrl:string;apiKey:string;models:string[];vision:boolean;search:boolean;thinking:'deepseek'|'effort'|'none';contextWindow:number;maxOutputTokens:number;asrModel?:string};
export type RecordRow={id:string;rev:string;type:'chat'|'message';deleted?:boolean;title?:string;created:number;chatId?:string;role?:'user'|'assistant';content?:string;reasoning?:string;sources?:Source[];interrupted?:boolean;contextSummary?:ContextSummary;images?:ImageAttachment[];searches?:SearchTrace[];providerId?:string;model?:string};
export type State={schema:number;deviceId:string;records:RecordRow[]};
export type Settings={apiKey:string;model:string;proxy:string;syncEnabled:boolean;syncKey:string;port:number;peerAddress?:string;syncMode?:'relay'|'direct';relayUrl?:string;relayAccessKey?:string;autoCompress?:boolean;contextBudget?:number;providers?:Provider[];activeProviderId?:string;theme?:'system'|'light'|'dark';fontSize?:number;fontFamily?:string;fontScope?:'chat'|'all';fontCode?:boolean;fontFileId?:string;fontFileName?:string;preview?:boolean;ttsVoice?:string;ttsRate?:number};
export type Snapshot={state:State;stateVersion?:number;settings:Settings;platform:string;status:{listening?:boolean;relayConnected?:boolean;phoneOnline?:boolean;lastSync?:number;pending?:boolean;error?:string;addresses?:string[]}};
type Chunk={requestId:string;chunk:string};
declare global {interface Window {desktopHost?:{call:(method:string,args?:unknown)=>Promise<any>;onChange:(cb:()=>void)=>()=>void;onStream:(cb:(chunk:Chunk)=>void)=>()=>void}}}
const native=registerPlugin<{execute:(arg:{method:string;args:unknown})=>Promise<any>;addListener:(event:string,listener:(chunk:Chunk)=>void)=>Promise<{remove:()=>Promise<void>}>}>('ChatHost');
export const isNative=!!window.desktopHost||Capacitor.isNativePlatform();
export async function call(method:string,args:unknown={}):Promise<any>{
  if(window.desktopHost)return window.desktopHost.call(method,args);
  if(Capacitor.isNativePlatform())return native.execute({method,args});
  if(method==='read')return {state:{schema:1,deviceId:crypto.randomUUID(),records:[]},settings:{apiKey:'',model:'deepseek-flash',proxy:'',syncEnabled:false,syncKey:'',port:39281},platform:'preview',status:{error:'浏览器仅供界面预览，请使用 Windows 或 Android 程序'}};
  throw Error('此功能需要安装 Windows 或 Android 程序');
}
export function subscribe(callback:()=>void){return window.desktopHost?.onChange(callback)??(()=>{});}
export async function requestChat(body:unknown,{signal,onChunk,providerId}:{signal?:AbortSignal;onChunk?:(text:string)=>void;providerId?:string}={}){
  if(signal?.aborted)throw new DOMException('已停止','AbortError');
  const requestId=crypto.randomUUID();let off=()=>{};
  if(onChunk){const listener=(chunk:Chunk)=>{if(chunk.requestId===requestId)onChunk(chunk.chunk);};if(window.desktopHost)off=window.desktopHost.onStream(listener);else{const handle=await native.addListener('streamChunk',listener);off=()=>{void handle.remove();};}}
  const cancel=()=>{void call('cancelRequest',{requestId}).catch(()=>{});};signal?.addEventListener('abort',cancel,{once:true});
  try{if(signal?.aborted)throw new DOMException('已停止','AbortError');const result=await call('request',{kind:'chat',body,stream:!!onChunk,requestId,providerId});await new Promise(resolve=>setTimeout(resolve,0));if(signal?.aborted||result.cancelled)throw new DOMException('已停止','AbortError');return result;}finally{signal?.removeEventListener('abort',cancel);off();}
}
