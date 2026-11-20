using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
namespace ChatAIBox.Core;
public sealed class MediaClient(Func<Settings> getSettings,bool desktop)
{
    public async Task<string> TranscribeAsync(byte[] bytes,string mime,CancellationToken token)
    {
        if(bytes.Length is 0 or >8*1024*1024)throw new InvalidDataException("音频应小于 8 MiB");var settings=getSettings();var provider=settings.Provider();if(provider.ApiKey.Length==0)throw new InvalidDataException("请先填写语音服务 API Key");
        using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,desktop),TimeSpan.FromMinutes(3));using var request=new HttpRequestMessage(HttpMethod.Post,provider.Id=="deepseek"?"https://api.deepseek.com/v1/tools/asr":provider.BaseUrl.TrimEnd('/')+"/audio/transcriptions");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",provider.ApiKey);
        var form=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new MediaTypeHeaderValue(mime);form.Add(file,"file",mime=="audio/ogg"?"recording.ogg":"recording.wav");if(provider.Id!="deepseek")form.Add(new StringContent(provider.AsrModel.Length>0?provider.AsrModel:"whisper-1"),"model");request.Content=form;
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);if(!response.IsSuccessStatusCode)throw new IOException($"语音识别失败（HTTP {(int)response.StatusCode}）");var result=JsonNode.Parse(await ModelClient.ReadLimitedAsync(response,8*1024*1024,token).ConfigureAwait(false));return result?["text"]?.GetValue<string>()??throw new InvalidDataException("语音识别没有返回文字");
    }
    public async Task<(byte[] Bytes,string Mime)> FetchImageAsync(string address,CancellationToken token)
    {
        if(!Uri.TryCreate(address,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length>0||u.IsLoopback||System.Net.IPAddress.TryParse(u.Host,out _))throw new InvalidDataException("只支持普通 HTTPS 图片地址");
        var settings=getSettings();using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,desktop),TimeSpan.FromSeconds(30));using var response=await client.GetAsync(u,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);if(!response.IsSuccessStatusCode)throw new IOException("图片加载失败");var mime=response.Content.Headers.ContentType?.MediaType??"";if(mime is not("image/png" or "image/jpeg" or "image/webp" or "image/gif"))throw new InvalidDataException("仅支持 PNG/JPEG/WebP/GIF 图片");await using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var data=new MemoryStream();var bytes=new byte[32768];int count;while((count=await stream.ReadAsync(bytes,token).ConfigureAwait(false))>0){if(data.Length+count>8*1024*1024)throw new InvalidDataException("图片超过8MiB");data.Write(bytes,0,count);}return(data.ToArray(),mime);
    }
}
public static class Importer
{
    public static ChatRecord[] Backup(string text){if(Encoding.UTF8.GetByteCount(text)>64_000_000)throw new InvalidDataException("备份文件过大");var state=Json.Read<AppState>(text);if(state.Schema!=1||state.Records.Length>Limits.MaxRecords)throw new InvalidDataException("备份版本或大小不受支持");return state.Records.Select(RecordStore.Validate).ToArray();}
    public static (string Id,ChatRecord[] Records) Share(JsonObject payload,RecordStore store)
    {
        if(payload["code"] is not null&&payload["code"]!.ToString()!="0")throw new InvalidDataException("分享已失效或需要登录");var data=payload["data"]?["biz_data"]??payload["data"]??payload;
        if(payload["data"]?["biz_code"] is not null&&payload["data"]!["biz_code"]!.ToString()!="0")throw new InvalidDataException("分享无法读取");
        var rows=data["chat_messages"]??data["messages"]??data["share"]?["messages"];if(rows is not JsonArray messages)throw new InvalidDataException("无法识别分享内容，可使用JSON备份导入");
        var imported=new List<(string Role,string Content,string Reasoning)>();
        foreach(var message in messages){var role=message?["role"]?.ToString().ToLowerInvariant();if(role is not("user" or "assistant"))continue;var fragments=message?["fragments"] as JsonArray;var content=message?["content"] is JsonValue v&&v.TryGetValue<string>(out var s)?s:string.Join("\n",fragments?.Where(f=>f?["type"]?.ToString().ToUpperInvariant() is "RESPONSE" or "REQUEST" or "TEXT").Select(f=>f?["content"]?.ToString()??f?["text"]?.ToString()??"")??[]);var reasoning=message?["reasoning_content"]?.ToString()??string.Join("\n",fragments?.Where(f=>f?["type"]?.ToString().ToUpperInvariant()=="THINK").Select(f=>f?["content"]?.ToString()??"")??[]);if(content.Length>0)imported.Add((role,content,reasoning));}
        if(imported.Count==0)throw new InvalidDataException("分享中没有可导入的文字");if(imported.Count+1>Limits.MaxRecords)throw new InvalidDataException("分享消息过多");
        var id=Guid.NewGuid().ToString();var created=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-imported.Count;var title=data["chat_session"]?["title"]?.ToString()??data["title"]?.ToString()??imported[0].Content[..Math.Min(30,imported[0].Content.Length)];
        var records=new List<ChatRecord>{new(){Id=id,Type="chat",Rev=store.NextRevision(),Title=title,Created=created}};for(var i=0;i<imported.Count;i++){var m=imported[i];records.Add(new(){Id=Guid.NewGuid().ToString(),Type="message",Rev=store.NextRevision(),ChatId=id,Role=m.Role,Content=m.Content,Reasoning=m.Reasoning,Sources=[],Created=created+i});}
        return(id,records.Select(RecordStore.Validate).ToArray());
    }
}
