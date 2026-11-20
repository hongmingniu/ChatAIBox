using ChatAIBox.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace ChatAIBox;
public sealed partial class MainPage
{
    private string lastBackground="";private long backgroundSequence;
    private async void ApplyBackground(Settings settings)
    {
        var dark=ActualTheme==ElementTheme.Dark;var tint=dark?Windows.UI.Color.FromArgb(255,37,35,33):Windows.UI.Color.FromArgb(255,245,246,245);
        var glass=false;
#if WINDOWS
        glass=NativeWindowAppearance.Apply(App.MainWindow!,settings,dark,BackdropHost);
#endif
        Root.Background=new SolidColorBrush(glass?Windows.UI.Color.FromArgb(0,0,0,0):tint);BackdropTint.Background=new SolidColorBrush(Windows.UI.Color.FromArgb(glass?(dark?(byte)92:(byte)204):(byte)0,tint.R,tint.G,tint.B));MainSurface.Background=new SolidColorBrush(Windows.UI.Color.FromArgb(glass?(dark?(byte)56:(byte)82):(byte)0,tint.R,tint.G,tint.B));Shell.PaneBackground=new SolidColorBrush(Windows.UI.Color.FromArgb(glass?(dark?(byte)76:(byte)112):(byte)180,tint.R,tint.G,tint.B));
        var identity=settings.BackgroundId+":"+settings.BackgroundBlur;if(identity==lastBackground){BackgroundImage.Opacity=settings.BackgroundOpacity/100d;return;}lastBackground=identity;var own=++backgroundSequence;BackgroundImage.Source=null;
        if(!Guid.TryParse(settings.BackgroundId,out _))return;var file=Path.Combine(App.Storage.Root,"backgrounds",settings.BackgroundId+".jpg");if(!File.Exists(file))return;
        try{var bytes=await File.ReadAllBytesAsync(file);var blurred=await BitmapTools.BlurAsync(bytes,Math.Clamp(settings.BackgroundBlur,0,30));if(own!=backgroundSequence)return;BackgroundImage.Source=await MarkdownView.ImageSourceAsync(blurred);BackgroundImage.Opacity=Math.Clamp(settings.BackgroundOpacity,0,100)/100d;}catch(Exception error){ShowError("背景加载失败："+error.Message);}
    }
}
