#if ANDROID
using Android.Content;
using Path=System.IO.Path;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;
namespace ChatAIBox;
public sealed class MarkdownInput : ContentControl
{
    private readonly EditText editor;private readonly MarkdownPipeline pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();private readonly List<Java.Lang.Object> spans=[];private long sequence;private string last="";private bool replacing,wasComposing;
    public event RoutedEventHandler? TextChanged;
    public Func<Task>? PasteRequested;
    public bool AcceptsReturn{get;set;}=true;public TextWrapping TextWrapping{get;set;}
    public string PlaceholderText{get=>editor.Hint??"";set=>editor.Hint=value;}
    public string Text{get=>editor.Text??"";set{if(Text==value)return;replacing=true;editor.Text=value;editor.SetSelection(value.Length);replacing=false;last=value;Schedule();}}
    public long CompositionEndedAt{get;private set;}
    public bool IsComposing{get{var current=editor.EditableText is {} text&&BaseInputConnection.GetComposingSpanStart(text)>=0;if(wasComposing&&!current)CompositionEndedAt=Environment.TickCount64;wasComposing=current;return current;}}
    public MarkdownInput()
    {
        var context=(Context?)Droid.MainActivity.Current??global::Android.App.Application.Context;editor=new EditText(context){Gravity=GravityFlags.Top|GravityFlags.Start,InputType=InputTypes.ClassText|InputTypes.TextFlagMultiLine|InputTypes.TextFlagCapSentences};editor.SetSingleLine(false);editor.SetBackgroundColor(Color.Transparent);editor.SetPadding(4,4,4,4);editor.SetMinLines(2);editor.SetMaxLines(8);Content=editor;HorizontalContentAlignment=HorizontalAlignment.Stretch;VerticalContentAlignment=VerticalAlignment.Stretch;
        editor.TextChanged+=(_,_)=>{var text=Text;if(text==last)return;last=text;if(!replacing)TextChanged?.Invoke(this,new RoutedEventArgs());Schedule();};Loaded+=(_,_)=>Appearance();ActualThemeChanged+=(_,_)=>Appearance();RegisterPropertyChangedCallback(FontSizeProperty,(_,_)=>Appearance());RegisterPropertyChangedCallback(FontFamilyProperty,(_,_)=>Appearance());
    }
    public void InsertText(string text){var content=editor.EditableText;if(content is null)return;var start=Math.Max(0,Math.Min(editor.SelectionStart,editor.SelectionEnd));var end=Math.Max(start,Math.Max(editor.SelectionStart,editor.SelectionEnd));content.Replace(start,end,text);editor.SetSelection(start+text.Length);editor.RequestFocus();((InputMethodManager?)editor.Context?.GetSystemService(Context.InputMethodService))?.ShowSoftInput(editor,ShowFlags.Implicit);}
    public void UndoText()=>editor.OnTextContextMenuItem(global::Android.Resource.Id.Undo);
    public void RedoText()=>editor.OnTextContextMenuItem(global::Android.Resource.Id.Redo);
    private void Appearance()
    {
        var dark=ActualTheme==ElementTheme.Dark;editor.SetTextColor(dark?Color.Rgb(238,232,224):Color.Rgb(37,42,48));editor.SetHintTextColor(dark?Color.Rgb(181,170,160):Color.Rgb(83,91,100));editor.SetTextSize(global::Android.Util.ComplexUnitType.Dip,(float)FontSize);
        try{var source=FontFamily?.Source??"";if(source.StartsWith("ms-appdata:///local/",StringComparison.Ordinal)){var file=Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,source[20..].Split('#')[0]);editor.Typeface=Typeface.CreateFromFile(file);}else if(source.StartsWith("ms-appx:///",StringComparison.Ordinal))editor.Typeface=Typeface.CreateFromAsset(editor.Context!.Assets,source[11..].Split('#')[0]);else editor.Typeface=Typeface.Create(source.Length==0?"sans-serif":source,TypefaceStyle.Normal);}catch{editor.Typeface=Typeface.Default;}Schedule();
    }
    private async void Schedule()
    {
        var own=++sequence;await Task.Delay(120);if(own!=sequence)return;if(IsComposing){Schedule();return;}var text=Text;
        try{var document=await Task.Run(()=>Markdown.Parse(text,pipeline));if(own!=sequence||Text!=text||IsComposing)return;Format(text,document);}catch(Exception error)when(error is ArgumentException or InvalidOperationException){}
    }
    private void Format(string text,MarkdownDocument document)
    {
        if(editor.EditableText is not {} content)return;foreach(var span in spans){content.RemoveSpan(span);span.Dispose();}spans.Clear();var dark=ActualTheme==ElementTheme.Dark;var marker=dark?Color.Rgb(88,83,78):Color.Rgb(182,184,185);
        void Add(Java.Lang.Object span,int start,int length){start=Math.Clamp(start,0,text.Length);var end=Math.Clamp(start+length,start,text.Length);if(end<=start){span.Dispose();return;}content.SetSpan(span,start,end,SpanTypes.ExclusiveExclusive);spans.Add(span);}
        void Mark(int start,int length)=>Add(new ForegroundColorSpan(marker),start,length);
        foreach(var h in document.Descendants<HeadingBlock>()){Add(new RelativeSizeSpan(h.Level==1?1.55f:h.Level==2?1.3f:1.12f),h.Span.Start,h.Span.Length);Add(new StyleSpan(TypefaceStyle.Bold),h.Span.Start,h.Span.Length);}
        foreach(var em in document.Descendants<EmphasisInline>()){Add(em.DelimiterChar=='~'?new StrikethroughSpan():new StyleSpan(em.DelimiterCount>=2?TypefaceStyle.Bold:TypefaceStyle.Italic),em.Span.Start,em.Span.Length);Mark(em.Span.Start,em.DelimiterCount);Mark(em.Span.End-em.DelimiterCount+1,em.DelimiterCount);}
        foreach(var code in document.Descendants<CodeInline>()){Add(new TypefaceSpan("monospace"),code.Span.Start,code.Span.Length);Mark(code.Span.Start,code.DelimiterCount);Mark(code.Span.End-code.DelimiterCount+1,code.DelimiterCount);}
        foreach(var code in document.Descendants<FencedCodeBlock>())Add(new TypefaceSpan("monospace"),code.Span.Start,code.Span.Length);
        foreach(Match mark in Regex.Matches(text,@"(?m)^\s*(?:#{1,6}\s|>+\s?|[-+*]\s+|\d+[.)]\s+|`{3,}[^\r\n]*|~{3,}[^\r\n]*)"))Mark(mark.Index,mark.Length);
        foreach(Match mark in Regex.Matches(text,@"\[([^\]\r\n]+)\]\(([^)\r\n]+)\)")){Mark(mark.Index,1);Mark(mark.Index+1+mark.Groups[1].Length,mark.Length-1-mark.Groups[1].Length);}
    }
}
#endif
