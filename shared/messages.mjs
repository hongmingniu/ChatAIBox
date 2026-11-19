export function modelMessage(m,images=true){
 const text=m.content||'';
 if(m.role==='user'&&m.images?.length&&images)return {role:m.role,content:[...(text?[{type:'text',text}]:[]),...m.images.map(image=>({type:'image_url',image_url:{url:image.dataUrl}}))]};
 return {role:m.role,content:text+(m.images?.length?'\n[图片：'+m.images.map(i=>i.name).join('、')+'，原图保留在消息中]':'')};
}
export function historyText(m){return (m.content||'')+(m.images?.length?'\n[图片附件：'+m.images.map(i=>i.name).join('、')+'；摘要只引用现有文字说明，不猜测未描述的图像内容]':'');}
export function speechText(markdown){return markdown.replace(/```[\s\S]*?```/g,'\n（代码块）\n').replace(/!\[([^\]]*)\]\([^)]*\)/g,'$1').replace(/\[([^\]]+)\]\([^)]*\)/g,'$1').replace(/<[^>]+>/g,'').replace(/[#*_>`~]/g,'').trim();}
