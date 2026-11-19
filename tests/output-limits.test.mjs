import test from 'node:test';import assert from 'node:assert/strict';
import {CHAT_OUTPUT_TOKENS,SUMMARY_OUTPUT_TOKENS,MODEL_CONTEXT_TOKENS,CONTEXT_INPUT_BUDGET,CONTEXT_OVERHEAD_TOKENS} from '../shared/limits.mjs';
import {prepareContext,prefixHash} from '../shared/context.mjs';
import {summarizeHistory} from '../shared/summarize.mjs';
import {validateRecords} from '../shared/records.mjs';
import {ChatStream} from '../shared/sse.mjs';
test('普通单轮输出使用 24K 上限，长流式回复不会被旧显示长度截断',()=>{
 assert.equal(CHAT_OUTPUT_TOKENS,24576);
 const content='word '.repeat(CHAT_OUTPUT_TOKENS);const stream=new ChatStream();
 for(let i=0;i<content.length;i+=700)stream.feed('data: '+JSON.stringify({choices:[{delta:{content:content.slice(i,i+700)}}]})+'\n\n');
 stream.feed('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n');stream.finish();assert.equal(stream.content,content);
});
const revision='0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa';
const rows=n=>Array.from({length:n},(_,i)=>({id:'window-'+i,rev:revision,role:i%2?'assistant':'user',content:'信息'.repeat(5000),created:i}));
test('1M 总窗口预留 24K 输出，16K 旧阈值不再提前触发压缩',async()=>{
 assert.equal(MODEL_CONTEXT_TOKENS,1048576);assert.equal(SUMMARY_OUTPUT_TOKENS,81920);assert.equal(CONTEXT_INPUT_BUDGET+CHAT_OUTPUT_TOKENS+CONTEXT_OVERHEAD_TOKENS,MODEL_CONTEXT_TOKENS);
 let calls=0;const result=await prepareContext(rows(50),{summarize:async()=>{calls++;return '摘要';}});assert.equal(calls,0);assert.equal(result.compressed,0);assert.ok(result.estimatedTokens>16000);assert.equal(result.messages.length,50);
});
test('达到接近 1M 的输入预算才压缩，大于旧上限的摘要可以保存、恢复并用于请求',async()=>{
 const history=rows(180),text='摘要'.repeat(60000);let calls=0;
 const plan=await prepareContext(history,{summarize:async()=>{calls++;return text;}});assert.ok(calls>0);assert.ok(plan.estimatedTokens<CONTEXT_INPUT_BUDGET);assert.equal(plan.cache.text,text);
 const stored=validateRecords([{id:'chat',type:'chat',title:'80K 摘要',rev:revision,created:0,contextSummary:plan.cache}])[0];assert.equal(stored.contextSummary.text,text);
 calls=0;await prepareContext(history,{cache:stored.contextSummary,summarize:async()=>{calls++;return text;}});assert.equal(calls,0);
 const generated=await summarizeHistory({summary:'',batch:'历史',model:'test',request:async body=>{assert.equal(body.max_tokens,81920);return {data:JSON.stringify({choices:[{message:{content:text},finish_reason:'stop'}]})};}});assert.equal(generated,text);
});
test('接口实际用量校准可避免字符估算过早压缩，模型明确拒绝时允许强制压缩',async()=>{
 let calls=0;await prepareContext(rows(180),{estimatedInputTokens:CONTEXT_INPUT_BUDGET-100,summarize:async()=>{calls++;return '摘要';}});assert.equal(calls,0);
 const small=rows(10);const forced=await prepareContext(small,{forceCompress:true,summarize:async()=>{calls++;return '摘要';}});assert.ok(calls>0);assert.ok(forced.compressed>0);
});
