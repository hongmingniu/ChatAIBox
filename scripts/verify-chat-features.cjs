const {app,BrowserWindow}=require('electron');const fs=require('node:fs');const path=require('node:path');
const folder=path.resolve('.local/feature-test-'+Date.now());fs.mkdirSync(folder,{recursive:true});app.setPath('userData',folder);
const api=require('../desktop/deepseek.cjs'),createClient=api.createClient;let summaryCalls=0,streamCalls=0,contextRejected=false;
const fixture={code:0,data:{biz_code:0,biz_data:{title:'长对话性能验证',chat_messages:Array.from({length:1000},(_,i)=>({role:i%2?'ASSISTANT':'USER',content:`消息 ${i}。`+'长对话测试资料。'.repeat(40),reasoning_content:i%2?'不应在折叠时渲染的思考内容。'.repeat(60):''}))}}};
api.createClient=get=>createClient(get,async(url,options)=>{
 if(url.includes('/share/content'))return Response.json(fixture);
 const body=JSON.parse(options.body);
 if(!body.stream){if(body.max_tokens!==81920)throw Error('Summary output cap is not 80K');summaryCalls++;return Response.json({choices:[{message:{content:'用户正在测试长对话性能。已导入历史消息，需保留原文，按需压缩发送的历史资料。'},finish_reason:summaryCalls===1?'length':'stop'}]});}
 if(!contextRejected){if(summaryCalls!==0)throw Error('Compression started before the model context limit');contextRejected=true;return Response.json({error:{code:'context_length_exceeded',message:'maximum context window exceeded'}},{status:400});}
 if(body.max_tokens!==24576)throw Error('Chat output cap is not 24K');streamCalls++;let timer,abort;const encoder=new TextEncoder();
 return new Response(new ReadableStream({start(controller){let index=0;abort=()=>{clearInterval(timer);controller.error(Error('aborted'));};options.signal.addEventListener('abort',abort,{once:true});timer=setInterval(()=>{index++;controller.enqueue(encoder.encode('data: '+JSON.stringify({choices:[{delta:{content:`流式片段${index}。`}}]})+'\n\n'));if(index>=30){clearInterval(timer);options.signal.removeEventListener('abort',abort);controller.enqueue(encoder.encode('data: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n'));controller.close();}},60);},cancel(){clearInterval(timer);options.signal.removeEventListener('abort',abort);}}),{headers:{'content-type':'text/event-stream'}});
});
require('../desktop/main.cjs');
app.whenReady().then(async()=>{
 try{
  let win;for(let i=0;i<100;i++){win=BrowserWindow.getAllWindows()[0];if(win&&!win.webContents.isLoading()&&win.webContents.getURL())break;await new Promise(r=>setTimeout(r,100));}
  win.webContents.on('console-message',(_e,_l,message)=>{if(/worker|error|failed/i.test(message))console.error(message);});
  const js=code=>win.webContents.executeJavaScript(code);
  const waitFor=async(code,timeout=15000)=>{const start=Date.now();while(Date.now()-start<timeout){if(await js(code))return;await new Promise(r=>setTimeout(r,60));}throw Error('Timed out: '+code);};
  const fill=async(selector,value)=>js(`(()=>{const field=document.querySelector(${JSON.stringify(selector)});if(field.isContentEditable){field.focus();const range=document.createRange();range.selectNodeContents(field);getSelection().removeAllRanges();getSelection().addRange(range);document.execCommand('insertText',false,${JSON.stringify(value)});}else{Object.getOwnPropertyDescriptor(Object.getPrototypeOf(field),'value').set.call(field,${JSON.stringify(value)});field.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
  await waitFor('!!document.querySelector(".welcome")');
  await js("window.desktopHost.call('settings',{apiKey:'test-key-no-network',contextBudget:16000})");await win.webContents.reload();await waitFor('!!document.querySelector(".welcome")');
  await js("[...document.querySelectorAll('button')].find(b=>b.textContent.includes('导入分享')).click()");await fill('.modal .field input','https://chat.deepseek.com/share/test123');const importStart=Date.now();await js("document.querySelector('.modal .primary').click()");
  await waitFor('document.querySelectorAll("article.message").length===12');
  const importElapsedMs=Date.now()-importStart;const initial=await js("({articles:document.querySelectorAll('article.message').length,reasoningNodes:document.querySelectorAll('.reasoning p').length,text:document.querySelector('.history-pager').textContent})");if(!initial.text.includes('1000'))throw Error('Import did not retain 1000 messages');if(initial.reasoningNodes!==0)throw Error('Collapsed reasoning was eagerly rendered');
  await js("window.__longTasks=[];new PerformanceObserver(list=>window.__longTasks.push(...list.getEntries().map(e=>e.duration))).observe({type:'longtask',buffered:false})");
  for(let i=0;i<30;i++)await fill('.composer .cm-content','性能测试输入'+i);
  const performanceResult=await js("({articles:document.querySelectorAll('article.message').length,maxLongTask:Math.max(0,...window.__longTasks)})");if(performanceResult.maxLongTask>600)throw Error('Typing caused a long UI stall: '+performanceResult.maxLongTask);
  await js("document.querySelector('.send').click()");await waitFor('document.querySelector(".streaming-message")?.innerText.includes("流式片段")',20000);
  const partial=await js("document.querySelector('.streaming-message').textContent");if(partial.includes('流式片段30'))throw Error('Reply only appeared after the stream finished');
  await waitFor('!document.querySelector(".streaming-message")&&document.querySelector(".send").getAttribute("aria-label")==="发送消息"');
  const callsBefore=summaryCalls;
  const checkpointed=await js("window.desktopHost.call('read').then(r=>r.state.records.some(m=>m.type==='chat'&&m.contextSummary?.covered>200))");if(!checkpointed)throw Error('Compression checkpoints were not stored by the native bridge');
  await fill('.composer .cm-content','第二次请求，测试停止');await js("document.querySelector('.send').click()");await waitFor('document.querySelector(".streaming-message")?.innerText.includes("流式片段")');await js("document.querySelector('.send').click()");
  await waitFor('document.querySelector(".send").getAttribute("aria-label")==="发送消息"');
  const stopped=await js("window.desktopHost.call('read').then(r=>r.state.records.filter(m=>m.type==='message'&&m.role==='assistant'&&m.interrupted).length)");if(!stopped)throw Error('Stopped response was not saved as partial');
  if(summaryCalls>callsBefore+1)throw Error('Summary cache did not prevent repeated full compression');
  fs.writeFileSync(path.join(folder,'long-chat.png'),(await win.webContents.capturePage()).toPNG());
  console.log(JSON.stringify({pass:true,importedMessages:1000,importElapsedMs,mountedMessages:initial.articles,maxTypingLongTaskMs:performanceResult.maxLongTask,summaryCalls,streamCalls,partialSaved:true}));app.exit(0);
 }catch(e){console.error(e);const w=BrowserWindow.getAllWindows()[0];if(w){console.error(await w.webContents.executeJavaScript('document.body.innerText'));fs.writeFileSync(path.join(folder,'failure.png'),(await w.webContents.capturePage()).toPNG());}app.exit(1);}
});
