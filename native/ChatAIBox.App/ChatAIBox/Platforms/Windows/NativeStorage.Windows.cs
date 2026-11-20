#if WINDOWS
using ChatAIBox.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace ChatAIBox;
public sealed partial class NativeStorage
{
    private partial string GetRoot()=>ProfileOverride is {Length:>0} profile?Path.GetFullPath(profile):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ChatAIBox.Native");
    public partial async Task<Settings?> ReadSettingsAsync()
    {
        var file=Path.Combine(Root,"settings.bin");if(File.Exists(file))return Json.Read<Settings>(Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(file),null,DataProtectionScope.CurrentUser)));
        if(ProfileOverride is not null)return null;
        foreach(var name in new[]{"ChatAIBox","chatai-box"}){
            var legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),name);var settings=Path.Combine(legacy,"settings.bin");if(!File.Exists(settings))continue;
            var bytes=await File.ReadAllBytesAsync(settings);return Json.Read<Settings>(DecryptLegacy(bytes,Path.Combine(legacy,"Local State")));
        }
        return null;
    }
    internal static string DecryptLegacy(byte[] bytes,string localState)
    {
        if(bytes.Length>31&&(Encoding.ASCII.GetString(bytes,0,3) is "v10" or "v11")){
            var state=JsonNode.Parse(File.ReadAllText(localState));var wrapped=Convert.FromBase64String(state?["os_crypt"]?["encrypted_key"]?.GetValue<string>()??throw new InvalidDataException("旧版凭据密钥缺失"));
            if(wrapped.Length<=5||Encoding.ASCII.GetString(wrapped,0,5)!="DPAPI")throw new InvalidDataException("旧版凭据加密格式不受支持");
            var key=ProtectedData.Unprotect(wrapped.AsSpan(5).ToArray(),null,DataProtectionScope.CurrentUser);var plain=new byte[bytes.Length-31];using var aes=new AesGcm(key,16);aes.Decrypt(bytes.AsSpan(3,12),bytes.AsSpan(15,plain.Length),bytes.AsSpan(bytes.Length-16),plain);CryptographicOperations.ZeroMemory(key);return Encoding.UTF8.GetString(plain);
        }
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser));
    }
    public partial async Task SaveSettingsAsync(Settings value){var encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Write(value)),null,DataProtectionScope.CurrentUser);var file=Path.Combine(Root,"settings.bin");await File.WriteAllBytesAsync(file+".tmp",encrypted);File.Move(file+".tmp",file,true);}
    public partial Task<AppState?> ReadStateAsync()=>Task.FromResult<AppState?>(null);
    public partial Task SaveStateAsync(AppState state)=>throw new InvalidOperationException("Windows 聊天仅保存在内存，不写入永久聊天库");
}
#endif
