using ChatAIBox.Core;
namespace ChatAIBox;
public sealed partial class NativeStorage : IAppStorage
{
    public static string? ProfileOverride=>Environment.GetEnvironmentVariable("CHATAIBOX_NATIVE_TEST_PROFILE")
#if DEBUG && WINDOWS
        ??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChatAIBox.Development")
#endif
        ;
    public string Root{get;}
    public bool IsPhone{
        get{
#if ANDROID
            return true;
#else
            return false;
#endif
        }
    }
    public NativeStorage(){Root=GetRoot();Directory.CreateDirectory(Root);}
    private partial string GetRoot();
    public partial Task<Settings?> ReadSettingsAsync();
    public partial Task SaveSettingsAsync(Settings value);
    public partial Task<AppState?> ReadStateAsync();
    public partial Task SaveStateAsync(AppState state);
}
