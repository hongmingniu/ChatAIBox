import React,{forwardRef,useEffect,useImperativeHandle,useRef} from 'react';
import {Annotation,Compartment,EditorState,Prec,Transaction,type Range} from '@codemirror/state';
import {Decoration,EditorView,ViewPlugin,drawSelection,dropCursor,keymap,placeholder,type DecorationSet,type ViewUpdate} from '@codemirror/view';
import {HighlightStyle,syntaxHighlighting,syntaxTree} from '@codemirror/language';
import {defaultKeymap,history,historyKeymap} from '@codemirror/commands';
import {markdown} from '@codemirror/lang-markdown';import {GFM} from '@lezer/markdown';import {tags} from '@lezer/highlight';

export type MarkdownEditorHandle={insertText:(text:string)=>void;focus:()=>void};
type Props={value:string;onChange:(value:string)=>void;placeholder:string;onSubmit:()=>void;onImages:(files:File[])=>void};
const external=Annotation.define<boolean>();
const highlight=HighlightStyle.define([
 {tag:tags.heading,class:'md-heading-text'},{tag:tags.strong,class:'md-strong'},
 {tag:tags.emphasis,class:'md-emphasis'},{tag:tags.strikethrough,class:'md-strike'},
 {tag:tags.link,class:'md-link'},{tag:tags.url,class:'md-url'},
 {tag:tags.monospace,class:'md-inline-code'},{tag:tags.quote,class:'md-quote-text'}
]);
function decorations(view:EditorView){const ranges:Range<Decoration>[]=[],lines=new Map<number,Set<string>>();
 const lineClass=(from:number,to:number,cls:string)=>{let line=view.state.doc.lineAt(from);while(line.from<=to){const set=lines.get(line.from)||new Set<string>();set.add(cls);lines.set(line.from,set);if(line.number===view.state.doc.lines)break;line=view.state.doc.line(line.number+1);}};
 for(const {from,to} of view.visibleRanges)syntaxTree(view.state).iterate({from,to,enter(node){const name=node.name;
  if(/^(HeaderMark|EmphasisMark|StrikethroughMark|LinkMark|CodeMark|QuoteMark|ListMark|TaskMarker|TableDelimiter)$/.test(name))ranges.push(Decoration.mark({class:'md-format-mark'}).range(node.from,node.to));
  if(name==='URL'||name==='CodeInfo')ranges.push(Decoration.mark({class:'md-source-detail'}).range(node.from,node.to));
  if(name==='Escape'&&node.to>node.from)ranges.push(Decoration.mark({class:'md-format-mark'}).range(node.from,node.from+1));
  const heading=/^(ATXHeading|SetextHeading)([1-6])$/.exec(name);if(heading)lineClass(node.from,node.to,'md-heading-'+heading[2]);
  if(name==='FencedCode'||name==='CodeBlock')lineClass(Math.max(node.from,from),Math.min(node.to,to),'md-code-line');
  if(name==='Blockquote')lineClass(Math.max(node.from,from),Math.min(node.to,to),'md-quote-line');
  if(name==='Table')lineClass(Math.max(node.from,from),Math.min(node.to,to),'md-table-line');
  if(name==='HorizontalRule')ranges.push(Decoration.mark({class:'md-format-mark md-horizontal-rule'}).range(node.from,node.to));
 }});
 for(const [from,classes] of lines)ranges.push(Decoration.line({class:[...classes].join(' ')}).range(from));
 return Decoration.set(ranges,true);
}
const formatting=ViewPlugin.fromClass(class{decorations:DecorationSet;constructor(view:EditorView){this.decorations=decorations(view);}update(update:ViewUpdate){if(update.docChanged||update.viewportChanged||syntaxTree(update.startState)!==syntaxTree(update.state))this.decorations=decorations(update.view);}},{decorations:plugin=>plugin.decorations});

/** One editable Markdown surface. Formatting marks stay in the source and caret map. */
export const MarkdownEditor=forwardRef<MarkdownEditorHandle,Props>(function MarkdownEditor(props,ref){
 const mount=useRef<HTMLDivElement>(null),viewRef=useRef<EditorView|null>(null),latest=useRef(props),placeholderSlot=useRef(new Compartment());latest.current=props;
 useImperativeHandle(ref,()=>({insertText(text){const view=viewRef.current;if(!view)return;view.dispatch(view.state.replaceSelection(text),{scrollIntoView:true,userEvent:'input.paste'});view.focus();},focus(){viewRef.current?.focus();}}),[]);
 useEffect(()=>{if(!mount.current)return;const view=new EditorView({parent:mount.current,state:EditorState.create({doc:latest.current.value,extensions:[
  history(),drawSelection(),dropCursor(),EditorView.lineWrapping,
  markdown({extensions:GFM,addKeymap:false,completeHTMLTags:false,pasteURLAsLink:false}),
  keymap.of([...defaultKeymap,...historyKeymap]),syntaxHighlighting(highlight),formatting,
  placeholderSlot.current.of(placeholder(latest.current.placeholder)),
  EditorView.contentAttributes.of({'aria-label':'聊天消息','aria-multiline':'true',spellcheck:'false',autocapitalize:'sentences',enterkeyhint:'enter'}),
  EditorView.updateListener.of(update=>{if(update.docChanged&&!update.transactions.some(tr=>tr.annotation(external)))latest.current.onChange(update.state.doc.toString());}),
  Prec.highest(EditorView.domEventHandlers({keydown(event,view){if(event.key==='Enter'&&!event.shiftKey&&!event.ctrlKey&&!event.altKey&&!event.metaKey&&!event.isComposing&&!view.composing&&event.keyCode!==229&&window.innerWidth>700){event.preventDefault();latest.current.onSubmit();return true;}return false;},paste(event){const files=Array.from(event.clipboardData?.files||[]).filter(file=>file.type.startsWith('image/'));if(!files.length)return false;event.preventDefault();latest.current.onImages(files);return true;}}))
 ]})});viewRef.current=view;return()=>{viewRef.current=null;view.destroy();};},[]);
 useEffect(()=>{const view=viewRef.current;if(!view||view.composing)return;const before=view.state.doc.toString(),after=props.value;if(before===after)return;let from=0;while(from<before.length&&from<after.length&&before[from]===after[from])from++;let oldEnd=before.length,newEnd=after.length;while(oldEnd>from&&newEnd>from&&before[oldEnd-1]===after[newEnd-1]){oldEnd--;newEnd--;}view.dispatch({changes:{from,to:oldEnd,insert:after.slice(from,newEnd)},annotations:[external.of(true),Transaction.addToHistory.of(false)]});},[props.value]);
 useEffect(()=>{viewRef.current?.dispatch({effects:placeholderSlot.current.reconfigure(placeholder(props.placeholder))});},[props.placeholder]);
 return <div className="markdown-editor" ref={mount}/>;
});
