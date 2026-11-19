import test from 'node:test';import assert from 'node:assert/strict';
import {summarizeHistory}from '../shared/summarize.mjs';import {prepareContext}from '../shared/context.mjs';import {validateRecords}from '../shared/records.mjs';
const revision='0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa';
const history=n=>Array.from({length:n},(_,i)=>({id:'message-'+i,rev:revision,role:i%2?'assistant':'user',content:`消息 ${i} `+'资料'.repeat(500),created:i}));
test('摘要统一使用 80K 输出上限，遇到 length 重试原资料，绝不接受截断内容',async()=>{
 const calls=[];const retries=[];
 const text=await summarizeHistory({summary:'旧摘要',batch:'完整历史资料',model:'test',request:async body=>{calls.push(body);return {data:JSON.stringify({choices:[{message:{content:calls.length===1?'截断的摘要':'完整摘要'},finish_reason:calls.length===1?'length':'stop'}]})};},onRetry:info=>retries.push(info)});
 assert.equal(text,'完整摘要');assert.equal(calls.length,2);assert.ok(calls.every(call=>call.max_tokens===81920));assert.equal(calls[0].messages[1].content,calls[1].messages[1].content);assert.equal(retries.length,1);
});
test('摘要过长会重新生成紧凑摘要，空结果或不完整状态有重试上限',async()=>{
 let count=0;const text=await summarizeHistory({summary:'',batch:'资料',model:'test',request:async()=>({data:JSON.stringify({choices:[{message:{content:++count===1?'字'.repeat(1000001):'精炼摘要'},finish_reason:'stop'}]})})});assert.equal(text,'精炼摘要');assert.equal(count,2);
 count=0;await assert.rejects(summarizeHistory({summary:'',batch:'资料',model:'test',request:async()=>{count++;return {data:JSON.stringify({choices:[{message:{content:'半句'},finish_reason:'length'}]})};}}),/达到输出上限.*此前进度已保留/);assert.equal(count,3);
});
test('第 226 条附近失败时保存先前批次，重试直接从检查点续压',async()=>{
 const rows=history(260),original=JSON.stringify(rows);let checkpoint;
 await assert.rejects(prepareContext(rows,{budget:8000,onCheckpoint:async value=>{checkpoint=structuredClone(value);},summarize:async(_old,batch)=>{if(batch.includes('消息 226 '))throw Error('模拟摘要输出上限');return '完整历史摘要';}}),/模拟摘要输出上限/);
 assert.ok(checkpoint.covered>0&&checkpoint.covered<=226);let firstBatch;
 const plan=await prepareContext(rows,{budget:8000,cache:checkpoint,summarize:async(_old,batch)=>{firstBatch??=batch;return '完整历史摘要';}});
 assert.ok(firstBatch.includes(`消息 ${checkpoint.covered} `));assert.ok(!firstBatch.includes('消息 0 '));assert.ok(plan.compressed>226);assert.equal(JSON.stringify(rows),original);
});
test('单条超长消息在中途失败也能从字符偏移续压，并检查来源修订',async()=>{
 const rows=history(4);rows[0].content='长'.repeat(50000);let cache,calls=0;
 await assert.rejects(prepareContext(rows,{budget:8000,onCheckpoint:async value=>{cache=structuredClone(value);},summarize:async()=>{if(++calls===2)throw Error('模拟中断');return '分段摘要';}}),/模拟中断/);
 assert.equal(cache.covered,0);assert.equal(cache.partial.offset,16000);let first;
 await prepareContext(rows,{budget:8000,cache,summarize:async(_old,batch)=>{first??=batch;return '分段摘要';}});assert.ok(first.includes('分段 16001–32000'));
 rows[0].rev='0000000000000002:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa';first=undefined;await prepareContext(rows,{budget:8000,cache,summarize:async(_old,batch)=>{first??=batch;return '分段摘要';}});assert.ok(first.includes('分段 1–16000'));
 const sanitized=validateRecords([{id:'chat',type:'chat',title:'部分压缩',rev:revision,created:0,contextSummary:cache}]);assert.equal(sanitized[0].contextSummary.partial.offset,16000);
});
test('内容审核、认证失败和用户停止不会进行无意义重试',async()=>{
 let count=0;await assert.rejects(summarizeHistory({summary:'',batch:'资料',model:'test',request:async()=>{count++;return {data:JSON.stringify({choices:[{message:{content:''},finish_reason:'content_filter'}]})};}}),/内容审核/);assert.equal(count,1);
 count=0;await assert.rejects(summarizeHistory({summary:'',batch:'资料',model:'test',request:async()=>{count++;throw Error('API Key 无效');}}),/API Key/);assert.equal(count,1);
 const controller=new AbortController();controller.abort();await assert.rejects(summarizeHistory({summary:'',batch:'资料',model:'test',signal:controller.signal,request:async()=>{throw Error('must not call');}}),{name:'AbortError'});
});
