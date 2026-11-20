using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using Windows.Foundation;
namespace ChatAIBox;
public sealed partial class MainPage : Page
{
    private ChatEngine? engine;private bool switching,loaded;private string active="",chatSignature="",rowSignature="";private long historyVersion=-1;private string historyChat="";private ChatRecord[] history=[];private int page;private int queued;private string? followId;private bool following=true;
    private readonly Dictionary<string,string> drafts=new();private readonly Dictionary<string,List<ImageAttachment>> attachments=new();private readonly Dictionary<string,MessageCard> cards=new();private readonly List<(string Provider,string Model,string Label)> modelChoices=[];private CancellationTokenSource? speech;private IRecording? recording;private CancellationTokenSource? recordingTimer;
    public UIElement DragRegion=>TitleDragRegion;
    private void MinimizeClicked(object sender,RoutedEventArgs e){
#if WINDOWS
        (App.MainWindow?.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.Minimize();
#endif
    }
    private void MaximizeClicked(object sender,RoutedEventArgs e){
#if WINDOWS
        if(App.MainWindow?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p){if(p.State==Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)p.Restore();else p.Maximize();}
#endif
    }
    private void CloseClicked(object sender,RoutedEventArgs e)=>App.MainWindow?.Close();
    public MainPage(){InitializeComponent();InitializeFileDrop();DraftBox.PasteRequested=PasteIntoDraftAsync;ActualThemeChanged+=(_,_)=>{if(engine is not null)ApplyBackground(engine.Settings);};MessageList.PointerWheelChanged+=(_,_)=>following=false;MessageList.PointerPressed+=(_,_)=>following=false;}
    private async void PageLoaded(object sender,RoutedEventArgs e)
    {
        if(loaded)return;loaded=true;try{engine=await App.EngineAsync();engine.Changed+=QueueRefresh;engine.SettingsChanged+=()=>DispatcherQueue.TryEnqueue(ApplySettings);ApplySettings();NativeActions.EnsureBackgroundSync(engine.Settings.SyncEnabled);Refresh();
#if ANDROID
            _=Droid.StartupCompatibility.ConfirmReadyAsync();
#endif
#if APP_CHECKS
            if(Environment.GetEnvironmentVariable("CHATAIBOX_APP_CHECKS")=="1")_=RunAppChecksAsync();
#endif
        }catch(Exception error){ShowError("无法读取本机数据："+error.Message+"。旧版数据未被覆盖，请保留原应用与备份。");}
    }
    public void ShowError(string text){DispatcherQueue.TryEnqueue(()=>{ErrorBar.Message=text;ErrorBar.IsOpen=true;});}
    private void QueueRefresh(){if(Interlocked.Exchange(ref queued,1)!=0)return;DispatcherQueue.TryEnqueue(()=>{Interlocked.Exchange(ref queued,0);Refresh();});}
    private void ApplySettings()
    {
        if(engine is null)return;var settings=engine.Settings;RequestedTheme=settings.Theme switch{"light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};var selectedFont=FontService.Resolve(settings);FontFamily=settings.FontScope=="chat"?FontService.Default:selectedFont;DraftBox.FontFamily=selectedFont;cards.Clear();
        DraftBox.FontSize=settings.FontSize;ApplyBackground(settings);switching=true;modelChoices.Clear();foreach(var provider in new[]{new Provider{ApiKey=settings.ApiKey}}.Concat(settings.Providers))foreach(var model in provider.Models)modelChoices.Add((provider.Id,model,provider.Name+" · "+model));ModelBox.ItemsSource=modelChoices.Select(m=>m.Label).ToArray();ModelBox.SelectedIndex=modelChoices.FindIndex(m=>m.Provider==settings.ActiveProviderId&&m.Model==settings.Model);switching=false;rowSignature="";Refresh();
    }
    private void Refresh()
    {
        if(engine is null)return;
        var chats=engine.Store.Chats(HistorySearch.Text??"");var signature=string.Join("|",chats.Select(r=>r.Id+":"+r.Rev));if(signature!=chatSignature){switching=true;ChatList.ItemsSource=chats;ChatList.SelectedItem=chats.FirstOrDefault(c=>c.Id==active);switching=false;chatSignature=signature;}
        if(historyVersion!=engine.Store.Version||historyChat!=active){history=engine.Store.Messages(active);historyVersion=engine.Store.Version;historyChat=active;}
        var lastPage=Math.Max(0,(history.Length-1)/12);page=Math.Clamp(page,0,lastPage);var end=Math.Max(0,history.Length-page*12);var start=Math.Max(0,end-12);var visible=history[start..end].ToList();engine.Jobs.TryGetValue(active,out var job);if(page==0&&job?.Live is { } live)visible.Add(live);
        Welcome.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;HistoryPanel.Visibility=visible.Count>0?Visibility.Visible:Visibility.Collapsed;Pager.Visibility=history.Length>12?Visibility.Visible:Visibility.Collapsed;OlderButton.IsEnabled=page<lastPage;NewerButton.IsEnabled=page>0;PageLabel.Text=$"{start+1}–{end} / {history.Length}";
        var rowIds=string.Join("|",visible.Select(r=>r.Id))+":"+engine.Settings.FontSize+":"+active;
        if(rowIds!=rowSignature){MessageList.Items.Clear();var keep=new HashSet<string>();foreach(var row in visible){keep.Add(row.Id);if(!cards.TryGetValue(row.Id,out var card)){var retry=row.Role=="user"&&history.LastOrDefault()?.Id==row.Id?(Func<Task>)(()=>RetryAsync()):null;card=new MessageCard(row,engine.Settings.FontSize,ReadSpeechAsync,ShowError,url=>new MediaClient(()=>engine.Settings,!App.Storage.IsPhone).FetchImageAsync(url,CancellationToken.None),retry,()=>following=false,ReceiveRendererFilesAsync,ShowFileDropHint);card.FontFamily=FontService.Resolve(engine.Settings);cards[row.Id]=card;}else card.Update(row);MessageList.Items.Add(card);}foreach(var old in cards.Keys.Where(k=>!keep.Contains(k)).ToArray())cards.Remove(old);rowSignature=rowIds;}
        else foreach(var row in visible)cards[row.Id].Update(row);
        PhaseLabel.Text=job?.Phase??"";PhaseLabel.Visibility=string.IsNullOrEmpty(PhaseLabel.Text)?Visibility.Collapsed:Visibility.Visible;ContextLabel.Text=string.IsNullOrEmpty(job?.ContextInfo)?"模型输出可能不准确，请核实重要信息。":job.ContextInfo;
        if(job?.Error is {Length:>0} problem){ErrorBar.Message=problem;ErrorBar.IsOpen=true;}
        SendButton.Content=job?.Busy==true?"■":"↑";SyncLabel.Text=engine.Settings.SyncEnabled?(engine.Sync.Error.Length>0?engine.Sync.Error:App.Storage.IsPhone?engine.Sync.Connection.Connected?"手机主机在线":"正在连接":engine.Sync.Connection.PhoneOnline?engine.Sync.Pending?"正在同步至手机":"已连接手机":engine.Sync.Pending?"手机离线 · 消息暂存在内存":"等待手机在线"):engine.Sync.Pending?"有消息尚未保存到手机":"未开启";
        if(job?.Live is { } target&&followId!=target.Id){followId=target.Id;if(page==0&&following)ScrollReplyOnce(target.Id);}
    }
    private long scrollRequest;
    private async void ScrollReplyOnce(string id)
    {
        var request=++scrollRequest;var chat=active;
        try{
            await Task.Delay(160);
            if(request!=scrollRequest||!following||active!=chat||page!=0||!cards.TryGetValue(id,out var card))return;
            MessageList.UpdateLayout();var scroll=Find<ScrollViewer>(MessageList);if(scroll is null)return;
            ReplySpacer.Height=Math.Max(0,scroll.ViewportHeight-card.ActualHeight-12);MessageList.UpdateLayout();
            var top=card.TransformToVisual(scroll).TransformPoint(new Point(0,0)).Y;
            var desired=Math.Clamp(scroll.VerticalOffset+top-8,0,scroll.ScrollableHeight);
            if(Math.Abs(desired-scroll.VerticalOffset)>2)scroll.ChangeView(null,desired,null,false);
        }catch(Exception error){ShowError("无法定位消息："+error.Message);}
    }
    private static T? Find<T>(DependencyObject node)where T:DependencyObject{if(node is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=Find<T>(VisualTreeHelper.GetChild(node,i));if(child is not null)return child;}return null;}
    private void Select(string id)
    {
        active=id;switching=true;ChatList.SelectedItem=engine?.Store.Chats().FirstOrDefault(c=>c.Id==id);switching=false;page=0;following=true;followId=null;scrollRequest++;ReplySpacer.Height=0;rowSignature="";switching=true;DraftBox.Text=drafts.GetValueOrDefault(id)??"";switching=false;RefreshAttachments();if(ActualWidth<800)Shell.IsPaneOpen=false;Refresh();
    }
    private void NewChatClicked(object sender,RoutedEventArgs e){if(documentReads.Contains("")){ShowError("文件正在读取，请稍候");return;}drafts[""]="";attachments[""]=[];documents[""]=[];Select("");}
    private void HistorySearchChanged(object sender,TextChangedEventArgs e){chatSignature="";Refresh();}
    private void ChatSelectionChanged(object sender,SelectionChangedEventArgs e){if(!switching&&ChatList.SelectedItem is ChatRecord row)Select(row.Id);}
    private void DraftChanged(object sender,RoutedEventArgs e){if(!switching)drafts[active]=DraftBox.Text;}
    private async void DraftKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Handled||e.Key!=Windows.System.VirtualKey.Enter||App.Storage.IsPhone||DraftBox.IsComposing||Environment.TickCount64-DraftBox.CompositionEndedAt<100)return;
        bool Down(Windows.System.VirtualKey key)=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
        if(Down(Windows.System.VirtualKey.Shift)||Down(Windows.System.VirtualKey.Control)||Down(Windows.System.VirtualKey.Menu))return;
        e.Handled=true;if(e.KeyStatus.WasKeyDown||engine?.Jobs.TryGetValue(active,out var job)==true&&job.Busy)return;
        await SendAsync();
    }
    private async void SendClicked(object sender,RoutedEventArgs e){if(engine?.Jobs.TryGetValue(active,out var job)==true&&job.Busy){engine.Cancel(active);return;}await SendAsync();}
    private async Task SendAsync()
    {
        if(engine is null)return;var before=active;
        try{
            if(documentReads.Contains(before))throw new InvalidOperationException("文件正在读取，请稍候再发送");
            var message=Documents.Compose(DraftBox.Text,documents.GetValueOrDefault(before)??[]);
            var id=await engine.SendAsync(before,message,(attachments.GetValueOrDefault(before)??[]).ToArray(),SearchToggle.IsChecked==true,ThinkingToggle.IsChecked==true);
            drafts[before]="";attachments[before]=[];documents[before]=[];
            if(active==before){Select(id);DraftBox.Text="";}
        }catch(Exception error){ShowError(error.Message);if(engine.Settings.Provider().ApiKey.Length==0)await ShowSettingsAsync();}
    }
    private async Task RetryAsync(){if(engine is null)return;try{await engine.SendAsync(active,"",[],SearchToggle.IsChecked==true,ThinkingToggle.IsChecked==true,true);following=true;}catch(Exception error){ShowError(error.Message);}}
    private async void ModelChanged(object sender,SelectionChangedEventArgs e){if(switching||engine is null||ModelBox.SelectedIndex<0||ModelBox.SelectedIndex>=modelChoices.Count)return;try{var choice=modelChoices[ModelBox.SelectedIndex];await engine.SaveSettingsAsync(engine.Settings with{ActiveProviderId=choice.Provider,Model=choice.Model});}catch(Exception error){ShowError(error.Message);}}
    private void MenuClicked(object sender,RoutedEventArgs e)=>Shell.IsPaneOpen=!Shell.IsPaneOpen;
    private void PageSizeChanged(object sender,SizeChangedEventArgs e){Shell.DisplayMode=e.NewSize.Width<800?SplitViewDisplayMode.Overlay:SplitViewDisplayMode.Inline;if(e.NewSize.Width>=800)Shell.IsPaneOpen=true;WindowHeader.Visibility=App.Storage.IsPhone?Visibility.Collapsed:Visibility.Visible;Root.RowDefinitions[0].Height=App.Storage.IsPhone?new GridLength(0):new GridLength(44);MenuButton.Visibility=e.NewSize.Width<800?Visibility.Visible:Visibility.Collapsed;RecordButton.Visibility=e.NewSize.Width<600?Visibility.Collapsed:Visibility.Visible;ComposerArea.Margin=e.NewSize.Width<600?new Thickness(12,8,12,14):new Thickness(28,8,28,18);}
    private void OlderClicked(object sender,RoutedEventArgs e){page++;following=false;followId=null;ReplySpacer.Height=0;rowSignature="";Refresh();}
    private void NewerClicked(object sender,RoutedEventArgs e){page=Math.Max(0,page-1);following=false;followId=null;ReplySpacer.Height=0;rowSignature="";Refresh();}
    private void LatestClicked(object sender,RoutedEventArgs e)
    {
        page=0;following=true;scrollRequest++;ReplySpacer.Height=0;
        followId=engine?.Jobs.GetValueOrDefault(active)?.Live?.Id;Refresh();
        var target=followId??history.LastOrDefault()?.Id;if(target is not null)ScrollReplyOnce(target);
    }

