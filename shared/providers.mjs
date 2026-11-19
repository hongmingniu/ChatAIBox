export const BUILTIN_PROVIDER='deepseek';
export function providerFor(settings,id=settings.activeProviderId||BUILTIN_PROVIDER){
 if(id===BUILTIN_PROVIDER)return {id,name:'DeepSeek',baseUrl:'https://api.deepseek.com',apiKey:settings.apiKey||'',models:['deepseek-flash','deepseek-v4-pro'],vision:true,search:true,thinking:'deepseek',contextWindow:1048576,maxOutputTokens:393216,asrModel:''};
 const provider=(settings.providers||[]).find(p=>p.id===id);if(!provider)throw Error('提供方尚未配置，请在设置中添加或选择其他提供方');return provider;
}
export function validateProviders(providers=[]){
 if(!Array.isArray(providers)||providers.length>30)throw Error('提供方数量或格式无效');const ids=new Set();
 return providers.map(p=>{const url=new URL(p.baseUrl);if(!['https:','http:'].includes(url.protocol)||url.username||url.password||url.search||url.hash||(url.protocol==='http:'&&!['localhost','127.0.0.1','[::1]'].includes(url.hostname)))throw Error('提供方地址必须为 HTTPS；本机回环可使用 HTTP');if(!p.id||p.id===BUILTIN_PROVIDER||ids.has(p.id)||p.id.length>160)throw Error('提供方 ID 无效');ids.add(p.id);if(!Array.isArray(p.models)||!p.models.length||p.models.length>100||p.models.some(m=>typeof m!=='string'||!m.trim()||m.length>160))throw Error('请至少填写一个有效模型 ID');return {...p,name:String(p.name||'自定义提供方').slice(0,80),baseUrl:url.href.replace(/\/$/,''),apiKey:String(p.apiKey||''),vision:p.vision===true,search:p.search===true,thinking:['deepseek','effort','none'].includes(p.thinking)?p.thinking:'none',contextWindow:Math.max(4096,Math.min(2097152,Number(p.contextWindow)||131072)),maxOutputTokens:Math.max(256,Math.min(393216,Number(p.maxOutputTokens)||8192))};});
}
export function capabilities(provider,model){return {vision:provider.vision&&(provider.id!=='deepseek'||model==='deepseek-flash'),search:provider.search&&(provider.id!=='deepseek'||model==='deepseek-flash')};}
export function adaptBody(body,provider){const next={...body};if(provider.id!=='deepseek'){
 delete next.stream_options;
 if(provider.thinking==='effort'){next.reasoning_effort=body.thinking?.type==='enabled'?'high':'none';delete next.thinking;}
 else if(provider.thinking!=='deepseek')delete next.thinking;
 if(!provider.search){delete next.tools;delete next.web_search_options;}
 }return next;}
