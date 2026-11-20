using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ChatAIBox;

public sealed partial class MainPage
{
    private void InitializeFileDrop()
    {
#if WINDOWS
        Root.AllowDrop=true;
        Root.AddHandler(UIElement.DragEnterEvent,new DragEventHandler(WindowFilesDragOver),true);
        Root.AddHandler(UIElement.DragOverEvent,new DragEventHandler(WindowFilesDragOver),true);
        Root.AddHandler(UIElement.DragLeaveEvent,new DragEventHandler(WindowFilesDragLeave),true);
        Root.AddHandler(UIElement.DropEvent,new DragEventHandler(WindowFilesDrop),true);
        Unloaded+=(_,_)=>ShowFileDropHint(false);
#endif
    }
    private void ShowFileDropHint(bool visible)=>FileDropHint.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
    private Task ReceiveRendererFilesAsync(IReadOnlyList<StorageFile> files)
    {
        var chat=active;ShowFileDropHint(false);return AddDraftFilesAsync(chat,files);
    }
#if WINDOWS
    private void WindowFilesDragOver(object sender,DragEventArgs args)
    {
        if(!args.DataView.Contains(StandardDataFormats.StorageItems))return;
        args.Handled=true;var allowed=engine is not null&&!documentReads.Contains(active);args.AcceptedOperation=allowed?DataPackageOperation.Copy:DataPackageOperation.None;
        args.DragUIOverride.Caption=allowed?"添加到当前对话":"请稍候，正在读取附件";args.DragUIOverride.IsCaptionVisible=true;ShowFileDropHint(allowed);
    }
    private void WindowFilesDragLeave(object sender,DragEventArgs args)
    {
        var point=args.GetPosition(Root);if(point.X<=0||point.Y<=0||point.X>=Root.ActualWidth||point.Y>=Root.ActualHeight)ShowFileDropHint(false);
    }
    private static bool FromRenderer(object? source)
    {
        for(var node=source as DependencyObject;node is not null;node=VisualTreeHelper.GetParent(node))if(node is HtmlMarkdownView)return true;return false;
    }
    private async void WindowFilesDrop(object sender,DragEventArgs args)
    {
        if(!args.DataView.Contains(StandardDataFormats.StorageItems))return;
        ShowFileDropHint(false);if(FromRenderer(args.OriginalSource))return;
        args.Handled=true;args.AcceptedOperation=DataPackageOperation.Copy;var chat=active;var deferral=args.GetDeferral();IReadOnlyList<IStorageItem> items;
        try{items=await args.DataView.GetStorageItemsAsync();}catch(Exception error){ShowError("无法读取拖入的文件："+error.Message);return;}finally{deferral.Complete();}
        try{var files=items.OfType<StorageFile>().ToArray();await AddDraftFilesAsync(chat,files);if(files.Length!=items.Count)ShowError("文件夹不作为附件添加，请拖入具体文件");}catch(Exception error){ShowError(error.Message);}
    }
#endif
}
