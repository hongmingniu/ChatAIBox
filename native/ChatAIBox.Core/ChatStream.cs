using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ChatAIBox.Core;
public sealed partial class ChatStream
{
    private readonly object sync=new();private string buffer="";private readonly StringBuilder content=new(),reasoning=new();
    public string Content{get{lock(sync)return content.ToString();}}public string Reasoning{get{lock(sync)return reasoning.ToString();}}
    public Source[] Sources{get;private set;}=[];private readonly List<SearchTrace> searches=[];public SearchTrace[] Searches{get{lock(sync)return searches.ToArray();}}
    public string? FinishReason{get;private set;}public JsonObject? Usage{get;private set;}public bool Done{get;private set;}
    public event Action? Updated;
    [GeneratedRegex(@"\r?\n\r?\n",RegexOptions.CultureInvariant)]private static partial Regex Separator();
    public void Feed(string text){lock(sync)FeedLocked(text);}
    private void FeedLocked(string text)
    {
        buffer+=text;if(buffer.Length>4_000_000)throw new InvalidDataException("流式数据帧过大");
        while(true){var match=Separator().Match(buffer);if(!match.Success)break;var frame=buffer[..match.Index];buffer=buffer[(match.Index+match.Length)..];ParseEvent(frame);}
    }
    private void ParseEvent(string frame)
    {
        var data=string.Join("\n",frame.Split('\n').Select(l=>l.TrimEnd('\r')).Where(l=>l.StartsWith("data:",StringComparison.Ordinal)).Select(l=>l[5..].StartsWith(' ')?l[6..]:l[5..]));
        if(data.Length==0)return;if(data=="[DONE]"){Done=true;return;}if(Done)return;
        var value=JsonNode.Parse(data)?.AsObject()??throw new InvalidDataException("流式数据无效");
        if(value["error"] is JsonObject error){var message=error["message"]?.ToString()??"流式请求失败";if(Regex.IsMatch(error["code"]+" "+message,"context_length|context.*(window|limit)|maximum context",RegexOptions.IgnoreCase))message="CONTEXT_LIMIT: 模型上下文已达到上限";throw new InvalidDataException(message);}
        if(value["usage"] is JsonObject usage)Usage=(JsonObject)usage.DeepClone();
        if(value["choices"] is not JsonArray {Count:>0} choices||choices[0] is not JsonObject choice)return;
        var delta=choice["delta"]?.AsObject();content.Append(delta?["content"]?.GetValue<string>()??"");reasoning.Append(delta?["reasoning_content"]?.GetValue<string>()??"");
        if(content.Length+reasoning.Length>2_000_000)throw new InvalidDataException("回复超出保存容量");
        if(delta?["annotations"] is JsonArray annotations)Sources=annotations.Select(a=>a?["url_citation"]??a).Where(a=>Uri.TryCreate(a?["url"]?.ToString(),UriKind.Absolute,out var u)&&u.Scheme is "https" or "http").Select(a=>new Source(a?["title"]?.ToString()??a!["url"]!.ToString(),a!["url"]!.ToString())).ToArray();
        var tool=choice["server_tool_event"];
        if(tool?["call"] is JsonObject call)SearchCall(call,tool["type"]?.ToString()=="call_completed"?"completed":"searching",tool["step"]?.ToString()??"0");
        if(delta?["server_tool_calls"] is JsonArray steps)foreach(var step in steps)if(step?["calls"] is JsonArray calls)foreach(var c in calls.OfType<JsonObject>())SearchCall(c,c["status"]?.ToString()??"completed",step["index"]?.ToString()??"0");
        if(choice["finish_reason"] is JsonValue finish)FinishReason=finish.ToString();Updated?.Invoke();
    }
    private void SearchCall(JsonObject call,string status,string step)
    {
        var action=call["action"]??call["input"];var id=call["id"]?.ToString()??step+":"+(call["index"]?.ToString()??"0");var old=searches.FirstOrDefault(s=>s.Id==id);
        var queries=(action?["queries"] as JsonArray)?.Select(q=>q?.ToString()??"").ToArray()??[];
        var row=new SearchTrace(id,call["kind"]?.ToString()??action?["type"]?.ToString()??"search",queries.Length>0?queries:old?.Queries??[],call["status"]?.ToString()??status,action?["url"]?.ToString()??old?.Url);
        var index=searches.FindIndex(s=>s.Id==id);if(index>=0)searches[index]=row;else searches.Add(row);
    }
    public void Finish(){lock(sync)FinishLocked();}
    private void FinishLocked(){if(!string.IsNullOrWhiteSpace(buffer))ParseEvent(buffer);buffer="";if(!Done)throw new IOException("流式连接提前中断，已保留收到的内容");}
}
