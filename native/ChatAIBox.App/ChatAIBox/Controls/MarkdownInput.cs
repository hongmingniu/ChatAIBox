#if !ANDROID
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Text.RegularExpressions;
using Windows.UI;
using Microsoft.UI.Text;
namespace ChatAIBox;
public sealed class MarkdownInput : RichEditBox
{
    private bool internalChange,formatting;private long sequence;private string last="";private readonly List<string> undo=[],redo=[];
    private readonly MarkdownPipeline pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    public bool IsComposing{get;private set;}public long CompositionEndedAt{get;private set;}
    public Func<Task>? PasteRequested;
    private async Task PasteFromClipboardAsync(){if(PasteRequested is {} paste)await paste();else InsertText(await NativeActions.PasteAsync());}
    public string Text{get{Document.GetText(TextGetOptions.None,out var text);return text.EndsWith('\r')?text[..^1]:text;}set{var before=Text;if(before==value)return;internalChange=true;Document.SetText(TextSetOptions.None,value);internalChange=false;last=Text;undo.Clear();redo.Clear();ScheduleFormatting();}}
    public MarkdownInput()
    {
        var transparent=new SolidColorBrush(Color.FromArgb(0,0,0,0));foreach(var key in new[]{"TextControlBackground","TextControlBackgroundPointerOver","TextControlBackgroundFocused","TextControlBorderBrush","TextControlBorderBrushPointerOver","TextControlBorderBrushFocused"})Resources[key]=transparent;
        TextChanged+=(_,_)=>{if(formatting)return;var text=Text;if(text==last)return;if(!internalChange){undo.Add(last);while(undo.Count>100||undo.Sum(s=>s.Length)>16_000_000)undo.RemoveAt(0);redo.Clear();}last=text;ScheduleFormatting();};
        TextCompositionStarted+=(_,_)=>IsComposing=true;TextCompositionEnded+=(_,_)=>{IsComposing=false;CompositionEndedAt=Environment.TickCount64;ScheduleFormatting();};
        KeyDown+=OnKeyDown;
        Paste+=async(_,args)=>{args.Handled=true;await PasteFromClipboardAsync();};
        var menu=new MenuFlyout();foreach(var (label,action) in new (string,Action)[]{("撤销",UndoText),("重做",RedoText),("剪切",()=>Document.Selection.Cut()),("复制",()=>Document.Selection.Copy()),("粘贴",async()=>await PasteFromClipboardAsync()),("全选",()=>Document.Selection.SetRange(0,Text.Length))}){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}ContextFlyout=menu;
        ActualThemeChanged+=(_,_)=>ScheduleFormatting();
    }
    private void OnKeyDown(object sender,KeyRoutedEventArgs e)
    {
        var ctrl=(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
        if(!ctrl)return;if(e.Key==Windows.System.VirtualKey.Z){e.Handled=true;if((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0)RedoText();else UndoText();}else if(e.Key==Windows.System.VirtualKey.Y){e.Handled=true;RedoText();}
    }
    public void InsertText(string text){Document.Selection.SetText(TextSetOptions.None,text);Focus(FocusState.Programmatic);}
    public void UndoText(){if(undo.Count==0)return;redo.Add(Text);var text=undo[^1];undo.RemoveAt(undo.Count-1);Restore(text);}
    public void RedoText(){if(redo.Count==0)return;undo.Add(Text);var text=redo[^1];redo.RemoveAt(redo.Count-1);Restore(text);}
    private void Restore(string text){internalChange=true;Document.SetText(TextSetOptions.None,text);Document.Selection.SetRange(text.Length,text.Length);internalChange=false;last=Text;ScheduleFormatting();}
    private async void ScheduleFormatting()
    {
        var own=++sequence;await Task.Delay(120);if(own!=sequence||IsComposing)return;var text=Text;
        try{var document=await Task.Run(()=>Markdown.Parse(text,pipeline));if(own!=sequence||Text!=text||IsComposing)return;Apply(text,document);}catch(Exception e)when(e is ArgumentException or InvalidOperationException){}
    }
    private void Apply(string text,MarkdownDocument markdown)
    {
        formatting=true;
        try{
            var dark=ActualTheme==ElementTheme.Dark;var ink=dark?Color.FromArgb(255,238,232,224):Color.FromArgb(255,37,42,48);var marker=dark?Color.FromArgb(255,88,83,78):Color.FromArgb(255,182,184,185);var accent=dark?Color.FromArgb(255,241,177,133):Color.FromArgb(255,155,71,31);
            var all=Document.GetRange(0,text.Length);all.CharacterFormat=Document.GetDefaultCharacterFormat();all.CharacterFormat.ForegroundColor=ink;all.CharacterFormat.Size=(float)(FontSize*.75);all.CharacterFormat.Bold=FormatEffect.Off;all.CharacterFormat.Italic=FormatEffect.Off;all.CharacterFormat.Strikethrough=FormatEffect.Off;
            ITextRange Range(int start,int count)=>Document.GetRange(Math.Clamp(start,0,text.Length),Math.Clamp(start+count,0,text.Length));
            void Mark(int start,int count){if(count>0)Range(start,count).CharacterFormat.ForegroundColor=marker;}
            foreach(var h in markdown.Descendants<HeadingBlock>()){Range(h.Span.Start,h.Span.Length).CharacterFormat.Size=(float)(FontSize*.75*(h.Level==1?1.55:h.Level==2?1.3:1.12));Range(h.Span.Start,h.Span.Length).CharacterFormat.Bold=FormatEffect.On;var m=Regex.Match(text[h.Span.Start..],@"^#{1,6}\s");if(m.Success)Mark(h.Span.Start,m.Length);}
            foreach(var em in markdown.Descendants<EmphasisInline>()){var range=Range(em.Span.Start,em.Span.Length);if(em.DelimiterChar=='~')range.CharacterFormat.Strikethrough=FormatEffect.On;else if(em.DelimiterCount>=2)range.CharacterFormat.Bold=FormatEffect.On;else range.CharacterFormat.Italic=FormatEffect.On;Mark(em.Span.Start,em.DelimiterCount);Mark(em.Span.End-em.DelimiterCount+1,em.DelimiterCount);}
            foreach(var code in markdown.Descendants<CodeInline>()){Range(code.Span.Start,code.Span.Length).CharacterFormat.Name=OperatingSystem.IsAndroid()?"monospace":"Consolas";Mark(code.Span.Start,code.DelimiterCount);Mark(code.Span.End-code.DelimiterCount+1,code.DelimiterCount);}
            foreach(var code in markdown.Descendants<FencedCodeBlock>()){Range(code.Span.Start,code.Span.Length).CharacterFormat.Name=OperatingSystem.IsAndroid()?"monospace":"Consolas";}
            foreach(Match m in Regex.Matches(text,@"(?m)^\s*(?:#{1,6}\s|>+\s?|[-+*]\s+|\d+[.)]\s+|`{3,}[^\r\n]*|~{3,}[^\r\n]*)"))Mark(m.Index,m.Length);
            foreach(Match m in Regex.Matches(text,@"\[([^\]\r\n]+)\]\(([^)\r\n]+)\)")){Range(m.Index,m.Length).CharacterFormat.ForegroundColor=accent;Mark(m.Index,1);Mark(m.Index+1+m.Groups[1].Length,m.Length-1-m.Groups[1].Length);}
        }finally{formatting=false;}
    }
}

#endif
