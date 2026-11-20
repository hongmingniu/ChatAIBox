using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ChatAIBox.Core;
public sealed class ModelClient(Func<Settings> getSettings,bool desktop)
{
    public static JsonObject Adapt(JsonObject body,Provider provider,bool stream)
    {
        var next=(JsonObject)body.DeepClone();next["stream"]=stream;
        if(provider.Id!="deepseek"){
            next.Remove("stream_options");
            if(provider.Thinking=="effort"){next["reasoning_effort"]=next["thinking"]?["type"]?.ToString()=="enabled"?"high":"none";next.Remove("thinking");}
            else if(provider.Thinking!="deepseek")next.Remove("thinking");
            if(!provider.Search){next.Remove("tools");next.Remove("web_search_options");}
        }
        return next;
    }
    public async Task<JsonObject> CompleteAsync(JsonObject body,string? providerId,CancellationToken token)
    {
        var settings=getSettings();var provider=settings.Provider(providerId);
        using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,desktop));
        using var request=ChatRequest(provider,Adapt(body,provider,false));using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        await CheckAsync(response,token).ConfigureAwait(false);var text=await ReadLimitedAsync(response,16_000_000,token).ConfigureAwait(false);return JsonNode.Parse(text)?.AsObject()??throw new InvalidDataException("模型返回内容无效");
    }
    public async Task StreamAsync(JsonObject body,string? providerId,ChatStream stream,CancellationToken token)
    {
        var settings=getSettings();var provider=settings.Provider(providerId);
        using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,desktop));using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var request=ChatRequest(provider,Adapt(body,provider,true));using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
        await CheckAsync(response,timeout.Token).ConfigureAwait(false);
        if(response.Content.Headers.ContentType?.MediaType!="text/event-stream")throw new InvalidDataException("服务器没有返回 SSE 流式数据");
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);using var reader=new StreamReader(input,new UTF8Encoding(false,true));var chars=new char[8192];var size=0;
        while(true){var count=await reader.ReadAsync(chars.AsMemory(),timeout.Token).ConfigureAwait(false);if(count==0)break;size+=count;if(size>16_000_000)throw new InvalidDataException("模型返回内容过大");stream.Feed(new string(chars,0,count));}
        stream.Finish();
    }
    private static HttpRequestMessage ChatRequest(Provider provider,JsonObject body)
    {
        if(string.IsNullOrEmpty(provider.ApiKey))throw new InvalidDataException("请先填写当前提供方的 API Key");
        var request=new HttpRequestMessage(HttpMethod.Post,provider.BaseUrl.TrimEnd('/')+"/chat/completions"){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",provider.ApiKey);return request;
    }
    internal static async Task CheckAsync(HttpResponseMessage response,CancellationToken token)
    {
        if(response.IsSuccessStatusCode)return;
        if(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge){var text=await ReadLimitedAsync(response,65536,token).ConfigureAwait(false);if(Regex.IsMatch(text,"context_length|context.*(length|window|limit)|maximum context",RegexOptions.IgnoreCase))throw new InvalidDataException("CONTEXT_LIMIT: 模型上下文已达到上限");}
        throw new HttpRequestException(response.StatusCode switch{HttpStatusCode.Unauthorized=>"API Key 无效",HttpStatusCode.PaymentRequired=>"模型服务余额不足",HttpStatusCode.TooManyRequests=>"请求过于频繁，请稍后再试",_=>$"请求失败（HTTP {(int)response.StatusCode}）"},null,response.StatusCode);
    }
    internal static async Task<string> ReadLimitedAsync(HttpResponseMessage response,int limit,CancellationToken token)
    {
        await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();var bytes=new byte[32768];int count;
        while((count=await input.ReadAsync(bytes,token).ConfigureAwait(false))>0){if(output.Length+count>limit)throw new InvalidDataException("返回内容过大");output.Write(bytes,0,count);}return Encoding.UTF8.GetString(output.ToArray());
    }
    public async Task<JsonObject> ShareAsync(string link,CancellationToken token)
    {
        if(!Uri.TryCreate(link.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="chat.deepseek.com"||!uri.IsDefaultPort||uri.UserInfo.Length>0||!Regex.IsMatch(uri.AbsolutePath,@"^/share/[a-zA-Z0-9_-]{5,128}/?$"))throw new InvalidDataException("仅支持官方 DeepSeek HTTPS 分享链接");
        var id=uri.AbsolutePath.TrimEnd('/').Split('/')[^1];var settings=getSettings();using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,desktop));
        using var response=await client.GetAsync("https://chat.deepseek.com/api/v0/share/content?share_id="+Uri.EscapeDataString(id),HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);await CheckAsync(response,token).ConfigureAwait(false);
        return JsonNode.Parse(await ReadLimitedAsync(response,16_000_000,token).ConfigureAwait(false))?.AsObject()??throw new InvalidDataException("分享数据无效");
    }
}
