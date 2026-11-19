/// <reference lib="webworker" />
import {renderMarkdown} from '../shared/markdown.mjs';
self.onmessage=async({data})=>{try{self.postMessage({id:data.id,html:await renderMarkdown(data.text)});}catch(e:any){self.postMessage({id:data.id,error:e.message});}};
