using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ChatAIBox.Core;

public sealed record DocumentAttachment(string Id,string Name,string Text,long Bytes);

public static class Documents
{
    public const int MaxBytes=20*1024*1024,MaxCharacters=250_000,MaxFiles=6,MaxMessageCharacters=2_000_000;
    private const long MaxXmlBytes=16*1024*1024,MaxExpandedBytes=64*1024*1024;
    private static readonly HashSet<string> TextTypes=new(StringComparer.OrdinalIgnoreCase){".txt",".md",".markdown",".csv",".tsv",".json",".jsonl",".xml",".yaml",".yml",".toml",".ini",".cfg",".conf",".log",".html",".htm",".css",".scss",".less",".js",".mjs",".cjs",".ts",".tsx",".jsx",".vue",".svelte",".py",".pyi",".cs",".csproj",".sln",".c",".h",".cpp",".hpp",".java",".kt",".kts",".go",".rs",".swift",".dart",".sh",".bat",".cmd",".ps1",".sql",".r",".rb",".php",".tex",".ipynb",".properties",".env",".gitignore"};
    private static readonly XNamespace Word="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Sheet="http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static DocumentAttachment Read(string name,byte[] bytes)
    {
        if(bytes.Length==0)throw new InvalidDataException("文件为空");
        if(bytes.Length>MaxBytes)throw new InvalidDataException("单个文件不能超过 20 MB");
        name=Path.GetFileName(name.Replace('\\','/'));name=Regex.Replace(name,@"[\p{Cc}<>]"," ").Trim();if(name.Length==0)name="文件";if(name.Length>256)name=name[..256];
        var extension=Path.GetExtension(name).ToLowerInvariant();string text;
        switch(extension){
            case ".pdf":text=ReadPdf(bytes);break;
            case ".docx":text=ReadWord(bytes);break;
            case ".xlsx":text=ReadSpreadsheet(bytes);break;
            default:
                if(!TextTypes.Contains(extension)&&extension.Length!=0&&!name.StartsWith(".env.",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("暂不支持此格式，请选择文本、代码、PDF、DOCX 或 XLSX 文件");
                text=Decode(bytes);break;
        }
        text=text.Trim('\uFEFF','\0',' ','\t','\r','\n');CheckLength(text.Length);
        if(string.IsNullOrWhiteSpace(text))throw new InvalidDataException(extension==".pdf"?"PDF 没有可提取的文字，扫描件请先转文字或使用图片输入":"文件没有可读取的文字");
        return new(Guid.NewGuid().ToString(),name,text,bytes.LongLength);
    }

    public static string Compose(string prompt,IReadOnlyList<DocumentAttachment> files)
    {
        if(files.Count==0)return prompt;
        if(files.Count>MaxFiles)throw new InvalidDataException("每条消息最多附加 6 个文件");
        var result=new StringBuilder(string.IsNullOrWhiteSpace(prompt)?"文件："+string.Join("、",files.Select(f=>f.Name)):prompt.Trim());
        foreach(var file in files){
            CheckLength(file.Text.Length);var ticks=MaxRun(file.Text,'`');var tildes=MaxRun(file.Text,'~');var fence=new string(ticks<=tildes?'`':'~',Math.Max(3,Math.Min(ticks,tildes)+1));
            result.Append("\n\n---\n\n附件资料：").Append(file.Name).Append("\n\n").Append(fence).Append("text\n").Append(file.Text).Append('\n').Append(fence);
            if(result.Length>MaxMessageCharacters)throw new InvalidDataException("文件内容与消息合计超过 200 万字符，请拆分发送");
        }
        return result.ToString();
    }
    private static int MaxRun(string text,char marker){var max=0;var run=0;foreach(var c in text){run=c==marker?run+1:0;max=Math.Max(max,run);}return max;}
    private static void CheckLength(int length){if(length>MaxCharacters)throw new InvalidDataException("单个文件提取文字超过 25 万字符，请拆分文件；内容未被截断");}

    private static string Decode(byte[] bytes)
    {
        string text;
        if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe,0,0}))text=new UTF32Encoding(false,true,true).GetString(bytes,4,bytes.Length-4);
        else if(bytes.AsSpan().StartsWith(new byte[]{0,0,0xfe,0xff}))text=new UTF32Encoding(true,true,true).GetString(bytes,4,bytes.Length-4);
        else if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe}))text=new UnicodeEncoding(false,true,true).GetString(bytes,2,bytes.Length-2);
        else if(bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff}))text=new UnicodeEncoding(true,true,true).GetString(bytes,2,bytes.Length-2);
        else{
            try{text=new UTF8Encoding(false,true).GetString(bytes);}
            catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(54936,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(bytes);}
        }
        if(text.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t' or '\f')))throw new InvalidDataException("此文件不是可读取的文本，请转换为支持的文档格式");
        CheckLength(text.Length);return text;
    }

    private static string ReadPdf(byte[] bytes)
    {
        try{
            using var document=PdfDocument.Open(bytes);if(document.NumberOfPages>500)throw new InvalidDataException("PDF 超过 500 页，请拆分后上传");var result=new StringBuilder();
            foreach(var page in document.GetPages()){result.AppendLine("[第 "+page.Number+" 页]");var text=ContentOrderTextExtractor.GetText(page);if(!string.IsNullOrWhiteSpace(text))result.AppendLine(text);CheckLength(result.Length);}
            var content=result.ToString();return Regex.Replace(content,@"\[第 \d+ 页\]\s*","").Trim().Length==0?"":content;
        }catch(InvalidDataException){throw;}catch(Exception error)when(error is not OutOfMemoryException){throw new InvalidDataException("无法读取 PDF，请确认文件完整且没有密码保护",error);}
    }

    private static XDocument? Xml(ZipArchive archive,string name,ref long expanded)
    {
        var entry=archive.GetEntry(name);if(entry is null)return null;
        if(entry.Length>MaxXmlBytes||(expanded+=entry.Length)>MaxExpandedBytes)throw new InvalidDataException("文档解压后的内容过大，请拆分文件");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxXmlBytes});return XDocument.Load(reader);
    }
    private static string ReadWord(byte[] bytes)
    {
        using var memory=new MemoryStream(bytes,false);using var archive=new ZipArchive(memory,ZipArchiveMode.Read);long expanded=0;var main=Xml(archive,"word/document.xml",ref expanded)??throw new InvalidDataException("DOCX 文件缺少正文");var result=new StringBuilder();
        void Add(XDocument document){foreach(var paragraph in document.Descendants(Word+"p")){foreach(var item in paragraph.Descendants().Where(n=>n.Ancestors(Word+"p").FirstOrDefault()==paragraph)){if(item.Name==Word+"t")result.Append(item.Value);else if(item.Name==Word+"tab")result.Append('\t');else if(item.Name==Word+"br"||item.Name==Word+"cr")result.AppendLine();CheckLength(result.Length);}result.AppendLine();CheckLength(result.Length);}}
        Add(main);foreach(var part in new[]{"word/footnotes.xml","word/endnotes.xml"})if(Xml(archive,part,ref expanded) is {} notes){result.AppendLine("[附注]");Add(notes);}return result.ToString();
    }
    private static string ReadSpreadsheet(byte[] bytes)
    {
        using var memory=new MemoryStream(bytes,false);using var archive=new ZipArchive(memory,ZipArchiveMode.Read);long expanded=0;
        var shared=Xml(archive,"xl/sharedStrings.xml",ref expanded)?.Descendants(Sheet+"si").Select(si=>string.Concat(si.Descendants(Sheet+"t").Where(t=>!t.Ancestors(Sheet+"rPh").Any()).Select(t=>t.Value))).ToArray()??[];
        var workbook=Xml(archive,"xl/workbook.xml",ref expanded)??throw new InvalidDataException("XLSX 文件缺少工作簿");var relationships=Xml(archive,"xl/_rels/workbook.xml.rels",ref expanded);XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";var result=new StringBuilder();
        foreach(var sheet in workbook.Descendants(Sheet+"sheet")){
            var id=(string?)sheet.Attribute(rel+"id");var relationship=relationships?.Root?.Elements().FirstOrDefault(r=>(string?)r.Attribute("Id")==id&&(string?)r.Attribute("TargetMode")!="External");var target=(string?)relationship?.Attribute("Target");if(target is null)continue;
            var uri=new Uri(new Uri("https://document.invalid/xl/workbook.xml"),target);if(uri.Host!="document.invalid"||!uri.AbsolutePath.StartsWith("/xl/worksheets/",StringComparison.Ordinal))continue;
            var document=Xml(archive,Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),ref expanded);if(document is null)continue;result.AppendLine("[工作表："+(string?)sheet.Attribute("name")+"]");
            foreach(var row in document.Descendants(Sheet+"row")){
                foreach(var cell in row.Elements(Sheet+"c")){var type=(string?)cell.Attribute("t");var value=cell.Element(Sheet+"v")?.Value??"";if(type=="s"&&int.TryParse(value,out var index)&&index>=0&&index<shared.Length)value=shared[index];else if(type=="inlineStr")value=string.Concat(cell.Descendants(Sheet+"t").Select(t=>t.Value));else if(type=="b")value=value=="1"?"true":"false";if(value.Length==0&&cell.Element(Sheet+"f") is {} formula)value="="+formula.Value;if(value.Length>0)result.Append((string?)cell.Attribute("r")??"单元格").Append(": ").Append(value).Append('\t');CheckLength(result.Length);}
                result.AppendLine();CheckLength(result.Length);
            }
        }
        return result.ToString();
    }
}
