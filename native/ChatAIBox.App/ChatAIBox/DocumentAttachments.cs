using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace ChatAIBox;

public sealed record ComposerAttachment(string Id,string Name,string Detail);

public sealed partial class MainPage
{
    private readonly Dictionary<string,List<DocumentAttachment>> documents=new();
    private readonly HashSet<string> documentReads=new();

    private void RefreshAttachments()
    {
        var items=new List<ComposerAttachment>();
        foreach(var image in attachments.GetValueOrDefault(active)??[])items.Add(new(image.Id,image.Name,"图片"));
        foreach(var document in documents.GetValueOrDefault(active)??[])items.Add(new(document.Id,document.Name,$"{document.Bytes/1024d:N0} KB · {document.Text.Length:N0} 字符"));
        AttachmentList.ItemsSource=items;AttachmentStatus.Text=documentReads.Contains(active)?"正在读取文件…":items.Count>0?"发送时将附件内容交给当前模型":"";AttachmentStatus.Visibility=AttachmentStatus.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }

    private async void FilesClicked(object sender,RoutedEventArgs e)
    {
        var chat=active;try{if(documentReads.Contains(chat))return;await AddDraftFilesAsync(chat,await NativeActions.PickDocumentsAsync());}catch(Exception error){ShowError(error.Message);}
    }

    private static bool IsImageFile(StorageFile file)=>file.FileType.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";
    private async Task AddDraftFilesAsync(string chat,IReadOnlyList<StorageFile> files)
    {
        if(files.Count==0)return;if(documentReads.Contains(chat))throw new InvalidOperationException("正在读取当前对话的附件，请稍候");
        var list=documents.GetValueOrDefault(chat)??[];var images=attachments.GetValueOrDefault(chat)??[];var incomingImages=files.Count(IsImageFile);
        if(list.Count+files.Count-incomingImages>Documents.MaxFiles)throw new InvalidOperationException("每条消息最多附加 6 个文档");
        if(images.Count+incomingImages>4)throw new InvalidOperationException("每条消息最多附加 4 张图片");
        documents[chat]=list;attachments[chat]=images;documentReads.Add(chat);if(active==chat)RefreshAttachments();var errors=new List<string>();
        try{foreach(var file in files){try{if(IsImageFile(file))images.Add(await NativeActions.ImageAsync(file));else{var bytes=await NativeActions.FileBytesAsync(file,Documents.MaxBytes);list.Add(await Task.Run(()=>Documents.Read(file.Name,bytes)));}if(active==chat)RefreshAttachments();}catch(Exception error){errors.Add(file.Name+"："+(error.Message=="文件过大"?"单个文件不能超过 20 MB":error.Message));}}}
        finally{documentReads.Remove(chat);if(active==chat)RefreshAttachments();}
        if(errors.Count>0)ShowError(string.Join("\n",errors));
    }

    private void RemoveAttachmentClicked(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:string id})return;
        attachments.GetValueOrDefault(active)?.RemoveAll(image=>image.Id==id);documents.GetValueOrDefault(active)?.RemoveAll(document=>document.Id==id);RefreshAttachments();
    }
}
