using System.Text.Json.Nodes;
namespace ChatAIBox.Core;
public sealed class SyncCoordinator(RecordStore store,Func<Settings> getSettings,bool phone) : IAsyncDisposable
{
    private RelayConnection? relay;private DirectSyncServer? server;private CancellationTokenSource? runStop;private Task? loop;private readonly SemaphoreSlim syncGate=new(1,1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> acknowledged=new(StringComparer.Ordinal);private bool merging;
    public ConnectionStatus Connection{get;private set;}=new(false,false);
    public long LastSync{get;private set;}public string Error{get;private set;}="";
    public bool Pending=>!phone&&store.Snapshot().Records.Any(r=>!acknowledged.TryGetValue(r.Id,out var rev)||rev!=r.Rev);
    public event Action? Changed;
    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);var settings=getSettings();Error="";
        if(!settings.SyncEnabled){Changed?.Invoke();return;}
        NetworkPolicy.Validate(settings);runStop=new();
        if(settings.SyncMode=="relay"){
            relay=new(settings,store,phone,NetworkPolicy.Proxy(settings,!phone));
            relay.StatusChanged+=s=>{Connection=s;Error=s.Error;Changed?.Invoke();if(!phone&&s.PhoneOnline)_=SynchronizeSafeAsync();};
            relay.PhoneSaved+=()=>{LastSync=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();Error="";Changed?.Invoke();};relay.Start();
        }else if(phone){
            server=new(settings.Port,settings.SyncKey,store);server.Saved+=()=>{LastSync=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();Changed?.Invoke();};server.Start();Connection=new(true,true);
        }
        store.Changed-=OnRecordsChanged;store.Changed+=OnRecordsChanged;
        if(!phone)loop=PeriodicAsync(runStop.Token);Changed?.Invoke();
    }
    private void OnRecordsChanged(){Changed?.Invoke();if(!phone&&!merging&&getSettings().SyncEnabled)_=SynchronizeSafeAsync();}
    private async Task PeriodicAsync(CancellationToken token){using var timer=new PeriodicTimer(TimeSpan.FromSeconds(20));await SynchronizeSafeAsync().ConfigureAwait(false);try{while(await timer.WaitForNextTickAsync(token).ConfigureAwait(false))await SynchronizeSafeAsync().ConfigureAwait(false);}catch(OperationCanceledException){}}
    private async Task SynchronizeSafeAsync(){try{await SynchronizeAsync().ConfigureAwait(false);}catch(Exception e){Error=e.Message;Changed?.Invoke();}}
    public async Task SynchronizeAsync()
    {
        if(phone||!getSettings().SyncEnabled||!await syncGate.WaitAsync(0).ConfigureAwait(false))return;
        try{
            var settings=getSettings();var snapshot=store.Snapshot().Records;var sent=snapshot.Where(r=>!acknowledged.TryGetValue(r.Id,out var rev)||rev!=r.Rev).ToArray();var known=snapshot.Select(r=>new[]{r.Id,r.Rev}).ToArray();var token=runStop?.Token??CancellationToken.None;
            var response=settings.SyncMode=="relay"?await (relay??throw new IOException("转发连接尚未启动")).RequestAsync(sent,known,token).ConfigureAwait(false):await DirectSync.RequestAsync(settings,sent,known,token).ConfigureAwait(false);
            merging=true;try{await store.MergeAsync(response).ConfigureAwait(false);}finally{merging=false;}
            foreach(var row in sent.Concat(response))acknowledged[row.Id]=row.Rev;
            LastSync=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();Error="";
        }catch(Exception e){Error=e.Message;throw;}
        finally{syncGate.Release();Changed?.Invoke();}
    }
    public Task PauseAsync()=>StopAsync();
    public void ResetAcknowledgements()=>acknowledged.Clear();
    private async Task StopAsync(){if(runStop is not null)await runStop.CancelAsync().ConfigureAwait(false);if(relay is not null){await relay.DisposeAsync().ConfigureAwait(false);relay=null;}if(server is not null){await server.DisposeAsync().ConfigureAwait(false);server=null;}if(loop is not null){try{await loop.ConfigureAwait(false);}catch(OperationCanceledException){}loop=null;}runStop?.Dispose();runStop=null;Connection=new(false,false);}
    public async ValueTask DisposeAsync(){store.Changed-=OnRecordsChanged;await StopAsync().ConfigureAwait(false);}
}
