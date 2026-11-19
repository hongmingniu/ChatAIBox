import {SUMMARY_MAX_CHARACTERS} from './limits.mjs';
export const MAX_RECORDS = 30000;
export function validateRecords(records) {
  if (!Array.isArray(records) || records.length > MAX_RECORDS) throw new Error('聊天记录格式或数量不合法');
  return records.map(r => {
    if (!r || typeof r.id !== 'string' || r.id.length > 160 || !/^\d{16}:[a-f0-9-]{36}$/.test(r.rev) || !['chat','message'].includes(r.type)) throw new Error('无效的同步记录');
    const base = {id:r.id,rev:r.rev,type:r.type,deleted:r.deleted===true};
    if (r.type==='chat') return {...base,title:String(r.title??'新对话').slice(0,300),created:Number(r.created)||0,...modelFields(r),...(validSummary(r.contextSummary)?{contextSummary:{text:r.contextSummary.text,covered:r.contextSummary.covered,prefixHash:r.contextSummary.prefixHash,...(r.contextSummary.partial?{partial:{id:r.contextSummary.partial.id,rev:r.contextSummary.partial.rev,offset:r.contextSummary.partial.offset}}:{})}}:{})};
    if (typeof r.chatId!=='string' || !['user','assistant'].includes(r.role) || typeof r.content!=='string' || r.content.length>2000000) throw new Error('无效的消息');
    return {...base,chatId:r.chatId,role:r.role,content:r.content,reasoning:String(r.reasoning??'').slice(0,2000000),created:Number(r.created)||0,sources:cleanSources(r.sources),...modelFields(r),...(r.images?.length?{images:cleanImages(r.images)}:{}),...(r.searches?.length?{searches:cleanSearches(r.searches)}:{}),...(r.interrupted?{interrupted:true}:{})};
  });
}
function modelFields(r){return {...(typeof r.providerId==='string'?{providerId:r.providerId.slice(0,160)}:{}),...(typeof r.model==='string'?{model:r.model.slice(0,160)}:{})};}
function cleanImages(images){if(!Array.isArray(images)||images.length>4)throw Error('每条消息最多 4 张图片');return images.map(image=>{if(!/^data:image\/(png|jpeg|gif|webp);base64,[a-zA-Z0-9+/=]+$/.test(image.dataUrl)||image.dataUrl.length>1500000)throw Error('图片格式或大小无效');return {id:String(image.id).slice(0,160),name:String(image.name||'图片').slice(0,256),mime:String(image.mime||'image/jpeg').slice(0,80),dataUrl:image.dataUrl,width:Number(image.width)||0,height:Number(image.height)||0};});}
function cleanSearches(rows){return (Array.isArray(rows)?rows:[]).slice(0,100).map(row=>({id:String(row.id).slice(0,160),kind:String(row.kind||'search').slice(0,40),queries:(Array.isArray(row.queries)?row.queries:[]).slice(0,20).map(q=>String(q).slice(0,1024)),status:String(row.status||'completed').slice(0,40),...(/^https?:\/\//.test(row.url)?{url:String(row.url).slice(0,4000)}:{})}));}
function validSummary(s){const p=s?.partial;const validPartial=p&&typeof p.id==='string'&&p.id.length<=160&&/^\d{16}:[a-f0-9-]{36}$/.test(p.rev)&&Number.isInteger(p.offset)&&p.offset>0&&p.offset<=2000000;return s&&typeof s.text==='string'&&s.text.length<=SUMMARY_MAX_CHARACTERS&&Number.isInteger(s.covered)&&s.covered>=0&&(s.covered>0||validPartial)&&s.covered<=30000&&(!p||validPartial)&&typeof s.prefixHash==='string'&&/^[a-f0-9]{1,8}$/.test(s.prefixHash);}
function cleanSources(sources) {
  return (Array.isArray(sources)?sources:[]).slice(0,100).filter(s=>s&&typeof s.url==='string'&&/^https?:\/\//.test(s.url)).map(s=>({title:String(s.title??s.url).slice(0,1000),url:s.url.slice(0,4000)}));
}
export function mergeRecords(local, remote) {
  const records = new Map();
  for (const record of [...validateRecords(local),...validateRecords(remote)]) {
    const old=records.get(record.id);
    if (!old || record.rev>old.rev || (record.rev===old.rev && JSON.stringify(record)>JSON.stringify(old))) records.set(record.id,record);
  }
  if(records.size>MAX_RECORDS) throw new Error('记录超过同步容量，请先导出备份');
  return [...records.values()].sort((a,b)=>a.id.localeCompare(b.id));
}
