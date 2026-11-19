/** Incremental SSE parser. Native transports decode UTF-8 before forwarding text. */
export class ChatStream {
  constructor(onUpdate=()=>{}) {this.buffer='';this.content='';this.reasoning='';this.sources=[];this.searches=[];this.finishReason=null;this.done=false;this.usage=/** @type {any} */(null);this.onUpdate=onUpdate;}
  feed(text) {
    this.buffer+=text;
    if(this.buffer.length>4_000_000)throw Error('流式数据帧过大');
    let match;
    while((match=/\r?\n\r?\n/.exec(this.buffer))){const event=this.buffer.slice(0,match.index);this.buffer=this.buffer.slice(match.index+match[0].length);this.event(event);}
  }
  event(event) {
    const data=event.split(/\r?\n/).filter(line=>line.startsWith('data:')).map(line=>line.slice(5).replace(/^ /,'')).join('\n');
    if(!data)return;if(data==='[DONE]'){this.done=true;return;}if(this.done)return;
    const value=JSON.parse(data);if(value.error)throw Error(/context_length|context.*(window|limit)|maximum context/i.test(String(value.error.code)+' '+value.error.message)?'CONTEXT_LIMIT: 模型上下文已达到上限':value.error.message??'DeepSeek 流式请求失败');
    if(value.usage)this.usage=value.usage;
    const choice=value.choices?.[0];if(!choice)return;const delta=choice.delta??{};
    this.content+=delta.content??'';this.reasoning+=delta.reasoning_content??'';
    if(this.content.length+this.reasoning.length>2_000_000)throw Error('回复超出保存容量');
    if(delta.annotations)this.sources=delta.annotations.map(a=>a.url_citation??a).filter(a=>/^https?:\/\//.test(a.url)).map(a=>({title:a.title??a.url,url:a.url}));
    const toolEvent=choice.server_tool_event;if(toolEvent?.call)this.searchCall(toolEvent.call,toolEvent.type==='call_completed'?'completed':'searching',toolEvent.step);
    for(const step of delta.server_tool_calls||[])for(const call of step.calls||[])this.searchCall(call,call.status||'completed',step.index);
    if(choice.finish_reason)this.finishReason=choice.finish_reason;
    this.onUpdate({content:this.content,reasoning:this.reasoning,sources:this.sources,searches:this.searches,toolEvent:choice.server_tool_event});
  }
  searchCall(call,status,step=0){const action=call.action||call.input||{};const queries=Array.isArray(action.queries)?action.queries:[];const id=String(call.id||`${step}:${call.index||0}`),old=this.searches.find(row=>row.id===id);const row={id,kind:call.kind||action.type||'search',queries:queries.length?queries:old?.queries||[],status:call.status||status,...(action.url?{url:action.url}:old?.url?{url:old.url}:{})};this.searches=old?this.searches.map(value=>value.id===id?row:value):[...this.searches,row];}
  finish(){if(this.buffer.trim())this.event(this.buffer);this.buffer='';if(!this.done)throw Error('流式连接提前中断，已保留收到的内容');return this;}
}
