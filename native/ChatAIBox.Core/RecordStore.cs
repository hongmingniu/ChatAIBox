using System.Text.RegularExpressions;
namespace ChatAIBox.Core;

public sealed partial class RecordStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, ChatRecord> records = new(StringComparer.Ordinal);
    private readonly Func<AppState, Task>? persist;
    private long clock;
    public string DeviceId { get; }
    public long Version { get; private set; }
    public event Action? Changed;
    public RecordStore(AppState? initial = null, Func<AppState, Task>? persist = null)
    {
        DeviceId = initial?.DeviceId ?? Guid.NewGuid().ToString(); this.persist = persist;
        if (initial is not null && initial.Schema != 1) throw new InvalidDataException("备份版本不支持");
        foreach (var row in initial?.Records ?? []) { var clean = Validate(row); records[clean.Id] = clean; Observe(clean.Rev); }
        if(records.Count > Limits.MaxRecords)throw new InvalidDataException("记录超过同步容量");
    }
    [GeneratedRegex("^[0-9]{16}:[a-f0-9-]{36}$", RegexOptions.CultureInvariant)] private static partial Regex RevisionPattern();
    [GeneratedRegex("^[a-f0-9]{1,8}$", RegexOptions.CultureInvariant)] private static partial Regex HashPattern();
    [GeneratedRegex("^data:image/(png|jpeg|gif|webp);base64,[a-zA-Z0-9+/=]+$", RegexOptions.CultureInvariant)] private static partial Regex ImagePattern();
    public string NextRevision() { lock(records) { clock = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), clock + 1); return $"{clock:D16}:{DeviceId}"; } }
    private void Observe(string rev) { if(long.TryParse(rev.AsSpan(0,16),out var next))clock=Math.Max(clock,next); }
    public AppState Snapshot() { lock(records) return new(1,DeviceId,records.Values.OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray()); }
    public ChatRecord[] Chats(string search = "") => Snapshot().Records.Where(r=>r.Type=="chat"&&!r.Deleted&&(r.Title??"").Contains(search,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Rev,StringComparer.Ordinal).ToArray();
    public ChatRecord[] Messages(string id) => Snapshot().Records.Where(r=>r.Type=="message"&&!r.Deleted&&r.ChatId==id).OrderBy(r=>r.Created).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray();
    public async Task MergeAsync(IEnumerable<ChatRecord> incoming)
    {
        var validated=incoming.Select(Validate).ToArray();if(validated.Length>Limits.MaxRecords)throw new InvalidDataException("同步记录过多");
        await gate.WaitAsync().ConfigureAwait(false);
        var changed=false;
        try{
            AppState? next=null;
            lock(records){
                var candidate=new Dictionary<string,ChatRecord>(records,StringComparer.Ordinal);
                foreach(var row in validated){
                    if(!candidate.TryGetValue(row.Id,out var old)||string.CompareOrdinal(row.Rev,old.Rev)>0||(row.Rev==old.Rev&&string.CompareOrdinal(Canonical(row),Canonical(old))>0)){candidate[row.Id]=row;changed=true;}
                }
                if(candidate.Count>Limits.MaxRecords)throw new InvalidDataException("记录超过同步容量，请先导出备份");
                if(changed)next=new(1,DeviceId,candidate.Values.OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray());
            }
            // Commit durable phone storage before acknowledging replication.
            if(next is not null){if(persist is not null)await persist(next).ConfigureAwait(false);lock(records){records.Clear();foreach(var row in next.Records){records[row.Id]=row;Observe(row.Rev);}Version++;}}
        }finally{gate.Release();}
        if(changed)Changed?.Invoke();
    }
    public async Task ClearMirrorAsync(){if(persist is not null)throw new InvalidOperationException("不能清空手机持久库");await gate.WaitAsync().ConfigureAwait(false);try{lock(records){records.Clear();Version++;}}finally{gate.Release();}Changed?.Invoke();}
    public Task DeleteChatAsync(string id) => MergeAsync(Snapshot().Records.Where(r=>r.Id==id||r.ChatId==id).Select(r=>r with{Deleted=true,Rev=NextRevision()}));
    public static ChatRecord Validate(ChatRecord row)
    {
        if(row.Id is null||row.Id.Length>160||row.Rev is null||!RevisionPattern().IsMatch(row.Rev)||row.Type is not ("chat" or "message"))throw new InvalidDataException("无效的同步记录");
        var summary=row.ContextSummary;var p=summary?.Partial;
        var partial=p is not null&&p.Id.Length<=160&&RevisionPattern().IsMatch(p.Rev)&&p.Offset is >0 and <=2_000_000;
        var validSummary=summary is not null&&summary.Text.Length<=Limits.SummaryCharacters&&summary.Covered is >=0 and <=Limits.MaxRecords&&(summary.Covered>0||partial)&&(p is null||partial)&&HashPattern().IsMatch(summary.PrefixHash);
        var baseRow=new ChatRecord{Id=row.Id,Rev=row.Rev,Type=row.Type,Deleted=row.Deleted,Created=row.Created,ProviderId=row.ProviderId is {Length:<=160}?row.ProviderId:null,Model=row.Model is {Length:<=160}?row.Model:null};
        if(row.Type=="chat")return baseRow with{Title=Cut(row.Title??"新对话",300),ContextSummary=validSummary?summary:null};
        if(row.ChatId is null||row.Role is not("user" or "assistant")||row.Content is null||row.Content.Length>2_000_000)throw new InvalidDataException("无效的消息");
        var images=row.Images;
        if(images is {Length:>4})throw new InvalidDataException("每条消息最多4张图片");
        if(images?.Any(i=>i is null||i.DataUrl is null||i.DataUrl.Length>1_500_000||!ImagePattern().IsMatch(i.DataUrl))==true)throw new InvalidDataException("图片格式或大小无效");
        images=images?.Select(i=>i with{Id=Cut(i.Id??"",160),Name=Cut(i.Name??"图片",256),Mime=Cut(i.Mime??"image/jpeg",80),Width=i.Width??0,Height=i.Height??0}).ToArray();
        return baseRow with{ChatId=row.ChatId,Role=row.Role,Content=row.Content,Reasoning=Cut(row.Reasoning??"",2_000_000),Sources=(row.Sources??[]).Where(s=>Uri.TryCreate(s.Url,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https").Take(100).Select(s=>new Source(Cut(s.Title,1000),Cut(s.Url,4000))).ToArray(),Images=images is {Length:>0}?images:null,Searches=row.Searches?.Take(100).Select(s=>s with{Queries=s.Queries.Take(20).Select(q=>Cut(q,1024)).ToArray()}).ToArray(),Interrupted=row.Interrupted==true?true:null};
    }
    private static string Cut(string value,int max)=>value.Length<=max?value:value[..max];
    private static string Canonical(ChatRecord r)
    {
        var o=new System.Text.Json.Nodes.JsonObject{{"id",r.Id},{"rev",r.Rev},{"type",r.Type},{"deleted",r.Deleted}};
        if(r.Type=="chat"){o["title"]=r.Title;o["created"]=r.Created;AddModel();if(r.ContextSummary is not null)o["contextSummary"]=System.Text.Json.JsonSerializer.SerializeToNode(r.ContextSummary,Json.Options);}
        else{o["chatId"]=r.ChatId;o["role"]=r.Role;o["content"]=r.Content;o["reasoning"]=r.Reasoning;o["created"]=r.Created;o["sources"]=System.Text.Json.JsonSerializer.SerializeToNode(r.Sources,Json.Options);AddModel();if(r.Images is not null)o["images"]=System.Text.Json.JsonSerializer.SerializeToNode(r.Images,Json.Options);if(r.Searches is not null)o["searches"]=System.Text.Json.JsonSerializer.SerializeToNode(r.Searches,Json.Options);if(r.Interrupted==true)o["interrupted"]=true;}
        return o.ToJsonString(new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        void AddModel(){if(r.ProviderId is not null)o["providerId"]=r.ProviderId;if(r.Model is not null)o["model"]=r.Model;}
    }
}
