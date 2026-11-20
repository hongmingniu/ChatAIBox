#if ANDROID
using Android.Security.Keystore;
using ChatAIBox.Core;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;
namespace ChatAIBox;
public sealed partial class NativeStorage
{
    private partial string GetRoot()=>global::Android.App.Application.Context.FilesDir!.AbsolutePath;
    private ISecretKey Secret(){var keys=KeyStore.GetInstance("AndroidKeyStore")!;keys.Load(null);if(!keys.ContainsAlias("chataibox.local")){var generator=KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes,"AndroidKeyStore")!;var options=new KeyGenParameterSpec.Builder("chataibox.local",KeyStorePurpose.Encrypt|KeyStorePurpose.Decrypt).SetBlockModes(KeyProperties.BlockModeGcm).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone).Build();generator.Init(options);generator.GenerateKey();}return (ISecretKey)keys.GetKey("chataibox.local",null)!;}
    private string? ReadProtected(string name){var atomic=new Android.Util.AtomicFile(new Java.IO.File(Root,name));if(!atomic.BaseFile!.Exists()&&!File.Exists(Path.Combine(Root,name)+".bak"))return null;var bytes=atomic.ReadFully()!;if(bytes.Length<28)throw new InvalidDataException("本机加密数据损坏，请保留文件后恢复备份");var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!;cipher.Init(CipherMode.DecryptMode,Secret(),new GCMParameterSpec(128,bytes[..12]));return Encoding.UTF8.GetString(cipher.DoFinal(bytes[12..])!);}
    private void WriteProtected(string name,string text){var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!;cipher.Init(CipherMode.EncryptMode,Secret());var encrypted=cipher.DoFinal(Encoding.UTF8.GetBytes(text))!;var atomic=new Android.Util.AtomicFile(new Java.IO.File(Root,name));System.IO.Stream? output=null;try{output=atomic.StartWrite();output!.Write(cipher.GetIV()!);output.Write(encrypted);atomic.FinishWrite(output);}catch{if(output is not null)atomic.FailWrite(output);throw;}}
    public partial Task<Settings?> ReadSettingsAsync()=>Task.FromResult(ReadProtected("settings.enc") is {} text?Json.Read<Settings>(text):null);
    public partial Task SaveSettingsAsync(Settings value){WriteProtected("settings.enc",Json.Write(value));return Task.CompletedTask;}
    public partial Task<AppState?> ReadStateAsync()=>Task.FromResult(ReadProtected("records.enc") is {} text?Json.Read<AppState>(text):null);
    public partial Task SaveStateAsync(AppState state){WriteProtected("records.enc",Json.Write(state));return Task.CompletedTask;}
}
#endif
