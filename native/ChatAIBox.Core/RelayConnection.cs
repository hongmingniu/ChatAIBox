using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace ChatAIBox.Core;
public sealed record ConnectionStatus(bool Connected,bool PhoneOnline,string Error="");
public sealed class RelayConnection : IAsyncDisposable
{
    private readonly Settings settings;private readonly RecordStore store;private readonly bool phone;private readonly bool localTest;private readonly string proxy;
    private readonly CancellationTokenSource stop=new();private readonly SemaphoreSlim sendGate=new(1,1),phoneGate=new(1,1);
    private readonly ConcurrentDictionary<string,TaskCompletionSource<ChatRecord[]>> pending=new();
    private readonly Dictionary<string,Partial> parts=new();private readonly Dictionary<string,long> seen=new();
    private ClientWebSocket? socket;private Task? runner;private volatile bool joined,phoneOnline;
    private sealed class Partial{public StringBuilder Text{get;}=new();public long At{get;}=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}
    public event Action<ConnectionStatus>? StatusChanged;public event Action? PhoneSaved;
    public bool Connected=>joined;public bool PhoneOnline=>phoneOnline;
    public RelayConnection(Settings settings,RecordStore store,bool phone,string proxy="",bool localTest=false){this.settings=settings;this.store=store;this.phone=phone;this.proxy=proxy;this.localTest=localTest;SyncCrypto.Key(settings.SyncKey);NetworkPolicy.RelayUri(settings.RelayUrl,localTest);}
    public void Start(){runner??=RunAsync();}
    private async Task RunAsync()
    {
        while(!stop.IsCancellationRequested){
            using var ws=new ClientWebSocket();socket=ws;ws.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);ws.Options.KeepAliveTimeout=TimeSpan.FromSeconds(20);
            ws.Options.Proxy=NetworkPolicy.WebProxy(proxy);
            if(settings.RelayAccessKey.Length>0)ws.Options.SetRequestHeader("Authorization","Bearer "+settings.RelayAccessKey);
            var error="转发连接断开，正在自动重连";
            try{
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await ws.ConnectAsync(NetworkPolicy.RelayUri(settings.RelayUrl,localTest),timeout.Token).ConfigureAwait(false);
                await SendAsync(new{type="join",role=phone?"phone":"desktop",room=SyncCrypto.Room(settings.SyncKey)},stop.Token).ConfigureAwait(false);
                var buffer=new byte[32768];
                while(ws.State==WebSocketState.Open&&!stop.IsCancellationRequested){
                    using var frame=new MemoryStream();ValueWebSocketReceiveResult result;
                    do{result=await ws.ReceiveAsync(buffer.AsMemory(),stop.Token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new IOException("转发连接已关闭");if(result.MessageType!=WebSocketMessageType.Text||frame.Length+result.Count>96*1024)throw new InvalidDataException("转发数据帧无效");frame.Write(buffer,0,result.Count);}while(!result.EndOfMessage);
                    await ReceiveAsync(JsonNode.Parse(frame.ToArray())!.AsObject()).ConfigureAwait(false);
                }
            }catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            catch(Exception e){if(e.Message.Contains("401",StringComparison.Ordinal))error="服务器访问密钥无效或未填写";}
            finally{joined=false;phoneOnline=false;lock(parts)parts.Clear();foreach(var task in pending.Values)task.TrySetException(new IOException("转发连接断开，未确认消息仍在电脑内存"));pending.Clear();StatusChanged?.Invoke(new(false,false,stop.IsCancellationRequested?"":error));}
            if(!stop.IsCancellationRequested)try{await Task.Delay(3000,stop.Token).ConfigureAwait(false);}catch(OperationCanceledException){break;}
        }
    }
    private async Task ReceiveAsync(JsonObject msg)
    {
        var type=msg["type"]?.GetValue<string>();
        if(type is "joined" or "presence"){joined=true;phoneOnline=phone||msg["phoneOnline"]?.GetValue<bool>()==true;StatusChanged?.Invoke(new(true,phoneOnline));return;}
        var id=msg["id"]?.GetValue<string>()??"";
        if(type=="unavailable"){if(pending.TryRemove(id,out var task))task.TrySetException(new IOException("手机未在线，消息仍在电脑内存"));return;}
        if(type!="data")return;
        if(!Guid.TryParseExact(id,"D",out _))throw new InvalidDataException("同步请求 ID 无效");
        var from=phone?msg["from"]?.GetValue<string>()??"":"";
        if(phone&&!Guid.TryParseExact(from,"D",out _))throw new InvalidDataException("同步来源无效");
        if(!phone&&!pending.ContainsKey(id))return;
        var data=msg["data"]?.GetValue<string>()??throw new InvalidDataException("同步分片无效");if(data.Length>48000)throw new InvalidDataException("同步分片过大");
        string? completed=null;var key=from+id;
        lock(parts){
            foreach(var old in parts.Where(p=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-p.Value.At>180000).Select(p=>p.Key).ToArray())parts.Remove(old);
            if(!parts.TryGetValue(key,out var part)){if(parts.Count>=4)throw new InvalidDataException("同步请求过多");parts[key]=part=new();}
            part.Text.Append(data);if(part.Text.Length>Limits.MaxEnvelopeBytes){parts.Remove(key);throw new InvalidDataException("同步响应过大");}
            if(msg["end"]?.GetValue<bool>()==true){completed=part.Text.ToString();parts.Remove(key);}
        }
        if(completed is null)return;
        if(phone){await HandlePhoneAsync(from,id,completed).ConfigureAwait(false);return;}
        try{var value=SyncCrypto.Open<SyncMessage>(Json.Read<Envelope>(completed),settings.SyncKey);SyncCrypto.ValidateResponse(value,id);if(pending.TryRemove(id,out var result))result.TrySetResult(value.Records);}
        catch(Exception e){if(pending.TryRemove(id,out var result))result.TrySetException(e);}
    }
    private async Task HandlePhoneAsync(string from,string id,string text)
    {
        await phoneGate.WaitAsync(stop.Token).ConfigureAwait(false);
        try{
            var value=SyncCrypto.Open<SyncMessage>(Json.Read<Envelope>(text),settings.SyncKey);var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach(var old in seen.Where(p=>now-p.Value>300000).Select(p=>p.Key).ToArray())seen.Remove(old);
            if(value.Kind!="request"||value.Id!=id||Math.Abs(now-value.At)>300000||seen.ContainsKey(id)||seen.Count>=10000)throw new InvalidDataException("过期或重复的同步请求");seen[id]=now;
            await store.MergeAsync(value.Records).ConfigureAwait(false);
            var known=SyncCrypto.Inventory(value.Known);
            var rows=store.Snapshot().Records.Where(r=>known is null||!known.TryGetValue(r.Id,out var rev)||rev!=r.Rev).ToArray();
            await SendBoxAsync(id,Json.Write(SyncCrypto.Seal(new SyncMessage("response",id,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),rows),settings.SyncKey)),from,stop.Token).ConfigureAwait(false);
            PhoneSaved?.Invoke();
        }catch(Exception e)when(e is not OperationCanceledException){StatusChanged?.Invoke(new(true,true,"同步请求校验或保存失败，请检查配对密钥与手机存储"));}
        finally{phoneGate.Release();}
    }
    public async Task<ChatRecord[]> RequestAsync(ChatRecord[] records,string[][] known,CancellationToken token)
    {
        using var wait=CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);wait.CancelAfter(TimeSpan.FromSeconds(18));
        try{while(!joined)await Task.Delay(80,wait.Token).ConfigureAwait(false);}catch(OperationCanceledException)when(!token.IsCancellationRequested&&!stop.IsCancellationRequested){throw new IOException("无法连接转发服务，请检查地址、访问密钥与代理");}
        if(!phoneOnline)throw new IOException("手机尚未在线，消息仍在电脑内存");
        var id=Guid.NewGuid().ToString();var result=new TaskCompletionSource<ChatRecord[]>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=result;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try{await SendBoxAsync(id,Json.Write(SyncCrypto.Seal(new SyncMessage("request",id,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),records,known),settings.SyncKey)),null,timeout.Token).ConfigureAwait(false);return await result.Task.WaitAsync(timeout.Token).ConfigureAwait(false);}
        finally{pending.TryRemove(id,out _);}
    }
    private async Task SendBoxAsync(string id,string text,string? to,CancellationToken token){for(var offset=0;offset<text.Length;offset+=48000){var msg=new JsonObject{{"type","data"},{"id",id},{"data",text.Substring(offset,Math.Min(48000,text.Length-offset))},{"end",offset+48000>=text.Length}};if(to is not null)msg["to"]=to;await SendAsync(msg,token).ConfigureAwait(false);}}
    private async Task SendAsync<T>(T value,CancellationToken token){await sendGate.WaitAsync(token).ConfigureAwait(false);try{var ws=socket;if(ws?.State!=WebSocketState.Open)throw new IOException("转发连接已断开");await ws.SendAsync(Encoding.UTF8.GetBytes(Json.Write(value)).AsMemory(),WebSocketMessageType.Text,true,token).ConfigureAwait(false);}finally{sendGate.Release();}}
    public async ValueTask DisposeAsync(){await stop.CancelAsync().ConfigureAwait(false);socket?.Abort();if(runner is not null)try{await runner.ConfigureAwait(false);}catch(OperationCanceledException){}stop.Dispose();}
}
