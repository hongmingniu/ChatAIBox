using ChatAIBox.Core;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System.Buffers.Binary;
using Windows.Storage;
namespace ChatAIBox;
public static class FontService
{
    public static FontFamily Default=>new("ms-appx:///Assets/fonts/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans SC");
    private static string Folder=>Path.Combine(
#if WINDOWS
        App.Storage.Root,
#else
        ApplicationData.Current.LocalFolder.Path,
#endif
        "fonts");
    public static async Task<(string Id,string Family)> ImportAsync(byte[] bytes)
    {
        using var data=SKData.CreateCopy(bytes);using var face=SKTypeface.FromData(data)??throw new InvalidDataException("字体文件无法读取");
        var sfnt=await Task.Run(()=>Sfnt(face));var id=Guid.NewGuid().ToString();Directory.CreateDirectory(Folder);await File.WriteAllBytesAsync(Path.Combine(Folder,id+".ttf"),sfnt);return(id,face.FamilyName);
    }
    public static FontFamily Resolve(Settings settings)
    {
        if(Guid.TryParse(settings.FontFileId,out _)){
            var path=Path.Combine(Folder,settings.FontFileId+".ttf");
            if(File.Exists(path)){
#if WINDOWS
                // WinUI's unpackaged font loader resolves application-relative font files.
                var assets=Path.Combine(AppContext.BaseDirectory,"Assets","user-fonts");Directory.CreateDirectory(assets);var copy=Path.Combine(assets,settings.FontFileId+".ttf");if(!File.Exists(copy))File.Copy(path,copy);return new FontFamily("ms-appx:///Assets/user-fonts/"+settings.FontFileId+".ttf#"+settings.FontFamily);
#else
                return new FontFamily("ms-appdata:///local/fonts/"+settings.FontFileId+".ttf#"+settings.FontFamily);
#endif
            }
        }
        return settings.FontFamily is "" or "HarmonyOS Sans SC"?Default:new FontFamily(settings.FontFamily);
    }
    private static byte[] Sfnt(SKTypeface face)
    {
        var tags=face.GetTableTags().Order().ToArray();if(tags.Length is 0 or >256)throw new InvalidDataException("不支持此字体容器");var tables=tags.Select(face.GetTableData).ToArray();var head=Array.IndexOf(tags,0x68656164u);if(head>=0&&tables[head].Length>=12)tables[head].AsSpan(8,4).Clear();
        var bytes=new byte[12+16*tags.Length+tables.Sum(t=>(t.Length+3)&~3)];var span=bytes.AsSpan();Write32(span,tags.Contains(0x43464620u)?0x4f54544fu:0x00010000u);Write16(span[4..],tags.Length);var power=(int)Math.Floor(Math.Log2(tags.Length));Write16(span[6..],16*(1<<power));Write16(span[8..],power);Write16(span[10..],16*tags.Length-16*(1<<power));var offset=12+16*tags.Length;var headOffset=0;
        for(var i=0;i<tags.Length;i++){var entry=span[(12+i*16)..];Write32(entry,tags[i]);Write32(entry[4..],Checksum(tables[i]));Write32(entry[8..],(uint)offset);Write32(entry[12..],(uint)tables[i].Length);tables[i].CopyTo(bytes,offset);if(i==head)headOffset=offset;offset+=(tables[i].Length+3)&~3;}
        if(headOffset>0)Write32(span[(headOffset+8)..],unchecked(0xb1b0afbAu-Checksum(bytes)));return bytes;
    }
    private static uint Checksum(ReadOnlySpan<byte> bytes){uint sum=0;for(var i=0;i<bytes.Length;i+=4){uint value=0;for(var n=0;n<4;n++)value=(value<<8)|(i+n<bytes.Length?bytes[i+n]:0u);sum=unchecked(sum+value);}return sum;}
    private static void Write16(Span<byte> span,int value)=>BinaryPrimitives.WriteUInt16BigEndian(span,(ushort)value);
    private static void Write32(Span<byte> span,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(span,value);
}
