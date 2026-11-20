#if WINDOWS
using ChatAIBox.Core;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
namespace ChatAIBox;
public static partial class NativeActions
{
    public static partial async Task<IRecording> StartRecordingAsync(){var capture=new MediaCapture();await capture.InitializeAsync(new MediaCaptureInitializationSettings{StreamingCaptureMode=StreamingCaptureMode.Audio});var path=Path.Combine(App.Storage.Root,"temporary");Directory.CreateDirectory(path);var folder=await StorageFolder.GetFolderFromPathAsync(path);var file=await folder.CreateFileAsync(Guid.NewGuid()+".wav");var profile=MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);profile.Audio.SampleRate=16000;profile.Audio.ChannelCount=1;profile.Audio.BitsPerSample=16;await capture.StartRecordToStorageFileAsync(profile,file);return new WindowsRecording(capture,file);}
    private sealed class WindowsRecording(MediaCapture capture,StorageFile file):IRecording
    {
        private bool stopped;
        public async Task<byte[]> StopAsync(){if(stopped)return[];stopped=true;await capture.StopRecordAsync();var bytes=await File.ReadAllBytesAsync(file.Path);await file.DeleteAsync();capture.Dispose();return bytes;}
        public async ValueTask DisposeAsync(){if(!stopped){stopped=true;try{await capture.StopRecordAsync();}finally{capture.Dispose();try{await file.DeleteAsync();}catch{}}}}
    }
    public static partial async Task PlayAudioAsync(byte[] mp3,CancellationToken token)
    {
        var folder=Path.Combine(App.Storage.Root,"temporary");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid()+".mp3");await File.WriteAllBytesAsync(path,mp3,token);using var player=new MediaPlayer();var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);player.MediaEnded+=(_,_)=>done.TrySetResult();player.MediaFailed+=(_,e)=>done.TrySetException(new IOException("音频播放失败"));using var registration=token.Register(()=>done.TrySetCanceled(token));try{player.Source=Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));player.Play();await done.Task;}finally{player.Pause();player.Source=null;File.Delete(path);}
    }
    public static partial void EnsureBackgroundSync(bool enabled){}
}
#endif
