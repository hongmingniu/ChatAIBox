using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
namespace ChatAIBox.Core;
public static class DirectSync
{
    public static async Task<ChatRecord[]> RequestAsync(Settings settings,ChatRecord[] records,string[][] known,CancellationToken token)
    {
        var id=Guid.NewGuid().ToString();var body=Json.Write(SyncCrypto.Seal(new SyncMessage("request",id,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),records,known),settings.SyncKey));
        using var client=NetworkPolicy.Client(NetworkPolicy.Proxy(settings,true),TimeSpan.FromSeconds(12));using var request=new HttpRequestMessage(HttpMethod.Post,NetworkPolicy.PeerUri(settings.PeerAddress,settings.Port)){Content=new StringContent(body,Encoding.UTF8,"application/json")};using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode)throw new IOException($"手机拒绝同步（{(int)response.StatusCode}），请检查密钥与系统时间");var raw=await ModelClient.ReadLimitedAsync(response,Limits.MaxEnvelopeBytes,token).ConfigureAwait(false);var value=SyncCrypto.Open<SyncMessage>(Json.Read<Envelope>(raw),settings.SyncKey);SyncCrypto.ValidateResponse(value,id);return value.Records;
    }
}
public sealed class DirectSyncServer(int port,string key,RecordStore store) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop=new();private readonly TcpListener listener=new(IPAddress.IPv6Any,port);private readonly SemaphoreSlim limit=new(4,4);private readonly Dictionary<string,long> seen=new();private Task? loop;
    public event Action? Saved;
    public void Start(){SyncCrypto.Key(key);listener.Server.DualMode=false;listener.Start(12);loop=AcceptAsync();}
    private async Task AcceptAsync(){while(!stop.IsCancellationRequested){TcpClient client;try{client=await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);}catch(OperationCanceledException){break;}catch(SocketException)when(stop.IsCancellationRequested){break;}if(!await limit.WaitAsync(0,stop.Token).ConfigureAwait(false)){client.Dispose();continue;}_=ServeAsync(client);}}
    private async Task ServeAsync(TcpClient client)
    {
        try{using(client){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(15));var token=timeout.Token;await using var stream=client.GetStream();var header=new List<byte>();var one=new byte[1];
            while(true){if(header.Count>=16384)throw new InvalidDataException("HTTP 头过大");if(await stream.ReadAsync(one,token).ConfigureAwait(false)==0)return;header.Add(one[0]);if(header.Count>=4&&header[^4]==13&&header[^3]==10&&header[^2]==13&&header[^1]==10)break;}
            var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");if(lines[0]!="POST /sync HTTP/1.1"){await ReplyAsync(stream,404,"Not found",token).ConfigureAwait(false);return;}
            var fields=lines.Skip(1).Where(l=>l.Contains(':')).Select(l=>l.Split(':',2)).ToArray();if(fields.Any(p=>p[0].Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("不支持分块请求");var lengths=fields.Where(p=>p[0].Equals("Content-Length",StringComparison.OrdinalIgnoreCase)).ToArray();if(lengths.Length!=1||!int.TryParse(lengths[0][1].Trim(),out var length)||length<=0||length>Limits.MaxEnvelopeBytes)throw new InvalidDataException("同步请求大小无效");
            var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token).ConfigureAwait(false);var value=SyncCrypto.Open<SyncMessage>(Json.Read<Envelope>(Encoding.UTF8.GetString(bytes)),key);var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock(seen){foreach(var old in seen.Where(p=>now-p.Value>300000).Select(p=>p.Key).ToArray())seen.Remove(old);if(value.Kind!="request"||value.Id.Length>80||Math.Abs(now-value.At)>300000||seen.Count>=10000||!seen.TryAdd(value.Id,now))throw new InvalidDataException("过期或重复的请求");}
            await store.MergeAsync(value.Records).ConfigureAwait(false);var known=SyncCrypto.Inventory(value.Known);var rows=store.Snapshot().Records.Where(r=>known is null||!known.TryGetValue(r.Id,out var rev)||rev!=r.Rev).ToArray();var reply=Json.Write(SyncCrypto.Seal(new SyncMessage("response",value.Id,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),rows),key));await ReplyAsync(stream,200,reply,token).ConfigureAwait(false);Saved?.Invoke();
        }}catch(Exception e)when(e is IOException or SocketException or InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or ArgumentException or OperationCanceledException){}
        finally{client.Dispose();limit.Release();}
    }
    private static async Task ReplyAsync(NetworkStream stream,int code,string text,CancellationToken token){var bytes=Encoding.UTF8.GetBytes(text);var head=Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code==200?"OK":"Not Found")}\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {bytes.Length}\r\n\r\n");await stream.WriteAsync(head,token).ConfigureAwait(false);await stream.WriteAsync(bytes,token).ConfigureAwait(false);}
    public async ValueTask DisposeAsync(){await stop.CancelAsync().ConfigureAwait(false);listener.Stop();if(loop is not null)try{await loop.ConfigureAwait(false);}catch(OperationCanceledException){}stop.Dispose();}
}
