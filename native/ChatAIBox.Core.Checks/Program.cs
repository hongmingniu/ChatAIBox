using ChatAIBox.Core;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

static void Check(bool value,string name){if(!value)throw new Exception("FAILED: "+name);Console.WriteLine("PASS "+name);}
var key=new string('a',64);
var row=new ChatRecord{Id="sample",Type="message",Rev="0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa",ChatId="chat",Role="user",Content="你好🙂",Created=1};
if(args is ["--serve-relay",var relay,var access]){var store=new RecordStore();await using var connection=new RelayConnection(new Settings{RelayUrl=relay,RelayAccessKey=access,SyncKey=key},store,true,localTest:true);connection.Start();Console.WriteLine("READY");await Task.Delay(Timeout.Infinite);return;}
if(args is ["--tts",var audio]){var bytes=await ChatAIBox.Edge.Speech.SynthesizeAsync("ChatAIBox test.","zh-CN-XiaoxiaoNeural",0,new System.Net.WebProxy("http://127.0.0.1:7897"),CancellationToken.None);await File.WriteAllBytesAsync(audio,bytes);Console.WriteLine("PASS Edge TTS audio bytes: "+bytes.Length);return;}
if(args is ["--gec",var timestamp]){Console.WriteLine(ChatAIBox.Edge.Gec.Generate(long.Parse(timestamp)));return;}
if(args is ["--serve-direct",var port]){
    var store=new RecordStore();var server=new DirectSyncServer(int.Parse(port),key,store);server.Start();Console.WriteLine("READY");await Task.Delay(Timeout.Infinite);return;
}
if(args is ["--crypto",var input,var output]){
    var value=JsonNode.Parse(await File.ReadAllTextAsync(input))!.AsObject();var secret=value["key"]!.ToString();JsonNode result=value["operation"]!.ToString() switch{"seal"=>System.Text.Json.JsonSerializer.SerializeToNode(SyncCrypto.Seal(value["value"],secret),Json.Options)!,"open"=>SyncCrypto.Open<JsonNode>(Json.Read<Envelope>(value["value"]!.ToJsonString()),secret),"room"=>JsonValue.Create(SyncCrypto.Room(secret))!,_=>throw new Exception("Unknown operation")};await File.WriteAllTextAsync(output,result.ToJsonString());return;
}
Check(SyncCrypto.Room(key).Length==64,"HMAC route identifier");
var envelope=SyncCrypto.Seal(new SyncMessage("request","synthetic",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),[row]),key);
Check(SyncCrypto.Open<SyncMessage>(envelope,key).Records[0].Content==row.Content,"AES-GCM UTF8 roundtrip");
var rejected=false;try{SyncCrypto.Open<SyncMessage>(envelope,new string('b',64));}catch(CryptographicException){rejected=true;}Check(rejected,"wrong pairing key rejected");
var forbidden=Json.Read<ChatRecord>("{\"id\":\"x\",\"rev\":\"0000000000000001:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa\",\"type\":\"chat\",\"title\":\"test\",\"apiKey\":\"private-fixture\"}");Check(!Json.Write(forbidden).Contains("private-fixture"),"credentials excluded from records");
var saved=false;var store2=new RecordStore(persist:state=>{saved=true;return Task.CompletedTask;});await store2.MergeAsync([row]);Check(saved&&store2.Snapshot().Records.Length==1,"phone persistence before acknowledgement");
var failing=new RecordStore(persist:_=>throw new IOException("synthetic disk failure"));try{await failing.MergeAsync([row]);}catch(IOException){}Check(failing.Snapshot().Records.Length==0,"failed persistence does not commit in memory");
await store2.MergeAsync([row with{Rev="0000000000000002:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa",Deleted=true}]);await store2.MergeAsync([row]);Check(store2.Snapshot().Records[0].Deleted,"tombstones survive older replicas");
var stream=new ChatStream();var chunks="data: "+Json.Write(new JsonObject{{"choices",new JsonArray(new JsonObject{{"delta",new JsonObject{{"content","你好🙂"},{"reasoning_content","思考"}}}})}})+"\r\n\r\ndata: [DONE]\n\n";foreach(var character in chunks)stream.Feed(character.ToString());stream.Finish();Check(stream.Content=="你好🙂"&&stream.Reasoning=="思考","SSE arbitrary chunks and final marker");
var interrupted=new ChatStream();interrupted.Feed("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");rejected=false;try{interrupted.Finish();}catch(IOException){rejected=true;}Check(rejected&&interrupted.Content=="partial","incomplete SSE retains received output");
var prepared=await ContextBuilder.PrepareAsync([row],Limits.ContextBudget,true,true,null,(_,_,_)=>throw new Exception("Unexpected summary"),_=>Task.CompletedTask,CancellationToken.None);Check(prepared.Compressed==0,"small history does not compress at old thresholds");
Check(Limits.ChatOutput==24576&&Limits.SummaryOutput==81920&&Limits.ContextBudget==1019904,"24K reply, 80K summary and 1M total budget");
var large=Enumerable.Range(0,12).Select(i=>row with{Id="long-"+i,Rev=$"{i+1:D16}:aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa",Role=i%2==0?"user":"assistant",Content=new string('中',17000)}).ToArray();large[^1]=large[^1] with{Content="请继续"};var checkpoints=new List<ContextSummary>();
prepared=await ContextBuilder.PrepareAsync(large,12000,true,false,null,(_,_,_)=>Task.FromResult("已保留约束、关键事实与待办"),s=>{checkpoints.Add(s);return Task.CompletedTask;},CancellationToken.None,chunkBudget:512);
Check(checkpoints.Count>1&&prepared.Compressed>0&&large[0].Content!.Length==17000,"bounded compression checkpoints preserve originals");
Console.WriteLine("ALL_NATIVE_CORE_CHECKS_PASSED");
