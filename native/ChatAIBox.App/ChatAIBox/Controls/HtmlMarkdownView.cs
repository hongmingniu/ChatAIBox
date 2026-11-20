using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#else
using Microsoft.Web.WebView2.Core;
#endif
namespace ChatAIBox;
public sealed class HtmlMarkdownView : WebView2
{
    private string markdown;private bool dark;private double size;private readonly Action<string>? error;private readonly Func<string,Task<(byte[],string)>>? image;
    private bool ready,initialized,loading,delivering;private long generation,renderVersion,confirmedVersion=-1;
    public Action<bool>? RenderAvailabilityChanged;
    public Action? SelectionStarted;public Func<IReadOnlyList<Windows.Storage.StorageFile>,Task>? FilesDropped;public Action<bool>? FileDragChanged;
    public void Update(string text,bool isDark,double fontSize){markdown=text;dark=isDark;size=fontSize;renderVersion++;_=WatchRenderAsync(renderVersion);if(ready)_=DeliverAsync();}
    private async Task WatchRenderAsync(long version){await Task.Delay(8000);if(IsLoaded&&version==renderVersion&&confirmedVersion!=version)RenderAvailabilityChanged?.Invoke(false);}
    private async Task DeliverAsync()
    {
        if(delivering||!ready||!IsLoaded)return;delivering=true;
        try{long sent;do{sent=renderVersion;var result=await ExecuteScriptAsync("typeof window.nativeRender==='function' ? (window.nativeRender("+Json.Write(new{markdown,dark,fontSize=size,version=sent})+"), true) : false");if(result=="false"){ready=false;RenderAvailabilityChanged?.Invoke(false);break;}}while(ready&&IsLoaded&&sent!=renderVersion);}
        catch(Exception exception){ready=false;RenderAvailabilityChanged?.Invoke(false);error?.Invoke("Markdown 已切换为文字显示："+exception.Message);}
        finally{delivering=false;}
    }
    public HtmlMarkdownView(string markdown,bool dark,double size,Action<string>? error,Func<string,Task<(byte[],string)>>? image)
    {
        this.markdown=markdown;this.dark=dark;this.size=size;this.error=error;this.image=image;Height=1;HorizontalAlignment=HorizontalAlignment.Stretch;Loaded+=Load;Unloaded+=(_,_)=>{confirmedVersion=-1;ready=false;loading=false;generation++;RenderAvailabilityChanged?.Invoke(false);};
    }
    private async void Load(object sender,RoutedEventArgs e)
    {
        if(loading)return;loading=true;var own=++generation;
        try{
            if(initialized){var present=await ExecuteScriptAsync("typeof window.nativeRender==='function'");if(own!=generation||!IsLoaded)return;if(present=="true"){ready=true;await DeliverAsync();}else CoreWebView2.Navigate("https://chataibox.markdown/index.html");return;}
            await EnsureCoreWebView2Async();if(own!=generation||!IsLoaded)return;
#if WINDOWS
            CoreWebView2.Settings.AreDevToolsEnabled=false;CoreWebView2.Settings.AreHostObjectsAllowed=false;CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;DefaultBackgroundColor=Windows.UI.Color.FromArgb(0,0,0,0);
            var folder=Path.Combine(AppContext.BaseDirectory,"Assets","renderer");
#else
            var folder="Assets/renderer";
#endif
            CoreWebView2.SetVirtualHostNameToFolderMapping("chataibox.markdown",folder,CoreWebView2HostResourceAccessKind.DenyCors);
            CoreWebView2.WebMessageReceived+=async(_,args)=>{try{
                var message=JsonNode.Parse(args.WebMessageAsJson);if(message is JsonValue value&&value.TryGetValue<string>(out var encoded))message=JsonNode.Parse(encoded);if(message is not JsonObject data)return;
                switch(data["type"]?.ToString()){
                    case "fileDrag":FileDragChanged?.Invoke(data["active"]?.GetValue<bool>()==true);break;
                    case "dropUnsupported":FileDragChanged?.Invoke(false);error?.Invoke("此区域暂不支持拖放，请使用附件按钮选择文件");break;
#if WINDOWS
                    case "dropFiles":{
                        FileDragChanged?.Invoke(false);
                        if(!args.Source.StartsWith("https://chataibox.markdown/",StringComparison.OrdinalIgnoreCase))break;
                        var dropped=args.AdditionalObjects.OfType<CoreWebView2File>().ToArray();
                        if(dropped.Length>10)throw new InvalidOperationException("一次最多拖入 10 个文件");
                        var files=new List<Windows.Storage.StorageFile>();
                        foreach(var file in dropped){if(string.IsNullOrWhiteSpace(file.Path))continue;files.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Path));}
                        if(files.Count>0&&FilesDropped is not null)await FilesDropped(files);
                        else error?.Invoke("请拖入具体文件，文件夹无法作为附件添加");break;
                    }
#endif
                    case "ready":ready=true;if(IsLoaded)await DeliverAsync();break;
                    case "rendered":if(data["version"]?.GetValue<long>()==renderVersion){confirmedVersion=renderVersion;Height=Math.Clamp(data["height"]?.GetValue<double>()??80,30,9000);RenderAvailabilityChanged?.Invoke(true);}break;
                    case "renderError":if(data["version"]?.GetValue<long>()==renderVersion){RenderAvailabilityChanged?.Invoke(false);error?.Invoke("Markdown 已切换为文字显示："+(data["message"]?.ToString()??"渲染异常"));}break;
                    case "height":if(confirmedVersion==renderVersion)Height=Math.Clamp(data["value"]?.GetValue<double>()??80,30,9000);break;
                    case "copy":NativeActions.Copy(data["text"]?.ToString()??"");break;
                    case "selection":if(data["active"]?.GetValue<bool>()==true)SelectionStarted?.Invoke();break;
                    case "selectionMenu":{
                        var selected=data["text"]?.ToString()??"";if(selected.Length==0)break;var menu=new MenuFlyout();var copy=new MenuFlyoutItem{Text="复制所选文字"};copy.Click+=(_,_)=>{try{NativeActions.Copy(selected);}catch(Exception exception){error?.Invoke(exception.Message);}};menu.Items.Add(copy);var x=Math.Clamp(data["x"]?.GetValue<double>()??0,0,ActualWidth);var y=Math.Clamp(data["y"]?.GetValue<double>()??0,0,ActualHeight);menu.ShowAt(this,new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions{Position=new Windows.Foundation.Point(x,y)});break;
                    }
                    case "download":{var extension=data["language"]?.ToString() switch{"javascript" or "js"=>"js","typescript" or "ts"=>"ts","python" or "py"=>"py","csharp" or "cs"=>"cs","cpp"=>"cpp","json"=>"json","html"=>"html","css"=>"css","java"=>"java","go"=>"go","rust" or "rs"=>"rs","bash" or "sh"=>"sh",_=>"txt"};await NativeActions.SaveTextAsync("code."+extension,data["text"]?.ToString()??"");break;}
                    case "link":await NativeActions.OpenLinkAsync(data["url"]?.ToString()??"");break;
                    case "image":if(image is not null){var (bytes,mime)=await image(data["url"]?.ToString()??"");await ExecuteScriptAsync("window.nativeImage("+Json.Write(data["id"]?.ToString())+","+Json.Write("data:"+mime+";base64,"+Convert.ToBase64String(bytes))+")");}break;
                }
            }catch(Exception exception){error?.Invoke(exception.Message);}};
            CoreWebView2.NavigationStarting+=(_,args)=>{var uri=args.Uri;if(uri=="about:blank")return;if(uri.StartsWith("https://chataibox.markdown/",StringComparison.OrdinalIgnoreCase)||uri.StartsWith("file:///android_asset/Assets/renderer/index.html",StringComparison.Ordinal))return;args.Cancel=true;};
            #if WINDOWS
            CoreWebView2.ProcessFailed+=(_,_)=>{ready=false;RenderAvailabilityChanged?.Invoke(false);error?.Invoke("Markdown 显示进程中断，已保留文字显示");};
#endif
            CoreWebView2.NavigationCompleted+=(_,args)=>{if(!args.IsSuccess){ready=false;RenderAvailabilityChanged?.Invoke(false);error?.Invoke("Markdown 页面加载失败，已保留文字显示");}};
            initialized=true;CoreWebView2.Navigate("https://chataibox.markdown/index.html");
        }catch(Exception exception){RenderAvailabilityChanged?.Invoke(false);error?.Invoke("复杂 Markdown 渲染不可用，已保留文字："+exception.Message);}finally{if(own==generation)loading=false;}
    }
}
