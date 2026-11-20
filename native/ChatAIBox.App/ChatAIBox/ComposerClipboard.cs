using ChatAIBox.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ChatAIBox;

public sealed partial class MainPage
{
    private bool pasting;
    private async Task PasteIntoDraftAsync()
    {
        if(pasting)return;
        pasting=true;var chat=active;
        try{
            if(documentReads.Contains(chat))throw new InvalidOperationException("附件正在读取，请稍候再粘贴");
            var data=Clipboard.GetContent();
            if(data.Contains(StandardDataFormats.StorageItems)){
                var items=await data.GetStorageItemsAsync();var files=items.OfType<StorageFile>().ToArray();
                await AddDraftFilesAsync(chat,files);
                if(files.Length!=items.Count)ShowError("文件夹不作为附件添加，请选择具体文件");
                return;
            }
            if(data.Contains(StandardDataFormats.Bitmap)){
                var images=attachments.GetValueOrDefault(chat)??[];
                if(images.Count>=4)throw new InvalidOperationException("每条消息最多附加 4 张图片");
                documentReads.Add(chat);if(active==chat)RefreshAttachments();
                try{
                    var bitmap=await data.GetBitmapAsync();using var stream=await bitmap.OpenReadAsync();
                    if(stream.Size>20*1024*1024)throw new InvalidDataException("剪贴板图片不能超过 20 MB");
                    using var input=stream.AsStreamForRead();using var bytes=new MemoryStream();var buffer=new byte[81920];int count;
                    while((count=await input.ReadAsync(buffer))>0){if(bytes.Length+count>20*1024*1024)throw new InvalidDataException("剪贴板图片不能超过 20 MB");await bytes.WriteAsync(buffer.AsMemory(0,count));}
                    images.Add(await BitmapTools.ChatImageAsync(bytes.ToArray(),"剪贴板图片.png"));attachments[chat]=images;
                }finally{documentReads.Remove(chat);if(active==chat)RefreshAttachments();}
                return;
            }
            if(data.Contains(StandardDataFormats.Text)){
                var text=await data.GetTextAsync();
                if(active==chat)DraftBox.InsertText(text);
                else drafts[chat]=(drafts.GetValueOrDefault(chat)??"")+text;
            }
        }catch(Exception error){ShowError("无法粘贴："+error.Message);}finally{pasting=false;}
    }
}
