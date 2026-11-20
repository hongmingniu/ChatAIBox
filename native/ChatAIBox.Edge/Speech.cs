// SPDX-License-Identifier: LGPL-3.0-or-later
// Edge v2 transport and SSML framing. See Gec.cs and shared/THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ChatAIBox.Edge;
public static class Speech
{
    public static readonly IReadOnlyDictionary<string,string> Voices=new Dictionary<string,string>{{"zh-CN-XiaoxiaoNeural","晓晓 · 普通话女声"},{"zh-CN-YunxiNeural","云希 · 普通话男声"},{"zh-CN-XiaoyiNeural","晓伊 · 普通话女声"},{"zh-HK-HiuMaanNeural","晓曼 · 粤语"},{"zh-TW-HsiaoChenNeural","晓臻 · 台湾国语"},{"en-US-AriaNeural","Aria · English"},{"en-US-GuyNeural","Guy · English"}};
    public static string PlainText(string markdown)=>Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(markdown,@"```[\s\S]*?```","\n（代码块）\n"),@"!?\[([^\]]*)\]\([^)]*\)","$1"),"<[^>]+>",""),"[#*_>`~]","").Trim();
    public static IEnumerable<string> Chunks(string markdown){var text=PlainText(markdown);for(var offset=0;offset<text.Length;){var count=Math.Min(3500,text.Length-offset);if(offset+count<text.Length&&char.IsHighSurrogate(text[offset+count-1]))count--;yield return text.Substring(offset,count);offset+=count;}}
    public static (Uri Url,string[] Frames) Payload(string text,string voice,int rate,long? milliseconds=null)
    {
        if(!Voices.ContainsKey(voice)||string.IsNullOrWhiteSpace(text)||text.Length>4000)throw new InvalidDataException("Edge 朗读参数无效");
        var id=Guid.NewGuid().ToString("N");var date=DateTimeOffset.UtcNow.ToString("r");rate=Math.Clamp(rate,-50,100);
        var url=new Uri("wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1?ConnectionId="+id+"&Ocp-Apim-Subscription-Key=6A5AA1D4EAFF4E9FB37E23D68491D6F4&Sec-MS-GEC="+Gec.Generate(milliseconds)+"&Sec-MS-GEC-Version=2-143.0.3650.75");
        var config=JsonSerializer.Serialize(new{context=new{synthesis=new{audio=new{metadataoptions=new{sentenceBoundaryEnabled=false,wordBoundaryEnabled=false},outputFormat="audio-24khz-48kbitrate-mono-mp3"}}}});
        var clean=Regex.Replace(text,@"[\x00-\x08\x0b\x0c\x0e-\x1f]","");var ssml=$"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{voice[..5]}\"><voice name=\"{voice}\"><prosody rate=\"{(rate>=0?"+":"")}{rate}%\">{SecurityElement.Escape(clean)}</prosody></voice></speak>";
        return(url,[$"X-Timestamp:{date}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n{config}",$"X-RequestId:{id}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{date}\r\nPath:ssml\r\n\r\n{ssml}"]);
    }
    public static async Task<byte[]> SynthesizeAsync(string text,string voice,int rate,IWebProxy? proxy,CancellationToken token)
    {
        var payload=Payload(text,voice,rate);using var ws=new ClientWebSocket();ws.Options.Proxy=proxy;
        ws.Options.SetRequestHeader("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");ws.Options.SetRequestHeader("Origin","chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(90));token=timeout.Token;await ws.ConnectAsync(payload.Url,token).ConfigureAwait(false);
        foreach(var frame in payload.Frames)await ws.SendAsync(Encoding.UTF8.GetBytes(frame).AsMemory(),WebSocketMessageType.Text,true,token).ConfigureAwait(false);
        using var audio=new MemoryStream();var bytes=new byte[32768];
        while(true){using var frame=new MemoryStream();ValueWebSocketReceiveResult result;do{result=await ws.ReceiveAsync(bytes.AsMemory(),token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new IOException("Edge TTS 连接提前结束");if(frame.Length+result.Count>1024*1024)throw new IOException("Edge TTS 数据帧过大");frame.Write(bytes,0,result.Count);}while(!result.EndOfMessage);
            var data=frame.ToArray();if(result.MessageType==WebSocketMessageType.Binary){if(data.Length<2)continue;var header=BinaryPrimitives.ReadUInt16BigEndian(data);if(header+2>data.Length)throw new IOException("Edge TTS 音频帧无效");if(Encoding.UTF8.GetString(data,2,header).Contains("Path:audio",StringComparison.Ordinal)){audio.Write(data,header+2,data.Length-header-2);if(audio.Length>8*1024*1024)throw new IOException("Edge TTS 音频过大");}}
            else if(Encoding.UTF8.GetString(data).Contains("Path:turn.end",StringComparison.Ordinal)){if(audio.Length==0)throw new IOException("Edge TTS 未返回音频");return audio.ToArray();}
        }
    }
}
