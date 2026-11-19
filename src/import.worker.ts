/// <reference lib="webworker" />
import {parseShare} from '../shared/import.mjs';
import {validateRecords} from '../shared/records.mjs';
self.onmessage=({data})=>{
 try{
  if(data.kind==='backup'){const payload=JSON.parse(data.text);if(payload.schema!==1)throw Error('备份版本不支持');self.postMessage({records:validateRecords(payload.records)});return;}
  const imported=parseShare(JSON.parse(data.text));let clock=data.clock;const created=Date.now()-imported.messages.length,chatId=crypto.randomUUID();const rev=()=>`${String(++clock).padStart(16,'0')}:${data.deviceId}`;
  const records=[{id:chatId,type:'chat',title:imported.title,rev:rev(),created},...imported.messages.map((m:any,i:number)=>({...m,id:crypto.randomUUID(),type:'message',chatId,rev:rev(),created:created+i}))];
  self.postMessage({records,chatId,clock});
 }catch(e:any){self.postMessage({error:e.message});}
};
