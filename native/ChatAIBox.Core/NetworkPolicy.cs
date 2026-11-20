using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ChatAIBox.Core;
public static partial class NetworkPolicy
{
    [GeneratedRegex("^[A-Za-z0-9_-]{32,256}$",RegexOptions.CultureInvariant)] private static partial Regex AccessKeyPattern();
    public static Uri RelayUri(string text,bool localTest=false)
    {
        if(!Uri.TryCreate(text,UriKind.Absolute,out var u)||!(u.Scheme=="https"||(localTest&&u.Scheme=="http"&&u.IsLoopback))||!string.IsNullOrEmpty(u.UserInfo)||!string.IsNullOrEmpty(u.Query)||!string.IsNullOrEmpty(u.Fragment))throw new InvalidDataException("转发地址必须是 HTTPS，不含账号、参数或片段");
        return new Uri((u.Scheme=="https"?"wss":"ws")+"://"+u.Authority+u.AbsolutePath.TrimEnd('/')+"/relay");
    }
    public static Uri PeerUri(string address,int port)
    {
        if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetworkV6||address.Contains('%')||port is <1024 or >65535)throw new InvalidDataException("请输入完整 IPv6 地址和 1024–65535 端口");
        return new Uri($"http://[{ip}]:{port}/sync");
    }
    public static string Proxy(Settings settings,bool desktop)
    {
        var proxy=settings.Proxy;
        if(string.IsNullOrEmpty(proxy)&&desktop)proxy=Environment.GetEnvironmentVariable("HTTPS_PROXY")??Environment.GetEnvironmentVariable("HTTP_PROXY")??"";
        return proxy;
    }
    public static IWebProxy? WebProxy(string proxy)
    {
        if(string.IsNullOrEmpty(proxy))return null;
        if(!Uri.TryCreate(proxy,UriKind.Absolute,out var uri)||uri.Scheme!="http"||!uri.IsLoopback||!string.IsNullOrEmpty(uri.UserInfo)||uri.Port is <1 or >65535)throw new InvalidDataException("代理应为本机 HTTP 地址");
        return new WebProxy(uri){BypassProxyOnLocal=false,BypassList=[@"^https?://localhost(?::|/|$)",@"^https?://127\.0\.0\.1(?::|/|$)",@"^https?://\[::1\](?::|/|$)"]};
    }
    public static HttpClient Client(string proxy,TimeSpan? timeout=null)
    {
        var handler=new SocketsHttpHandler{UseProxy=!string.IsNullOrEmpty(proxy),Proxy=WebProxy(proxy),AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(15)};
        return new HttpClient(handler){Timeout=timeout??TimeSpan.FromMinutes(30)};
    }
    public static void Validate(Settings settings)
    {
        SyncCrypto.Key(settings.SyncKey);
        if(settings.Port is <1024 or >65535||settings.Model.Length is 0 or >160)throw new InvalidDataException("模型或同步端口无效");
        if(settings.SyncMode is not("relay" or "direct"))throw new InvalidDataException("同步方式无效");
        if(settings.RelayAccessKey.Length>0&&!AccessKeyPattern().IsMatch(settings.RelayAccessKey))throw new InvalidDataException("服务器访问密钥格式无效");
        if(settings.RelayUrl.Length>0||settings.SyncEnabled&&settings.SyncMode=="relay")RelayUri(settings.RelayUrl);
        if(settings.Proxy.Length>0)WebProxy(settings.Proxy);
        if(settings.Theme is not("system" or "light" or "dark")||settings.WindowBackdrop is not("system" or "acrylic" or "opaque"))throw new InvalidDataException("外观选项无效");
        var ids=new HashSet<string>();if(settings.Providers.Length>30)throw new InvalidDataException("提供方过多");
        foreach(var p in settings.Providers){
            if(!ids.Add(p.Id)||p.Id.Length is 0 or >160||p.Id=="deepseek")throw new InvalidDataException("提供方 ID 无效");
            if(!Uri.TryCreate(p.BaseUrl,UriKind.Absolute,out var u)||!(u.Scheme=="https"||u.Scheme=="http"&&u.IsLoopback)||u.UserInfo.Length>0||u.Query.Length>0||u.Fragment.Length>0)throw new InvalidDataException("提供方地址必须使用 HTTPS");
            if(p.Models.Length is 0 or >100||p.Models.Any(m=>string.IsNullOrWhiteSpace(m)||m.Length>160)||p.ContextWindow is <4096 or >2097152||p.MaxOutputTokens is <256 or >393216||p.Thinking is not("deepseek" or "effort" or "none"))throw new InvalidDataException("提供方模型配置无效");
        }
    }
}
