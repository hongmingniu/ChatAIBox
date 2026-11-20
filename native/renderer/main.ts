import {renderMarkdown} from '../../shared/markdown.mjs';
import font from '../../src/assets/fonts/HarmonyOS_Sans_SC_Regular.ttf?url';
import 'katex/dist/katex.min.css';import 'highlight.js/styles/github-dark.css';import './style.css';
const root=document.getElementById('body')!;let sequence=0;
type RenderRequest={markdown:string;dark:boolean;fontSize:number;version:number};let latest:RenderRequest|undefined,deferred=false,rendering=false;
function selecting(){const selection=window.getSelection();return document.hasFocus()&&selection&&!selection.isCollapsed&&!!selection.anchorNode&&root.contains(selection.anchorNode);}
function post(value:unknown){const w=window as any;if(w.chrome?.webview)w.chrome.webview.postMessage(value);else if(w.unoWebView)w.unoWebView.postMessage(JSON.stringify(value));}
const face=new FontFace('HarmonyOS Sans SC',`url("${font}")`);void face.load().then(f=>{document.fonts.add(f);height();}).catch(()=>height());
function height(){post({type:'height',value:Math.ceil(root.getBoundingClientRect().height)+6});}
async function renderLatest(){
 if(rendering)return;rendering=true;
 try{let applied:RenderRequest|undefined;while(latest&&latest!==applied){
  if(selecting()){deferred=true;return;}const request=latest;const own=++sequence;
  document.documentElement.dataset.theme=request.dark?'dark':'light';document.documentElement.style.fontSize=request.fontSize+'px';
  try{const html=await renderMarkdown(request.markdown);if(latest!==request||own!==sequence)continue;if(selecting()){deferred=true;return;}
   root.innerHTML=html;if(request.markdown.trim()&&!root.textContent?.trim()&&!root.querySelector('img,hr,input'))root.textContent=request.markdown;
   deferred=false;post({type:'rendered',version:request.version,height:Math.ceil(root.getBoundingClientRect().height)+6});requestAnimationFrame(height);
  }catch(error){if(latest===request)post({type:'renderError',version:request.version,message:String(error instanceof Error?error.message:error).slice(0,300)});}
  applied=request;
 }}finally{rendering=false;}
}
(window as any).nativeRender=(request:RenderRequest)=>{latest=request;void renderLatest();};

document.addEventListener('selectionchange',()=>{const active=!!selecting();if(active)post({type:'selection',active:true});if(deferred&&!active&&latest)void (window as any).nativeRender(latest);});
window.addEventListener('blur',()=>{if(deferred&&latest)void (window as any).nativeRender(latest);});
document.addEventListener('contextmenu',event=>{const text=window.getSelection()?.toString();if(text){event.preventDefault();post({type:'selectionMenu',text,x:event.clientX,y:event.clientY});}});
(window as any).nativeImage=(id:string,dataUrl:string)=>{const target=document.querySelector(`[data-request-id="${id}"]`);if(!target||!/^data:image\/(png|jpeg|webp|gif);base64,/.test(dataUrl))return;const image=document.createElement('img');image.alt='图片';image.src=dataUrl;image.onload=height;target.replaceWith(image);};
document.addEventListener('click',event=>{const target=event.target as HTMLElement;const action=target.closest<HTMLElement>('[data-code-action]');if(action){const text=action.closest('.code-shell')?.querySelector('code')?.textContent||'';post({type:action.dataset.codeAction,text,language:action.dataset.language||'text'});return;}const image=target.closest<HTMLElement>('[data-image-url]');if(image){const id=crypto.randomUUID();image.dataset.requestId=id;post({type:'image',id,url:image.dataset.imageUrl});return;}const link=target.closest<HTMLAnchorElement>('a');if(link){event.preventDefault();if(link.getAttribute('href')?.startsWith('#'))document.getElementById(link.hash.slice(1))?.scrollIntoView({behavior:'smooth'});else post({type:'link',url:link.href});}});
new ResizeObserver(height).observe(root);post({type:'ready'});

let fileDrag=false;
function showFileDrag(active:boolean){if(fileDrag===active)return;fileDrag=active;document.documentElement.classList.toggle('file-drag-active',active);post({type:'fileDrag',active});}
function hasFiles(event:DragEvent){return Array.from(event.dataTransfer?.types||[]).includes('Files');}
document.addEventListener('dragover',event=>{if(!hasFiles(event))return;event.preventDefault();event.stopPropagation();if(event.dataTransfer)event.dataTransfer.dropEffect='copy';showFileDrag(true);});
document.addEventListener('dragleave',event=>{if(!event.relatedTarget)showFileDrag(false);});
window.addEventListener('blur',()=>showFileDrag(false));
document.addEventListener('drop',event=>{if(!hasFiles(event))return;event.preventDefault();event.stopPropagation();showFileDrag(false);const files=Array.from(event.dataTransfer?.files||[]);const bridge=(window as any).chrome?.webview;if(files.length>10){post({type:'dropUnsupported'});return;}if(bridge?.postMessageWithAdditionalObjects)bridge.postMessageWithAdditionalObjects({type:'dropFiles'},files);else post({type:'dropUnsupported'});});
