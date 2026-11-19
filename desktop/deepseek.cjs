const {fetch,ProxyAgent}=require('undici');
function createClient(getSettings,fetcher=fetch){
 const active=new Map();
 async function request({kind,body,shareId,requestId,stream=false,providerId},onChunk=()=>{}){
  const settings=getSettings(),id=requestId||require('node:crypto').randomUUID();
  if(active.has(id))throw Error('请求 ID 重复');
  const controller=new AbortController();active.set(id,controller);
  const proxy=settings.proxy||process.env.HTTPS_PROXY||process.env.HTTP_PROXY;
  const dispatcher=proxy?new ProxyAgent(proxy):undefined;
  const timeout=setTimeout(()=>controller.abort(Error('请求超时')),30*60*1000);
  try{
   if(kind!=='chat'&&!(kind==='share'&&/^[a-zA-Z0-9_-]{5,128}$/.test(shareId)))throw Error('无效请求');
   const {providerFor,adaptBody}=await import('../shared/providers.mjs');const provider=kind==='chat'?providerFor(settings,providerId):null;
   const url=kind==='chat'?provider.baseUrl+'/chat/completions':`https://chat.deepseek.com/api/v0/share/content?share_id=${encodeURIComponent(shareId)}`;
   const response=await fetcher(url,{method:kind==='chat'?'POST':'GET',dispatcher,signal:controller.signal,redirect:'error',headers:kind==='chat'?{'Content-Type':'application/json',Authorization:`Bearer ${provider.apiKey}`}:{Accept:'application/json'},body:kind==='chat'?JSON.stringify({...adaptBody(body,provider),stream}):undefined});
   if(!response.ok){if(response.status===400||response.status===413){const error=await response.json().catch(()=>null);if(/context_length|context.*(length|window|limit)|maximum context/i.test(String(error?.error?.code)+' '+error?.error?.message))throw Error('CONTEXT_LIMIT: 模型上下文已达到上限');}throw Error(({401:'API Key 无效',402:'DeepSeek 余额不足',429:'请求过于频繁，请稍后再试'}[response.status])||`请求失败（HTTP ${response.status}）`);}
   if(!stream){const data=await response.text();if(data.length>16000000)throw Error('返回内容过大');return {data};}
   if(!response.headers.get('content-type')?.includes('text/event-stream'))throw Error('服务器没有返回 SSE 流式数据');
   const decoder=new TextDecoder();let pending='',size=0,last=Date.now();
   for await(const bytes of response.body){size+=bytes.length;if(size>16000000)throw Error('返回内容过大');pending+=decoder.decode(bytes,{stream:true});if(Date.now()-last>=40||pending.length>=8192){onChunk({requestId:id,chunk:pending});pending='';last=Date.now();}}
   pending+=decoder.decode();if(pending)onChunk({requestId:id,chunk:pending});return {streamed:true};
  }catch(e){if(controller.signal.aborted){if(controller.signal.reason?.message==='请求超时')throw Error('请求超时，请稍后重试');return {cancelled:true};}if(/^(请求|API Key|DeepSeek|服务器|返回|CONTEXT_LIMIT:)/.test(e.message))throw e;throw Error(`网络请求失败${proxy?'，请检查代理连接':''}`);}
  finally{clearTimeout(timeout);active.delete(id);if(controller.signal.aborted)await dispatcher?.destroy();else await dispatcher?.close();}
 }
 return {request,cancel:id=>active.get(id)?.abort()};
}
module.exports={createClient};
