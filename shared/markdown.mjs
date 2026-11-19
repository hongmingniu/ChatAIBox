import {unified} from 'unified';import remarkParse from 'remark-parse';import remarkGfm from 'remark-gfm';import remarkMath from 'remark-math';import remarkRehype from 'remark-rehype';import rehypeRaw from 'rehype-raw';import rehypeSanitize,{defaultSchema} from 'rehype-sanitize';import rehypeKatex from 'rehype-katex';import rehypeHighlight from 'rehype-highlight';import rehypeStringify from 'rehype-stringify';import GithubSlugger from 'github-slugger';
const schema={...defaultSchema,attributes:{...defaultSchema.attributes,code:[...(defaultSchema.attributes?.code||[]),['className',/^language-/, 'math-inline','math-display']],span:[...(defaultSchema.attributes?.span||[]),'className'],div:[...(defaultSchema.attributes?.div||[]),'className']},protocols:{...defaultSchema.protocols,src:['https','http','data']}};
const textOf=n=>n.type==='text'?n.value:(n.children||[]).map(textOf).join('');
const txt=value=>({type:'text',value});const el=(tagName,properties,children)=>({type:'element',tagName,properties,children});
function enhance(){return tree=>{const slugger=new GithubSlugger();function walk(node){if(!node.children)return;node.children=node.children.map(child=>{
 if(child.type==='element'&&/^h[1-6]$/.test(child.tagName))child.properties={...child.properties,id:'md-'+slugger.slug(textOf(child))};
 if(child.type==='element'&&child.tagName==='img'){
  const src=String(child.properties?.src||'');if(!/^data:image\/(png|jpeg|gif|webp);base64,/i.test(src))return el('button',{type:'button',className:['remote-image'],dataImageUrl:/^https:\/\//.test(src)?src:'',dataAlt:String(child.properties?.alt||'图片')},[txt('加载图片：'+String(child.properties?.alt||'远程图片'))]);
 }
 if(child.type==='element'&&child.tagName==='pre'&&child.children?.[0]?.tagName==='code'){
  const code=child.children[0];const language=(code.properties?.className||[]).find(c=>c.startsWith('language-'))?.slice(9)||'text';
  return el('div',{className:['code-shell']},[el('div',{className:['code-header']},[el('span',{className:['code-language']},[txt(language)]),el('div',{className:['code-actions']},[el('button',{type:'button',dataCodeAction:'copy'},[txt('复制')]),el('button',{type:'button',dataCodeAction:'download',dataLanguage:language},[txt('下载')])])]),child]);
 }walk(child);return child;
 });}walk(tree);};}
const processor=unified().use(remarkParse).use(remarkGfm).use(remarkMath).use(remarkRehype,{allowDangerousHtml:true}).use(rehypeRaw).use(rehypeSanitize,schema).use(rehypeKatex,{throwOnError:false,strict:false,trust:false}).use(rehypeHighlight,{detect:false,ignoreMissing:true}).use(enhance).use(rehypeStringify);
export async function renderMarkdown(text){return String(await processor.process(String(text)));}
