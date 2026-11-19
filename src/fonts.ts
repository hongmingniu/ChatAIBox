import type {Settings} from './host';
export type FontSettings=Pick<Settings,'fontFamily'|'fontSize'|'fontScope'|'fontCode'|'fontFileId'|'fontFileName'>;
export const SYSTEM_FONT='"HarmonyOS Sans SC", "Segoe UI", "Microsoft YaHei", system-ui, sans-serif';
export function fontStack(name='system'){
 if(name==='system')return SYSTEM_FONT;
 if(name==='serif')return 'Georgia, "Noto Serif CJK SC", serif';
 if(name==='mono')return 'Consolas, "Noto Sans Mono", monospace';
 const generic=new Set(['serif','sans-serif','monospace','cursive','fantasy','system-ui','ui-serif','ui-sans-serif','ui-monospace','ui-rounded','emoji','math','fangsong']);
 const families=(name.match(/"[^"\n]*"|'[^'\n]*'|[^,]+/g)||[]).map(s=>s.trim().replace(/^(['"])(.*)\1$/,'$2').replace(/[\x00-\x1f]/g,'')).filter(Boolean).slice(0,12);
 return [...families.map(s=>generic.has(s)?s:JSON.stringify(s)),SYSTEM_FONT].join(', ');
}
function database():Promise<IDBDatabase>{return new Promise((resolve,reject)=>{const request=indexedDB.open('chataibox-fonts',1);request.onupgradeneeded=()=>request.result.createObjectStore('fonts');request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(Error('无法打开本机字体库'));});}
export async function storeFont(id:string,file:Blob){const db=await database();try{await new Promise<void>((resolve,reject)=>{const tx=db.transaction('fonts','readwrite');tx.objectStore('fonts').put(file,id);tx.oncomplete=()=>resolve();tx.onerror=()=>reject(Error('字体文件保存失败，可能是本机存储空间不足'));tx.onabort=()=>reject(Error('字体文件保存已中止'));});}finally{db.close();}}
async function readFont(id:string):Promise<Blob>{const db=await database();try{return await new Promise((resolve,reject)=>{const tx=db.transaction('fonts','readonly'),request=tx.objectStore('fonts').get(id);request.onsuccess=()=>request.result instanceof Blob?resolve(request.result):reject(Error('导入的字体文件不存在，请重新选择字体文件'));request.onerror=()=>reject(Error('读取字体文件失败'));});}finally{db.close();}}
const loaded=new Map<string,Promise<FontFace>>();
export function loadFont(id:string,file?:Blob){if(!/^[a-f0-9-]{36}$/.test(id))return Promise.reject(Error('字体标识无效'));if(!loaded.has(id)){const job=(async()=>{const blob=file||await readFont(id);const face=await new FontFace('ChatAIBoxFont-'+id,await blob.arrayBuffer()).load();document.fonts.add(face);return face;})();loaded.set(id,job);job.catch(()=>loaded.delete(id));}return loaded.get(id)!;}
export async function applyFonts(settings:Settings,isCurrent:()=>boolean){const root=document.documentElement;const fallback=fontStack(settings.fontFamily==='file'?'system':settings.fontFamily);let family=fallback;
 const commit=()=>{root.style.setProperty('--message-font',family);root.style.setProperty('--message-font-size',Math.max(12,Math.min(36,settings.fontSize||15))+'px');root.style.setProperty('--app-font',settings.fontScope==='all'?family:SYSTEM_FONT);root.style.setProperty('--code-font',settings.fontCode?family:'Consolas, "Noto Sans Mono", monospace');};if(isCurrent())commit();
 if(settings.fontFamily==='file'&&settings.fontFileId){const face=await loadFont(settings.fontFileId);family=JSON.stringify(face.family)+', '+SYSTEM_FONT;}
 if(isCurrent())commit();
}
