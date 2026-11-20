using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
namespace ChatAIBox;
public partial class App : Application
{
    private static readonly object engineGate=new();private static Task<ChatEngine>? engineTask;
    public static Window? MainWindow{get;private set;}public static NativeStorage Storage{get;}=new();
    public static Task<ChatEngine> EngineAsync(){lock(engineGate)return engineTask??=ChatEngine.CreateAsync(Storage);}
    public static bool CloseApproved{get;set;}
    public App(){InitializeComponent();UnhandledException+=(_,e)=>{if(MainWindow?.Content is MainPage page){page.ShowError(e.Exception.Message);e.Handled=true;}};}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow=new Window{Title="ChatAIBox"};var page=new MainPage();MainWindow.Content=page;
#if WINDOWS
        var window=MainWindow.AppWindow;window.Resize(new Windows.Graphics.SizeInt32(1250,850));
        if(window.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.SetBorderAndTitleBar(false,false);
        MainWindow.ExtendsContentIntoTitleBar=true;MainWindow.SetTitleBar(page.DragRegion);
        window.Closing+=async(_,e)=>{if(CloseApproved)return;e.Cancel=true;if(await page.RequestCloseAsync()){CloseApproved=true;MainWindow.Close();}};
#endif
        MainWindow.Activate();
#if WINDOWS
        MainWindow.AppWindow.Show();
#endif
    }
    public static void InitializeLogging(){var factory=LoggerFactory.Create(builder=>{builder.SetMinimumLevel(LogLevel.Warning);});global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory=factory;}
}
