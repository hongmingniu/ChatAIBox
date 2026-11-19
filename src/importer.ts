import type {RecordRow} from './host';
export function parseImport(args:{kind:'share'|'backup';text:string;clock?:number;deviceId?:string}){return new Promise<{records:RecordRow[];chatId?:string;clock?:number}>((resolve,reject)=>{
 const worker=new Worker(new URL('./import.worker.ts',import.meta.url),{type:'module'});
 worker.onmessage=({data})=>{worker.terminate();if(data.error)reject(Error(data.error));else resolve(data);};worker.onerror=()=>{worker.terminate();reject(Error('导入解析失败'));};worker.postMessage(args);
});}
