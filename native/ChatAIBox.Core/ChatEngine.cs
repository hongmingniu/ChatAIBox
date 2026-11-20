using System.Collections.Concurrent;
using System.Text.Json.Nodes;
namespace ChatAIBox.Core;
public interface IAppStorage
{
    bool IsPhone{get;}
    Task<Settings?> ReadSettingsAsync();Task SaveSettingsAsync(Settings value);
    Task<AppState?> ReadStateAsync();Task SaveStateAsync(AppState state);
}
public sealed class GenerationJob(string chatId)
{
    public string ChatId{get;}=chatId;public CancellationTokenSource Cancellation{get;}=new();
    public ChatRecord? Live{get;internal set;}public string Phase{get;internal set;}="";public string ContextInfo{get;internal set;}="";public string Error{get;internal set;}="";public bool Busy{get;internal set;}=true;
}
public sealed class ChatEngine : IAsyncDisposable
{
    private readonly IAppStorage storage;private readonly SemaphoreSlim settingsGate=new(1,1);private readonly ConcurrentDictionary<string,ContextSummary> summaries=new();
    private readonly ConcurrentDictionary<string,(string Model,int Covered,string Hash,int Tokens)> metrics=new();private readonly ConcurrentDictionary<string,bool> fullContexts=new();
    public Settings Settings{get;private set;}public RecordStore Store{get;}public SyncCoordinator Sync{get;}public ModelClient Models{get;}
    public ConcurrentDictionary<string,GenerationJob> Jobs{get;}=new();public event Action? Changed;public event Action? SettingsChanged;
    private ChatEngine(IAppStorage storage,Settings settings,AppState? state)
    {
        this.storage=storage;Settings=settings;Store=new(state,storage.IsPhone?storage.SaveStateAsync:null);Sync=new(Store,()=>Settings,storage.IsPhone);Models=new(()=>Settings,!storage.IsPhone);Store.Changed+=Notify;Sync.Changed+=Notify;
    }
    public static async Task<ChatEngine> CreateAsync(IAppStorage storage)
    {
        var settings=await storage.ReadSettingsAsync().ConfigureAwait(false)??new Settings();NetworkPolicy.Validate(settings);
        var state=storage.IsPhone?await storage.ReadStateAsync().ConfigureAwait(false):null;
        var engine=new ChatEngine(storage,settings,state);await storage.SaveSettingsAsync(settings).ConfigureAwait(false);await engine.Sync.RestartAsync().ConfigureAwait(false);return engine;
    }
    private void Notify()=>Changed?.Invoke();
    public async Task SaveSettingsAsync(Settings next)
    {
        NetworkPolicy.Validate(next);await settingsGate.WaitAsync().ConfigureAwait(false);
        try{
            var old=Settings;var pairing=old.SyncKey!=next.SyncKey;
            if(!storage.IsPhone&&pairing&&Sync.Pending)throw new InvalidOperationException("请先将当前消息同步到手机，再更改配对密钥");
            var syncChanged=pairing||old.SyncMode!=next.SyncMode||old.SyncEnabled!=next.SyncEnabled||old.RelayUrl!=next.RelayUrl||old.RelayAccessKey!=next.RelayAccessKey||old.PeerAddress!=next.PeerAddress||old.Port!=next.Port||old.Proxy!=next.Proxy;
            await storage.SaveSettingsAsync(next).ConfigureAwait(false);
            if(syncChanged)await Sync.PauseAsync().ConfigureAwait(false);
            Settings=next;if(pairing&&!storage.IsPhone){await Store.ClearMirrorAsync().ConfigureAwait(false);Sync.ResetAcknowledgements();}
            if(syncChanged)await Sync.RestartAsync().ConfigureAwait(false);SettingsChanged?.Invoke();Notify();
        }finally{settingsGate.Release();}
    }
    public async Task<string> SendAsync(string? chatId,string text,ImageAttachment[] images,bool web=true,bool thinking=true,bool retry=false)
    {
        var settings=Settings;var provider=settings.Provider();if(provider.ApiKey.Length==0)throw new InvalidOperationException("请先在设置中填写当前提供方的 API Key");
        var id=string.IsNullOrEmpty(chatId)?Guid.NewGuid().ToString():chatId;var history=Store.Messages(id);var current=Store.Snapshot().Records.FirstOrDefault(r=>r.Id==id&&!r.Deleted);
        if(Jobs.TryGetValue(id,out var running)&&running.Busy)throw new InvalidOperationException("此对话正在生成");
        if(Jobs.Values.Count(j=>j.Busy)>=4)throw new InvalidOperationException("最多同时生成4个会话");
        var vision=provider.Vision&&(provider.Id!="deepseek"||settings.Model=="deepseek-flash");var search=web&&provider.Search&&(provider.Id!="deepseek"||settings.Model=="deepseek-flash");
        if(images.Length>0&&!vision)throw new InvalidOperationException("当前模型不支持图像，请切换到支持视觉的模型");
        if(!retry&&string.IsNullOrWhiteSpace(text)&&images.Length==0)throw new InvalidOperationException("请输入消息");
        var previous=retry?history.LastOrDefault():null;if(retry&&previous?.Role!="user")throw new InvalidOperationException("仅可重新请求最后一条用户消息");
        var created=Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),history.LastOrDefault()?.Created+1??0);
        var user=previous??new ChatRecord{Id=Guid.NewGuid().ToString(),Type="message",Rev=Store.NextRevision(),ChatId=id,Role="user",Content=text.Trim(),Images=images.Length>0?images:null,ProviderId=provider.Id,Model=settings.Model,Created=created};
        var job=new GenerationJob(id);Jobs[id]=job;
        try{if(!retry)await Store.MergeAsync((current is null?new[]{new ChatRecord{Id=id,Type="chat",Rev=Store.NextRevision(),Title=(text.Trim().Length>0?text.Trim():images.FirstOrDefault()?.Name??"图像对话")[..Math.Min(36,(text.Trim().Length>0?text.Trim():images.FirstOrDefault()?.Name??"图像对话").Length)],Created=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),ProviderId=provider.Id,Model=settings.Model}}:[]).Append(user)).ConfigureAwait(false);}
        catch{Jobs.TryRemove(id,out _);throw;}
        Notify();_=RunJobAsync(id,job,user,current,retry?history:history.Append(user).ToArray(),settings,provider,vision,search,thinking);return id;
    }
    private async Task RunJobAsync(string id,GenerationJob job,ChatRecord user,ChatRecord? current,ChatRecord[] history,Settings settings,Provider provider,bool vision,bool web,bool thinking)
    {
        var output=Math.Min(Limits.ChatOutput,Math.Min(provider.MaxOutputTokens,provider.ContextWindow/4));var budget=Math.Min(Limits.ContextBudget,provider.ContextWindow-output-Math.Min(4096,provider.ContextWindow/8));
        var answerId=Guid.NewGuid().ToString();var stream=new ChatStream();var complete=false;var keepLive=false;var throttled=0;
        ChatRecord Live()=>new(){Id=answerId,Type="message",Rev="",ChatId=id,Role="assistant",Content=stream.Content,Reasoning=stream.Reasoning,Sources=stream.Sources,Searches=stream.Searches,Created=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),ProviderId=provider.Id,Model=settings.Model};
        stream.Updated+=()=>{if(Interlocked.Exchange(ref throttled,1)==0)_=RefreshLiveAsync();};
        async Task RefreshLiveAsync(){await Task.Delay(60).ConfigureAwait(false);lock(job){if(!job.Busy){Interlocked.Exchange(ref throttled,0);return;}job.Live=Live();}job.Phase=stream.Content.Length>0?"":web?"正在检索与思考…":"正在思考…";Interlocked.Exchange(ref throttled,0);Notify();}
        void Phase(string text){job.Phase=text;Notify();}
        async Task<PreparedContext> Prepare(bool force=false)
        {
            int? estimate=null;if(metrics.TryGetValue(id,out var m)&&m.Model==settings.Model&&m.Covered<=history.Length&&ContextBuilder.PrefixHash(history,m.Covered)==m.Hash)estimate=m.Tokens+history.Skip(m.Covered).Sum(ContextBuilder.Cost);
            return await ContextBuilder.PrepareAsync(history,budget,settings.AutoCompress,vision,summaries.GetValueOrDefault(id)??current?.ContextSummary,
                (summary,batch,token)=>ContextBuilder.SummarizeAsync(Models,settings.Model,provider.Id,summary,batch,Math.Min(Limits.SummaryOutput,Math.Min(provider.MaxOutputTokens,budget/4)),token,Phase),
                async summary=>{var latest=Store.Snapshot().Records.FirstOrDefault(r=>r.Id==id);if(latest is null||latest.Deleted)throw new OperationCanceledException("对话已删除");await Store.MergeAsync([latest with{ContextSummary=summary,Rev=Store.NextRevision()}]).ConfigureAwait(false);summaries[id]=summary;},job.Cancellation.Token,
                p=>Phase($"正在压缩历史 {p.Covered}/{p.Total} 条"+(p.Offset>0?$"（当前消息 {p.Offset}/{p.TotalCharacters} 字符）":"")+"…"),force||fullContexts.ContainsKey(id),estimate,provider.Id=="deepseek"?24000:Math.Max(512,budget/8)).ConfigureAwait(false);
        }
        try{
            Phase("准备上下文…");var prepared=await Prepare().ConfigureAwait(false);
            for(var attempt=0;attempt<2;attempt++){
                job.ContextInfo=prepared.Compressed>0?$"已压缩 {prepared.Compressed} 条历史 · 原文保留":$"上下文约 {prepared.EstimatedTokens:N0} tokens";
                var messages=new JsonArray(new JsonObject{{"role","system"},{"content",ContextBuilder.SystemPrompt}});foreach(var message in prepared.Messages)messages.Add(message?.DeepClone());
                var body=new JsonObject{{"model",settings.Model},{"messages",messages},{"max_tokens",output},{"stream_options",new JsonObject{{"include_usage",true}}},{"thinking",new JsonObject{{"type",thinking&&(provider.Id=="deepseek"||provider.Thinking!="none")?"enabled":"disabled"}}}};
                if(web)body["tools"]=new JsonArray(new JsonObject{{"type","deepseek_web_search"}},new JsonObject{{"type","deepseek_read_url"}});
                Phase(web?"正在连接并准备检索…":"正在连接模型…");
                try{await Models.StreamAsync(body,provider.Id,stream,job.Cancellation.Token).ConfigureAwait(false);break;}
                catch(Exception e)when(attempt==0&&settings.AutoCompress&&stream.Content.Length==0&&stream.Reasoning.Length==0&&e.Message.Contains("CONTEXT_LIMIT:",StringComparison.Ordinal)){fullContexts[id]=true;prepared=await Prepare(true).ConfigureAwait(false);}
            }
            complete=true;if(stream.FinishReason=="length")job.Error="回复达到输出上限，已保留内容，可发送“继续”";
            if(stream.Content.Length==0&&stream.Reasoning.Length==0)throw new InvalidDataException("模型没有返回文字回复");
            if(!web&&prepared.Compressed==0&&stream.Usage?["prompt_tokens"]?.GetValue<int>() is >0 and var tokens)metrics[id]=(settings.Model,history.Length,ContextBuilder.PrefixHash(history,history.Length),tokens);else if(prepared.Compressed>0)metrics.TryRemove(id,out _);
        }catch(OperationCanceledException){job.Error="已停止，收到的内容已保留";}catch(Exception e){job.Error=e.Message;}
        finally{
            // A cancellation stops the request, not the durable save of received text.
            var chatExists=Store.Snapshot().Records.Any(r=>r.Id==id&&!r.Deleted&&r.Type=="chat");
            if(chatExists&&(stream.Content.Length>0||stream.Reasoning.Length>0)){var answer=Live() with{Rev=Store.NextRevision(),Created=Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Math.Max(user.Created+1,Store.Messages(id).LastOrDefault()?.Created+1??0)),Interrupted=!complete||stream.FinishReason=="length"};try{await Store.MergeAsync([answer]).ConfigureAwait(false);}catch(Exception e){lock(job){job.Busy=false;job.Live=answer;}job.Error="回复尚未保存，请复制或重试保存："+e.Message;keepLive=true;}}
            lock(job){job.Busy=false;if(!keepLive)job.Live=null;}job.Phase="";Notify();
        }
    }
    public void Cancel(string chatId){if(Jobs.TryGetValue(chatId,out var job))job.Cancellation.Cancel();}
    public async Task SaveRecoveredAsync(string chatId){if(Jobs.TryGetValue(chatId,out var job)&&!job.Busy&&job.Live is not null){await Store.MergeAsync([job.Live]).ConfigureAwait(false);job.Live=null;job.Error="";Notify();}}
    public async ValueTask DisposeAsync(){foreach(var job in Jobs.Values)job.Cancellation.Cancel();await Sync.DisposeAsync().ConfigureAwait(false);}
}
