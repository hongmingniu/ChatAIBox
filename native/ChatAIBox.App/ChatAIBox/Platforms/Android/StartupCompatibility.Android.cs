#if ANDROID
using Android.App;
using Android.Content;
using Android.Runtime;

namespace ChatAIBox.Droid;

internal static class StartupCompatibility
{
    private static ISharedPreferences Preferences=>global::Android.App.Application.Context.GetSharedPreferences("startup-compatibility",FileCreationMode.Private)!;
    private static bool registered;
    public static bool Software=>Preferences.GetBoolean("software",false);
    public static void Begin()
    {
        var settings=Preferences;var software=settings.GetBoolean("software",false)||settings.GetBoolean("starting",false);
        settings.Edit()!.PutBoolean("starting",true)!.PutBoolean("software",software)!.Commit();
        if(registered)return;registered=true;
        AndroidEnvironment.UnhandledExceptionRaiser+=(_,args)=>Record(args.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>{if(args.ExceptionObject is Exception exception)Record(exception);};
    }
    public static async Task ConfirmReadyAsync()
    {
        await Task.Delay(3000);
        if(App.MainWindow?.Content is MainPage{IsLoaded:true})Preferences.Edit()!.PutBoolean("starting",false)!.Commit();
    }
    public static void EnableSoftware()=>Preferences.Edit()!.PutBoolean("software",true)!.PutBoolean("starting",false)!.Commit();
    public static void Record(Exception exception)
    {
        try{var lines=new List<string>{"ChatAIBox Android startup",Software?"Rendering: software":"Rendering: OpenGL"};
            for(Exception? current=exception;current is not null;current=current.InnerException){lines.Add(current.GetType().FullName??"Exception");lines.Add(current.StackTrace??"");}
            File.WriteAllText(Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath,"startup-failure.txt"),string.Join("\n",lines));
        }catch{}
    }
    public static string Report()
    {
        try{return File.ReadAllText(Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath,"startup-failure.txt"));}catch{return "ChatAIBox startup failure";}
    }
}
#endif
