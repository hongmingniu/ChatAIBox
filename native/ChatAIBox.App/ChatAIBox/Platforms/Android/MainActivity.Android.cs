using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace ChatAIBox.Droid;

[Activity(
    Name = "app.chataibox.mobile.MainActivity",
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden,
    Exported = true
)]
public class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    public static MainActivity? Current {get;private set;}
    public override void OnRequestPermissionsResult(int requestCode,string[] permissions,Permission[] grantResults){base.OnRequestPermissionsResult(requestCode,permissions,grantResults);NativePermissions.Complete(requestCode,grantResults);}
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        StartupCompatibility.Begin();
        Current=this;
        try{global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);}catch(Exception exception){StartupCompatibility.Record(exception);}
        try{
            base.OnCreate(savedInstanceState);
        }catch(Exception exception){
            StartupCompatibility.Record(exception);
            var panel=new LinearLayout(this){Orientation=Android.Widget.Orientation.Vertical};panel.SetPadding(32,48,32,24);
            var message=new TextView(this){Text="ChatAIBox 启动失败\n可以尝试软件渲染兼容模式。聊天和设置不会被清除。",TextSize=18};message.SetTextIsSelectable(true);panel.AddView(message);
            var retry=new Android.Widget.Button(this){Text="启用兼容模式并退出（重新打开生效）"};retry.Click+=(_,_)=>{StartupCompatibility.EnableSoftware();FinishAndRemoveTask();global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());};panel.AddView(retry);
            var copy=new Android.Widget.Button(this){Text="复制启动错误详情"};copy.Click+=(_,_)=>{var clipboard=(Android.Content.ClipboardManager)GetSystemService(ClipboardService)!;clipboard.PrimaryClip=Android.Content.ClipData.NewPlainText("ChatAIBox",StartupCompatibility.Report());Toast.MakeText(this,"已复制启动错误详情",ToastLength.Short)?.Show();};panel.AddView(copy);SetContentView(panel);
        }
    }

}
