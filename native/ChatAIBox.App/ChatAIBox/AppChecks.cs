#if APP_CHECKS && WINDOWS
using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
namespace ChatAIBox;
public sealed partial class MainPage
{
    private async Task RunAppChecksAsync()
    {
        var checks=new List<string>();var root=App.Storage.Root;var result=Path.Combine(root,"app-checks.json");
        void Check(bool passed,string label){if(!passed)throw new Exception(label);checks.Add(label);}
        try{
            if(NativeStorage.ProfileOverride is null)throw new InvalidOperationException("App checks require an isolated profile");
            await Task.Delay(500);Check(engine is not null,"engine initialized");var app=engine!;
            var window=System.Text.Json.JsonSerializer.SerializeToElement(NativeWindowAppearance.Inspect(App.MainWindow!));Check(!window.GetProperty("caption").GetBoolean()&&window.GetProperty("topInset").GetInt32()==0&&window.GetProperty("leftInset").GetInt32()==0,"frameless client covers window; no legacy frame strip");Check(window.GetProperty("hostBackdrop").GetBoolean()||window.GetProperty("systemBackdrop").GetBoolean(),"native glass initialized");
            DraftBox.Text="**你好🙂**";DraftBox.Document.Selection.SetRange(DraftBox.Text.Length,DraftBox.Text.Length);DraftBox.InsertText("测试");await Task.Delay(200);Check(DraftBox.Text=="**你好🙂**测试","native editor Unicode insertion");DraftBox.UndoText();Check(DraftBox.Text=="**你好🙂**","native editor undo retains original Markdown");DraftBox.RedoText();Check(DraftBox.Text.EndsWith("测试"),"native editor redo");DraftBox.Text="";
            using(var surface=SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(16,16))){surface.Canvas.Clear(SkiaSharp.SKColors.Beige);using var snapshot=surface.Snapshot();using var encoded=snapshot.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);var image=await MarkdownView.ImageSourceAsync(encoded.ToArray());Check(image.PixelWidth==16,"native image stream decodes before disposal");}
            var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Assets/fonts/HarmonyOS_Sans_SC_Regular.ttf"));var font=await FontService.ImportAsync(bytes);var selected=FontService.Resolve(app.Settings with{FontFileId=font.Id,FontFamily=font.Family});Check(selected.Source.Contains(font.Id),"custom font imported and resolved");
            var id=Guid.NewGuid().ToString();var rows=new List<ChatRecord>{new(){Id=id,Rev=app.Store.NextRevision(),Type="chat",Title="脚本合成测试",Created=1}};for(var i=0;i<1000;i++)rows.Add(new(){Id=Guid.NewGuid().ToString(),Rev=app.Store.NextRevision(),Type="message",ChatId=id,Role=i%2==0?"user":"assistant",Content="第 "+i+" 条合成消息：**Markdown** 与中文🙂",Created=i+2});await app.Store.MergeAsync(rows);Select(id);await Task.Delay(300);Check(MessageList.Items.Count<=12&&OlderButton.IsEnabled,"1000-message conversation renders only current page");
            page=0;foreach(var theme in new[]{"light","dark"}){await app.SaveSettingsAsync(app.Settings with{Theme=theme});await Task.Delay(300);Refresh();await CaptureCheckAsync(theme+"-history.png");}
            Select("");await Task.Delay(150);await CaptureCheckAsync("dark-empty.png");
            var api=Environment.GetEnvironmentVariable("CHATAIBOX_APP_CHECKS_API_URL")??throw new InvalidOperationException("Synthetic API URL required");await app.SaveSettingsAsync(app.Settings with{ActiveProviderId="fixture",Model="fixture",Providers=[new(){Id="fixture",Name="Synthetic",BaseUrl=api,ApiKey="fixture-key",Models=["fixture"],Search=false,Vision=false,Thinking="none",ContextWindow=131072,MaxOutputTokens=24576}]});var chat=await app.SendAsync("","script fixture",[],false,false);Select(chat);await Task.Delay(120);Select(id);Check(active==id,"can switch conversations during streaming");for(var i=0;i<150&&app.Jobs[chat].Busy;i++)await Task.Delay(100);Check(!app.Jobs[chat].Busy&&app.Store.Messages(chat).Any(r=>r.Role=="assistant"&&r.Content!.Contains("stream fixture")),"native application receives and saves streamed output");Select(chat);await Task.Delay(500);await CaptureCheckAsync("dark-stream.png");
            var markdown=cards.Values.LastOrDefault();Check(markdown is not null,"native message cards available");
            await File.WriteAllTextAsync(result,Json.Write(new{pass=true,checks,window}));
        }catch(Exception error){await File.WriteAllTextAsync(result,Json.Write(new{pass=false,checks,error=error.ToString()}));}
        finally{App.CloseApproved=true;App.MainWindow?.Close();}
    }
    private async Task CaptureCheckAsync(string name)
    {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);var pixels=(await bitmap.GetPixelsAsync()).ToArray();if(pixels.Length==0)throw new InvalidOperationException("Native render capture returned no pixels");var folder=await StorageFolder.GetFolderFromPathAsync(App.Storage.Root);var file=await folder.CreateFileAsync(name,CreationCollisionOption.ReplaceExisting);using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
}
#endif
