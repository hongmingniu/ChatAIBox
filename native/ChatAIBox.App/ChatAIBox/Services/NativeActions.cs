using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
namespace ChatAIBox;
public static partial class NativeActions
{
    public static void Copy(string text){var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);}
    public static async Task<string> PasteAsync(){var data=Clipboard.GetContent();return data.Contains(StandardDataFormats.Text)?await data.GetTextAsync():"";}
    public static async Task<bool> SaveTextAsync(string name,string text)
    {
        var picker=new FileSavePicker{SuggestedFileName=Path.GetFileNameWithoutExtension(name)};var extension=Path.GetExtension(name);if(extension.Length==0)extension=".txt";picker.FileTypeChoices.Add("文件",new List<string>{extension});InitializePicker(picker);var file=await picker.PickSaveFileAsync();if(file is null)return false;await FileIO.WriteTextAsync(file,text);return true;
    }
    public static async Task<StorageFile?> PickFileAsync(params string[] types){var picker=new FileOpenPicker();foreach(var type in types)picker.FileTypeFilter.Add(type);InitializePicker(picker);return await picker.PickSingleFileAsync();}
    public static async Task<IReadOnlyList<StorageFile>> PickImagesAsync(){var picker=new FileOpenPicker();foreach(var type in new[]{".png",".jpg",".jpeg",".webp"})picker.FileTypeFilter.Add(type);InitializePicker(picker);return await picker.PickMultipleFilesAsync();}
    public static async Task<IReadOnlyList<StorageFile>> PickDocumentsAsync(){var picker=new FileOpenPicker();picker.FileTypeFilter.Add("*");InitializePicker(picker);return await picker.PickMultipleFilesAsync();}
    private static void InitializePicker(object picker){
#if WINDOWS
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
#endif
    }
    public static async Task OpenLinkAsync(string link){if(Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")await Windows.System.Launcher.LaunchUriAsync(uri);else throw new InvalidDataException("只支持 HTTP/HTTPS 链接");}
    public static async Task<byte[]> FileBytesAsync(StorageFile file,int limit){var props=await file.GetBasicPropertiesAsync();if(props.Size>(ulong)limit)throw new InvalidDataException("文件过大");using var input=await file.OpenStreamForReadAsync();using var result=new MemoryStream();var buffer=new byte[81920];int count;while((count=await input.ReadAsync(buffer))>0){if(result.Length+count>limit)throw new InvalidDataException("文件过大");await result.WriteAsync(buffer.AsMemory(0,count));}return result.ToArray();}
    public static Task<ImageAttachment> ImageAsync(StorageFile file)=>BitmapTools.ChatImageAsync(file);
    public static partial Task<IRecording> StartRecordingAsync();
    public static partial Task PlayAudioAsync(byte[] mp3,CancellationToken token);
    public static partial void EnsureBackgroundSync(bool enabled);
}
public interface IRecording : IAsyncDisposable{Task<byte[]> StopAsync();}
