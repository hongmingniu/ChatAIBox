import {call} from './host';
export function WindowControls({maximized,onError}:{maximized:boolean;onError:(message:string)=>void}){
 const act=(action:string)=>{void call('windowControl',{action}).catch(e=>onError(e.message));};
 return <div className="window-controls"><button aria-label="最小化" title="最小化" onClick={()=>act('minimize')}><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h14"/></svg></button><button aria-label={maximized?'还原窗口':'最大化'} title={maximized?'还原':'最大化'} onClick={()=>act('toggleMaximize')}><svg viewBox="0 0 24 24" fill="none" aria-hidden="true">{maximized?<path d="M8 8V4h12v12h-4M4 8h12v12H4z"/>:<rect x="5" y="5" width="14" height="14" rx="1"/>}</svg></button><button className="window-close" aria-label="关闭窗口" title="关闭" onClick={()=>act('close')}><svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18"/></svg></button></div>;
}
