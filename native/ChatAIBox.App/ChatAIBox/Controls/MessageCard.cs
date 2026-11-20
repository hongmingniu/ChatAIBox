using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ChatAIBox;
public sealed class MessageCard : ContentControl
{
    private readonly StackPanel layout=new(){Spacing=10};
    private Microsoft.UI.Xaml.Controls.UIElementCollection Children=>layout.Children;
    private ChatRecord row;private readonly MarkdownView body=new();private readonly Expander thinking=new(),searches=new();private readonly TextBlock partial=new(){Text="回复未完整生成，收到的内容已保留",FontSize=11,Opacity=.7};
    private readonly Func<string,Task> speech;private readonly Action<string> error;private readonly StackPanel images=new(){Orientation=Orientation.Horizontal,Spacing=8};
    public string Id=>row.Id;
    public MessageCard(ChatRecord row,double fontSize,Func<string,Task> speech,Action<string> error,Func<string,Task<(byte[],string)>> fetch,Func<Task>? retry=null,Action? selectionStarted=null,Func<IReadOnlyList<Windows.Storage.StorageFile>,Task>? filesDropped=null,Action<bool>? fileDragChanged=null)
    {
        this.row=row;this.speech=speech;this.error=error;Content=layout;Margin=new Thickness(0,10,0,28);HorizontalAlignment=HorizontalAlignment.Stretch;HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var header=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};header.Children.Add(new Border{Width=28,Height=28,CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(25,173,83,44)),Child=new TextBlock{Text=row.Role=="user"?"你":"AI",FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});header.Children.Add(new TextBlock{Text=(row.Role=="user"?"你":row.Model??"助手"),FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});Children.Add(header);
        thinking.Header="思考过程";thinking.HorizontalAlignment=HorizontalAlignment.Stretch;thinking.Expanding+=(_,_)=>{if(thinking.Content is not MarkdownView)thinking.Content=new MarkdownView{Markdown=this.row.Reasoning??"",FontSize=fontSize,Error=error,FetchImage=fetch,SelectionStarted=selectionStarted,FilesDropped=filesDropped,FileDragChanged=fileDragChanged};};Children.Add(thinking);
        searches.Header="联网搜索";searches.HorizontalAlignment=HorizontalAlignment.Stretch;Children.Add(searches);
        Children.Add(images);body.FontSize=fontSize;body.Error=error;body.FetchImage=fetch;body.SelectionStarted=selectionStarted;body.FilesDropped=filesDropped;body.FileDragChanged=fileDragChanged;Children.Add(body);Children.Add(partial);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};var copy=Button("复制");copy.Click+=(_,_)=>{try{NativeActions.Copy(this.row.Content??"");}catch(Exception e){error(e.Message);}};var read=Button("朗读");read.Click+=async(_,_)=>{try{await this.speech(this.row.Content??"");}catch(Exception e){error(e.Message);}};actions.Children.Add(copy);actions.Children.Add(read);if(retry is not null){var again=Button("重新请求回复");again.Click+=async(_,_)=>{try{await retry();}catch(Exception e){error(e.Message);}};actions.Children.Add(again);}Children.Add(actions);Update(row);
    }
    private static Button Button(string text)=>new(){Content=text,FontSize=11,Padding=new Thickness(5,3,5,3),BorderThickness=new Thickness(0),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(0,0,0,0))};
    public void Update(ChatRecord value)
    {
        var old=row;row=value;body.Markdown=row.Content??"";thinking.Visibility=string.IsNullOrEmpty(row.Reasoning)?Visibility.Collapsed:Visibility.Visible;if(thinking.Content is MarkdownView thought)thought.Markdown=row.Reasoning??"";
        var search=row.Searches??[];searches.Visibility=search.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(searches.Content is null||!ReferenceEquals(old.Searches,row.Searches)){var stack=new StackPanel{Spacing=8};foreach(var trace in search){var label=string.Join(" · ",trace.Queries);if(label.Length>0)stack.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap});if(trace.Url is not null){var link=new HyperlinkButton{Content=trace.Url,Padding=new Thickness(0)};link.Click+=async(_,_)=>{try{await NativeActions.OpenLinkAsync(trace.Url);}catch(Exception e){error(e.Message);}};stack.Children.Add(link);}}searches.Content=stack;}
        partial.Visibility=row.Interrupted==true?Visibility.Visible:Visibility.Collapsed;
        if(images.Children.Count==0&&row.Images is {Length:>0})LoadImages();
    }
    private async void LoadImages(){foreach(var image in row.Images??[]){try{var comma=image.DataUrl.IndexOf(',');var bitmap=await MarkdownView.ImageSourceAsync(Convert.FromBase64String(image.DataUrl[(comma+1)..]));images.Children.Add(new Image{Source=bitmap,MaxWidth=240,MaxHeight=200,Stretch=Stretch.Uniform});}catch(Exception e){error("图片无法显示："+e.Message);}}}
}
