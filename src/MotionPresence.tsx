import {useEffect,useRef,useState,type ReactNode} from 'react';
/** Keep a closing surface mounted only for its exit transition. */
export function MotionPresence({children}:{children:ReactNode}){
 const open=!!children,[present,setPresent]=useState(open),last=useRef(children);
 if(open)last.current=children;
 useEffect(()=>{if(open){setPresent(true);return;}const timer=window.setTimeout(()=>setPresent(false),matchMedia('(prefers-reduced-motion: reduce)').matches?0:167);return()=>clearTimeout(timer);},[open]);
 if(!open&&!present)return null;
 return <div className={`motion-surface${open?'':' is-leaving'}`} inert={!open||undefined} aria-hidden={!open||undefined}>{open?children:last.current}</div>;
}
