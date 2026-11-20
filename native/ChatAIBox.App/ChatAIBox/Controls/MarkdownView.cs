using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Mathematics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using MdBlock=Markdig.Syntax.Block;
using MdInline=Markdig.Syntax.Inlines.Inline;
using XInline=Microsoft.UI.Xaml.Documents.Inline;
namespace ChatAIBox;
public sealed class MarkdownView : ContentControl
{
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMathematics().Build();
    private string pending="",rendered="";private bool working,selectionActive;private long sequence;
    private HtmlMarkdownView? complexView;private Grid? complexHost;private TextBlock? complexFallback;
    public Action<string>? Error;public Func<string,Task<(byte[],string)>>? FetchImage;public Action? SelectionStarted;public Func<IReadOnlyList<Windows.Storage.StorageFile>,Task>? FilesDropped;public Action<bool>? FileDragChanged;
    public string Markdown{get=>pending;set{if(pending==value)return;pending=value;sequence++;Render();}}
    public MarkdownView(){HorizontalContentAlignment=HorizontalAlignment.Stretch;ActualThemeChanged+=(_,_)=>{rendered="";Render();};Unloaded+=(_,_)=>selectionActive=false;}
    private async void Render()
    {
        if(working||selectionActive||rendered==pending)return;working=true;
        try{while(rendered!=pending){await Task.Delay(100);if(selectionActive)break;var text=pending;var own=sequence;var ast=await Task.Run(()=>Markdig.Markdown.Parse(text,Pipeline));if(own!=sequence)continue;if(selectionActive)break;var document=BuildDocument(ast,text);if(!ReferenceEquals(Content,document))Content=document;rendered=text;}}
        catch(Exception e){Content=new TextBlock{Text=pending,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};Error?.Invoke("Markdown 渲染失败："+e.Message);rendered=pending;}
        finally{working=false;}
    }
    private FrameworkElement BuildDocument(MarkdownDocument ast,string source)
    {
        var complex=ast.Descendants().Any(n=>n is MathBlock or MathInline or HtmlBlock or HtmlInline or CodeBlock or Table||n.GetType().Namespace?.Contains("Footnotes",StringComparison.Ordinal)==true||n.GetType().Namespace?.Contains("DefinitionLists",StringComparison.Ordinal)==true);
        if(complex){
            if(complexView is null){
                complexFallback=new TextBlock{TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=FontSize};
                complexView=new HtmlMarkdownView(source,ActualTheme==ElementTheme.Dark,FontSize,Error,FetchImage){Opacity=0,IsHitTestVisible=false};
                complexHost=new Grid();complexHost.Children.Add(complexFallback);complexHost.Children.Add(complexView);
                complexView.RenderAvailabilityChanged=available=>{complexFallback.Visibility=available?Visibility.Collapsed:Visibility.Visible;complexView.Opacity=available?1:0;complexView.IsHitTestVisible=available;if(!available)complexView.Height=1;};
            }
            complexFallback!.Text=source;complexFallback.FontSize=FontSize;complexFallback.FontFamily=FontFamily;
            complexView.SelectionStarted=SelectionStarted;complexView.FilesDropped=FilesDropped;complexView.FileDragChanged=FileDragChanged;complexView.Update(source,ActualTheme==ElementTheme.Dark,FontSize);return complexHost!;
        }
        // Keep paragraphs, headings, lists and quotations in one native selection range.
        var text=new RichTextBlock{FontSize=FontSize,FontFamily=FontFamily,IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap,LineHeight=FontSize*1.85};
        text.SelectionChanged+=(_,_)=>{selectionActive=text.SelectedText.Length>0;if(selectionActive)SelectionStarted?.Invoke();else Render();};text.LostFocus+=(_,_)=>{selectionActive=false;Render();};
        text.ContextRequested+=(_,args)=>{
            var selected=text.SelectedText;if(selected.Length==0)return;args.Handled=true;var menu=new MenuFlyout();var copy=new MenuFlyoutItem{Text="复制所选文字"};copy.Click+=(_,_)=>{try{NativeActions.Copy(selected);}catch(Exception error){Error?.Invoke(error.Message);}};menu.Items.Add(copy);
            if(args.TryGetPosition(text,out var point))menu.ShowAt(text,new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions{Position=point});else menu.ShowAt(text);
        };
        foreach(var block in ast)AppendBlock(text,block);return text;
    }
    private void AppendBlock(RichTextBlock target,MdBlock block,int indent=0,string prefix="",bool quoted=false)
    {
        if(block is ListBlock list){var number=int.TryParse(list.OrderedStart,out var start)?start:1;foreach(var item in list.OfType<ListItemBlock>()){var first=true;foreach(var child in item){AppendBlock(target,child,indent+1,first?(list.IsOrdered?(number++)+". ":"• "):"",quoted);first=false;}}return;}
        if(block is QuoteBlock quote){foreach(var child in quote)AppendBlock(target,child,indent,prefix,true);return;}
        if(block is ContainerBlock container){foreach(var child in container)AppendBlock(target,child,indent,prefix,quoted);return;}
        var paragraph=new Paragraph{Margin=new Thickness(indent*20+(quoted?12:0),0,0,10)};
        if(quoted)paragraph.Inlines.Add(new Run{Text="│ ",Foreground=Brush("AppAccentBrush")});if(prefix.Length>0)paragraph.Inlines.Add(new Run{Text=prefix});
        if(block is HeadingBlock heading){paragraph.FontSize=FontSize*(heading.Level==1?1.7:heading.Level==2?1.4:1.2);paragraph.FontWeight=FontWeights.SemiBold;paragraph.Margin=new Thickness(paragraph.Margin.Left,8,0,8);}
        if(block is ThematicBreakBlock)paragraph.Inlines.Add(new Run{Text="────────",Foreground=Brush("SurfaceStrokeColorDefaultBrush")});
        else if(block is LeafBlock leaf&&leaf.Inline is not null)foreach(var inline in leaf.Inline)paragraph.Inlines.Add(Inline(inline));
        target.Blocks.Add(paragraph);
    }
    private XInline Inline(MdInline inline)
    {
        switch(inline){
            case LiteralInline literal:return new Run{Text=literal.Content.ToString()};
            case HtmlEntityInline entity:return new Run{Text=entity.Transcoded.ToString()};
            case LineBreakInline line:return line.IsHard?new LineBreak():new Run{Text="\n"};
            case CodeInline code:return new Run{Text=code.Content,FontFamily=new FontFamily(OperatingSystem.IsAndroid()?"monospace":"Consolas"),Foreground=Brush("AppAccentBrush")};
            case EmphasisInline em:{var span=new Span();if(em.DelimiterChar=='~')span.TextDecorations=Windows.UI.Text.TextDecorations.Strikethrough;else if(em.DelimiterCount>=2)span.FontWeight=FontWeights.Bold;else span.FontStyle=Windows.UI.Text.FontStyle.Italic;foreach(var child in em)span.Inlines.Add(Inline(child));return span;}
            case AutolinkInline link:{var hyperlink=new Hyperlink();hyperlink.Inlines.Add(new Run{Text=link.Url});hyperlink.Click+=async(_,_)=>{try{await NativeActions.OpenLinkAsync(link.IsEmail?"mailto:"+link.Url:link.Url);}catch(Exception e){Error?.Invoke(e.Message);}};return hyperlink;}
            case LinkInline link when link.IsImage:{var image=new StackPanel{Spacing=6};var button=new Button{Content="加载图片："+(link.Title??link.Url??"图片")};button.Click+=async(_,_)=>{try{if(FetchImage is null)throw new InvalidOperationException("图片加载不可用");var data=await FetchImage(link.Url??"");var source=await ImageSourceAsync(data.Item1);image.Children.Clear();image.Children.Add(new Image{Source=source,MaxHeight=400,MaxWidth=560,Stretch=Stretch.Uniform});}catch(Exception e){Error?.Invoke(e.Message);}};image.Children.Add(button);return new InlineUIContainer{Child=image};}
            case LinkInline link:{var hyperlink=new Hyperlink{Foreground=Brush("AppAccentBrush")};foreach(var child in link)hyperlink.Inlines.Add(Inline(child));hyperlink.Click+=async(_,_)=>{try{await NativeActions.OpenLinkAsync(link.Url??"");}catch(Exception e){Error?.Invoke(e.Message);}};return hyperlink;}
            case TaskList task:return new Run{Text=task.Checked?"☑ ":"☐ "};
            case ContainerInline container:{var span=new Span();foreach(var child in container)span.Inlines.Add(Inline(child));return span;}
            default:return new Run{Text=inline.ToString()??""};
        }
    }
    private Brush Brush(string key){var dark=ActualTheme==ElementTheme.Dark;var color=key switch{"AppAccentBrush"=>dark?Windows.UI.Color.FromArgb(255,241,177,133):Windows.UI.Color.FromArgb(255,155,71,31),"AppCodeBrush"=>dark?Windows.UI.Color.FromArgb(255,33,31,29):Windows.UI.Color.FromArgb(255,240,241,240),_=>dark?Windows.UI.Color.FromArgb(36,231,209,187):Windows.UI.Color.FromArgb(36,40,48,58)};return new SolidColorBrush(color);}
    public static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> ImageSourceAsync(byte[] bytes){using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();using var writer=new Windows.Storage.Streams.DataWriter(stream);writer.WriteBytes(bytes);await writer.StoreAsync();stream.Seek(0);var bitmap=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();await bitmap.SetSourceAsync(stream);return bitmap;}
}
