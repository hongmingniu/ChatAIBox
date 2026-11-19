import test from 'node:test';import assert from 'node:assert/strict';import {createRequire}from 'node:module';
import {ChatStream}from '../shared/sse.mjs';import {prepareContext,prefixHash,estimateTokens}from '../shared/context.mjs';
import {validateRecords}from '../shared/records.mjs';
const require=createRequire(import.meta.url);const {createClient}=require('../desktop/deepseek.cjs');
const frame=value=>'data: '+JSON.stringify(value)+'\r\n\r\n';
test('SSE 任意分块、中文、思考、来源和终止标记',()=>{
 const wire=': keepalive\r\n\r\n'+frame({choices:[{delta:{reasoning_content:'先想一想'}}]})+frame({choices:[{delta:{content:'你好🙂'}}]})+frame({choices:[{delta:{annotations:[{url_citation:{url:'https://example.com',title:'来源'}}]},finish_reason:'stop'}],usage:{prompt_tokens:900000,completion_tokens:10}})+'data: [DONE]\r\n\r\n';
 const stream=new ChatStream();for(let i=0;i<wire.length;i+=3)stream.feed(wire.slice(i,i+3));stream.finish();assert.equal(stream.content,'你好🙂');assert.equal(stream.reasoning,'先想一想');assert.equal(stream.sources.length,1);assert.equal(stream.finishReason,'stop');assert.equal(stream.usage.prompt_tokens,900000);
});
test('SSE 网络提前中断或服务端错误时保留已收到的文本',()=>{const stream=new ChatStream();stream.feed(frame({choices:[{delta:{content:'半句话'}}]}));assert.throws(()=>stream.finish(),/提前中断/);assert.equal(stream.content,'半句话');assert.throws(()=>stream.feed(frame({error:{message:'服务端错误'}})),/服务端错误/);});
test('Windows 流式传输在 UTF-8 字符中间分块也不损坏文本',async()=>{
 const bytes=new TextEncoder().encode(frame({choices:[{delta:{content:'中文🙂'}}]})+'data: [DONE]\n\n');let received='';
 const client=createClient(()=>({apiKey:'test',proxy:''}),async()=>new Response(new ReadableStream({start(controller){for(let i=0;i<bytes.length;i+=2)controller.enqueue(bytes.slice(i,i+2));controller.close();}}),{headers:{'content-type':'text/event-stream'}}));
 await client.request({kind:'chat',body:{},stream:true},event=>received+=event.chunk);const parsed=new ChatStream();parsed.feed(received);parsed.finish();assert.equal(parsed.content,'中文🙂');
});
test('取消 Windows 请求会中止网络读取并返回 cancelled',async()=>{
 let networkSignal;const client=createClient(()=>({apiKey:'test'}),async(_url,options)=>{networkSignal=options.signal;return new Response(new ReadableStream({start(controller){options.signal.addEventListener('abort',()=>controller.error(Error('aborted')));}}),{headers:{'content-type':'text/event-stream'}});});
 const promise=client.request({kind:'chat',body:{},stream:true,requestId:'cancel-fixture'});await new Promise(r=>setTimeout(r,10));client.cancel('cancel-fixture');assert.equal((await promise).cancelled,true);assert.equal(networkSignal.aborted,true);
});
const rows=(n,length=500)=>Array.from({length:n},(_,i)=>({id:`m-${i}`,rev:`0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa`,role:i%2?'assistant':'user',content:`消息 ${i} `+'资料'.repeat(length),created:i}));
test('长历史自动分批压缩、保留最新原文、缓存摘要且不改原始消息',async()=>{
 const history=rows(100),original=JSON.stringify(history);let calls=0;const summarize=async(_summary,batch)=>{assert.ok(estimateTokens(batch)<25000);calls++;return '用户目标与重要事实。保留未完成事项。';};
 const plan=await prepareContext(history,{budget:8000,summarize});assert.ok(calls>1);assert.ok(plan.estimatedTokens<=8000);assert.equal(plan.messages.at(-1).content,history.at(-1).content);assert.equal(JSON.stringify(history),original);assert.equal(plan.cache.prefixHash,prefixHash(history,plan.cache.covered));
 calls=0;const cached=await prepareContext(history,{budget:8000,summarize,cache:plan.cache});assert.equal(calls,0);assert.equal(cached.compressed,plan.compressed);
 history[0]={...history[0],rev:'0000000000000002:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa'};await prepareContext(history,{budget:8000,summarize,cache:plan.cache});assert.ok(calls>0);
});
test('单条巨大历史分块摘要时不跳过文字，超长最新消息明确拒绝',async()=>{
 const history=[...rows(2,0),...rows(2,0)];history[0].content='界'.repeat(30000);let length=0;
 const plan=await prepareContext(history,{budget:8000,summarize:async(_old,batch)=>{length+=batch.split('\n').slice(1).join('\n').length;return '摘要';}});assert.ok(length>=30000);assert.equal(plan.messages.at(-1).content,history.at(-1).content);
 await assert.rejects(prepareContext(rows(1,8000),{budget:8000,summarize:async()=>''}),/最新一条消息过长/);
});
test('禁用压缩或中途停止不会偷偷截断原始上下文',async()=>{
 await assert.rejects(prepareContext(rows(20),{budget:8000,enabled:false}),/开启自动压缩/);
 const controller=new AbortController();controller.abort();await assert.rejects(prepareContext(rows(20),{budget:8000,signal:controller.signal,summarize:async()=>''}),{name:'AbortError'});
});
test('消息中断标记和摘要元数据可以同步，敏感配置仍不进入记录',()=>{
 const base={id:'chat',rev:'0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa',type:'chat',created:1,title:'长对话',contextSummary:{text:'摘要',covered:10,prefixHash:'abc123'},apiKey:'private'};const result=validateRecords([base]);assert.equal(result[0].contextSummary.text,'摘要');assert.equal(result[0].apiKey,undefined);
});
