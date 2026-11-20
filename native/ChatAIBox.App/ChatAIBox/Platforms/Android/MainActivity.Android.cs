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
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        Current=this;
        base.OnCreate(savedInstanceState);
    }

}
