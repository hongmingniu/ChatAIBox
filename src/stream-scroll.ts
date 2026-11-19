/** A single animation loop follows a message heading, never its growing tail. */
export function followMessageTop(parent:HTMLElement,stack:HTMLElement,spacer:HTMLElement,getTarget:()=>HTMLElement|null){
 let frame=0,stopped=false,dirty=true,destination=parent.scrollTop,lastTime=0;const reduced=matchMedia('(prefers-reduced-motion: reduce)');
 const measure=()=>{dirty=false;const target=getTarget();if(!target)return;const padding=parseFloat(getComputedStyle(parent).paddingTop)||0,pager=stack.querySelector<HTMLElement>('.history-pager'),inset=padding+(pager?.getBoundingClientRect().height||0)+12;
  // Enough breathing room lets a short reply reach the same reading position.
  const desired=Math.max(0,parent.scrollTop+target.getBoundingClientRect().top-parent.getBoundingClientRect().top-inset),naturalMax=parent.scrollHeight-parent.clientHeight-spacer.offsetHeight;
  spacer.style.height=Math.max(0,desired-naturalMax)+'px';destination=Math.min(desired,Math.max(0,parent.scrollHeight-parent.clientHeight));
 };
 const tick=(now:number)=>{frame=0;if(stopped)return;if(dirty)measure();const elapsed=lastTime?Math.min(40,now-lastTime):16;lastTime=now;const delta=destination-parent.scrollTop;if(Math.abs(delta)>1.5){parent.scrollTop=reduced.matches?destination:parent.scrollTop+delta*(1-Math.exp(-elapsed/85));if(!reduced.matches)frame=requestAnimationFrame(tick);}else{parent.scrollTop=destination;lastTime=0;}};
 const refresh=()=>{dirty=true;if(!stopped&&!frame)frame=requestAnimationFrame(tick);};
 const cancel=(event:Event)=>{if(event instanceof KeyboardEvent&&!['ArrowUp','ArrowDown','PageUp','PageDown','Home','End',' '].includes(event.key))return;stopped=true;cancelAnimationFrame(frame);frame=0;};
 const observer=new ResizeObserver(refresh);observer.observe(stack);observer.observe(parent);for(const event of ['wheel','touchmove','pointerdown','keydown'])parent.addEventListener(event,cancel,{passive:true});refresh();
 return()=>{stopped=true;cancelAnimationFrame(frame);observer.disconnect();for(const event of ['wheel','touchmove','pointerdown','keydown'])parent.removeEventListener(event,cancel);};
}
