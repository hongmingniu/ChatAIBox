#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
namespace ChatAIBox.Droid;
[Service(Name="app.chataibox.mobile.SyncService",Exported=false,ForegroundServiceType=ForegroundService.TypeConnectedDevice)]
public sealed class SyncService : Service
{
    public override IBinder? OnBind(Intent? intent)=>null;
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        const string channel="chataibox-sync";var manager=(NotificationManager)GetSystemService(NotificationService)!;if(Build.VERSION.SdkInt>=BuildVersionCodes.O)manager.CreateNotificationChannel(new NotificationChannel(channel,"设备同步",NotificationImportance.Low));var open=new Intent(this,typeof(MainActivity));var pending=PendingIntent.GetActivity(this,0,open,PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);var notification=new NotificationCompat.Builder(this,channel).SetContentTitle("ChatAIBox 手机主机运行中").SetContentText("加密设备同步 · 聊天记录保存在手机").SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync).SetContentIntent(pending).SetOngoing(true).Build();if(Build.VERSION.SdkInt>=BuildVersionCodes.Q)StartForeground(39281,notification,ForegroundService.TypeConnectedDevice);else StartForeground(39281,notification);_ = InitializeAsync();return StartCommandResult.Sticky;
    }
    private async Task InitializeAsync(){try{var engine=await App.EngineAsync();if(!engine.Settings.SyncEnabled)StopSelf();}catch{StopSelf();}}
}
[BroadcastReceiver(Name="app.chataibox.mobile.BootReceiver",Enabled=true,Exported=false)]
[IntentFilter([Intent.ActionBootCompleted,Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context,Intent? intent){if(context is null)return;var pending=GoAsync();_=Task.Run(async()=>{try{var storage=new NativeStorage();var settings=await storage.ReadSettingsAsync();if(settings?.SyncEnabled==true)NativeActions.EnsureBackgroundSync(true);}catch{}finally{pending?.Finish();}});}
}
#endif
