export function shareId(link) {
  let url; try {url=new URL(link.trim());} catch {throw new Error('请输入完整的 DeepSeek 分享链接');}
  const match=url.pathname.match(/^\/share\/([a-zA-Z0-9_-]{5,128})\/?$/);
  if(url.protocol!=='https:'||url.hostname!=='chat.deepseek.com'||url.port||!match) throw new Error('仅支持 https://chat.deepseek.com/share/… 分享链接');
  return match[1];
}
export function parseShare(payload) {
  if(payload.code && payload.code!==0) throw new Error('分享已失效或需要登录，请检查链接');
  const data=payload.data?.biz_data??payload.data??payload;
  if(payload.data?.biz_code && payload.data.biz_code!==0) throw new Error('分享已失效或无法读取');
  const rows=data.chat_messages??data.messages??data.share?.messages;
  if(!Array.isArray(rows)) throw new Error('无法识别分享内容；可改用 JSON 备份导入');
  const messages=rows.map(m=>{
    const role=String(m.role??'').toLowerCase();
    const fragments=Array.isArray(m.fragments)?m.fragments:[];
    const content=typeof m.content==='string'?m.content:fragments.filter(f=>['RESPONSE','REQUEST','TEXT'].includes(String(f.type).toUpperCase())).map(f=>f.content??f.text??'').join('\n');
    const reasoning=m.reasoning_content??fragments.filter(f=>String(f.type).toUpperCase()==='THINK').map(f=>f.content??'').join('\n');
    return {role:role==='user'?'user':role==='assistant'?'assistant':'',content,reasoning, sources:[]};
  }).filter(m=>m.role&&m.content);
  if(!messages.length) throw new Error('分享中没有可导入的文字消息');
  return {title:String(data.chat_session?.title??data.title??messages[0].content.slice(0,30)),messages};
}