    private async void DeleteChatClicked(object sender,RoutedEventArgs e){if(engine is null||sender is not MenuFlyoutItem {Tag:string id})return;var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="删除对话",Content="删除将同步到已配对设备。",PrimaryButtonText="删除",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;engine.Cancel(id);try{await engine.Store.DeleteChatAsync(id);if(id==active)Select("");}catch(Exception error){ShowError(error.Message);}}
    private async void PasteClicked(object sender,RoutedEventArgs e)=>await PasteIntoDraftAsync();
    private async void ImagesClicked(object sender,RoutedEventArgs e){var chat=active;try{await AddDraftFilesAsync(chat,await NativeActions.PickImagesAsync());}catch(Exception error){ShowError(error.Message);}}
    private async void AudioClicked(object sender,RoutedEventArgs e){if(engine is null)return;try{var file=await NativeActions.PickFileAsync(".wav",".mp3",".m4a",".ogg");if(file is null)return;var bytes=await NativeActions.FileBytesAsync(file,8*1024*1024);var text=await new MediaClient(()=>engine.Settings,!App.Storage.IsPhone).TranscribeAsync(bytes,file.ContentType.Length>0?file.ContentType:"audio/wav",CancellationToken.None);DraftBox.InsertText(text);}catch(Exception error){ShowError(error.Message);}}
    private async void RecordClicked(object sender,RoutedEventArgs e){if(recording is null){try{recording=await NativeActions.StartRecordingAsync();RecordButton.Content="停止录音";recordingTimer=new();_=StopRecordingLaterAsync(recordingTimer.Token);}catch(Exception error){ShowError("无法录音："+error.Message);}}else await StopRecordingAsync();}
    private async Task StopRecordingLaterAsync(CancellationToken token){try{await Task.Delay(TimeSpan.FromSeconds(119),token);await StopRecordingAsync();}catch(OperationCanceledException){}}
    private async Task StopRecordingAsync(){if(recording is null||engine is null)return;var current=recording;recording=null;recordingTimer?.Cancel();RecordButton.Content="语音输入";try{var bytes=await current.StopAsync();var text=await new MediaClient(()=>engine.Settings,!App.Storage.IsPhone).TranscribeAsync(bytes,"audio/wav",CancellationToken.None);DraftBox.InsertText(text);}catch(Exception error){ShowError(error.Message);}finally{await current.DisposeAsync();}}
    private async Task ReadSpeechAsync(string text){if(engine is null)return;if(speech is not null){speech.Cancel();speech=null;return;}using var stop=new CancellationTokenSource();speech=stop;try{foreach(var part in ChatAIBox.Edge.Speech.Chunks(text)){stop.Token.ThrowIfCancellationRequested();var bytes=await ChatAIBox.Edge.Speech.SynthesizeAsync(part,engine.Settings.TtsVoice,engine.Settings.TtsRate,NetworkPolicy.WebProxy(NetworkPolicy.Proxy(engine.Settings,!App.Storage.IsPhone)),stop.Token);await NativeActions.PlayAudioAsync(bytes,stop.Token);}}catch(OperationCanceledException){}finally{if(ReferenceEquals(speech,stop))speech=null;}}
    public async Task<bool> RequestCloseAsync(){if(engine is null)return true;if(engine.Sync.Pending||engine.Jobs.Values.Any(j=>j.Busy)){var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="还有消息尚未保存到手机",Content="电脑只保留内存副本。退出会停止生成并丢弃未同步消息。",PrimaryButtonText="返回并同步",SecondaryButtonText="放弃并退出",CloseButtonText="返回",DefaultButton=ContentDialogButton.Close};var result=await dialog.ShowAsync();if(result!=ContentDialogResult.Secondary){if(result==ContentDialogResult.Primary)try{await engine.Sync.SynchronizeAsync();}catch(Exception error){ShowError(error.Message);}return false;}}speech?.Cancel();await engine.DisposeAsync();return true;}
    private async void ImportClicked(object sender,RoutedEventArgs e)=>await ShowImportAsync();
    private async void SettingsClicked(object sender,RoutedEventArgs e)=>await ShowSettingsAsync();
    private async void SyncClicked(object sender,RoutedEventArgs e)=>await ShowSyncAsync();
}
