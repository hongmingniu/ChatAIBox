#if ANDROID
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System.Buffers.Binary;
namespace ChatAIBox;
public static partial class NativeActions
{
    public static partial async Task<IRecording> StartRecordingAsync()
    {
        var activity=Droid.MainActivity.Current??throw new InvalidOperationException("请在应用前台录音");if(!await NativePermissions.RequestAsync(activity,Manifest.Permission.RecordAudio))throw new InvalidOperationException("麦克风权限未授予");return new AndroidRecording();
    }
    private sealed class AndroidRecording : IRecording
    {
        private readonly AudioRecord recorder;private readonly CancellationTokenSource stop=new();private readonly MemoryStream pcm=new();private readonly Task worker;private bool stopped;
        public AndroidRecording(){var size=Math.Max(4096,AudioRecord.GetMinBufferSize(16000,ChannelIn.Mono,Encoding.Pcm16bit));recorder=new AudioRecord(AudioSource.Mic,16000,ChannelIn.Mono,Encoding.Pcm16bit,size*2);if(recorder.State!=State.Initialized)throw new IOException("麦克风无法初始化");recorder.StartRecording();worker=Task.Run(()=>{var buffer=new byte[size];while(!stop.IsCancellationRequested){var count=recorder.Read(buffer,0,buffer.Length);if(count>0){if(pcm.Length+count>16000*2*120)break;pcm.Write(buffer,0,count);}}});}
        public async Task<byte[]> StopAsync(){if(stopped)return[];stopped=true;stop.Cancel();try{recorder.Stop();}catch{}await worker;recorder.Release();recorder.Dispose();var body=pcm.ToArray();var bytes=new byte[44+body.Length];System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes,0);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),bytes.Length-8);System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes,8);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16),16);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22),1);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24),16000);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28),32000);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34),16);System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes,36);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40),body.Length);body.CopyTo(bytes,44);return bytes;}
        public async ValueTask DisposeAsync(){if(!stopped)await StopAsync();pcm.Dispose();stop.Dispose();}
    }
    public static partial async Task PlayAudioAsync(byte[] mp3,CancellationToken token)
    {
        var folder=Path.Combine(App.Storage.Root,"temporary");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid()+".mp3");await File.WriteAllBytesAsync(path,mp3,token);using var player=new MediaPlayer();var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);player.Completion+=(_,_)=>done.TrySetResult();player.Error+=(_,e)=>{e.Handled=true;done.TrySetException(new IOException("音频播放失败"));};using var registration=token.Register(()=>done.TrySetCanceled(token));try{player.SetDataSource(path);player.Prepare();player.Start();await done.Task;}finally{try{player.Stop();}catch{}player.Release();File.Delete(path);}
    }
    public static partial void EnsureBackgroundSync(bool enabled)
    {
        var context=global::Android.App.Application.Context;var intent=new Intent(context,typeof(Droid.SyncService));if(!enabled){context.StopService(intent);return;}if(Build.VERSION.SdkInt>=BuildVersionCodes.O)context.StartForegroundService(intent);else context.StartService(intent);
    }
}
public static class NativePermissions
{
    private static TaskCompletionSource<bool>? pending;private const int RequestCode=7312;
    public static async Task<bool> RequestAsync(Activity activity,string permission){if(ContextCompat.CheckSelfPermission(activity,permission)==Permission.Granted)return true;if(pending is not null)throw new InvalidOperationException("正在等待系统权限");pending=new(TaskCreationOptions.RunContinuationsAsynchronously);try{ActivityCompat.RequestPermissions(activity,[permission],RequestCode);return await pending.Task;}finally{pending=null;}}
    public static void Complete(int code,Permission[] results){if(code==RequestCode)pending?.TrySetResult(results.Length>0&&results.All(p=>p==Permission.Granted));}
}
#endif
