using ChatAIBox.Core;
using SkiaSharp;
using Windows.Storage;
namespace ChatAIBox;
public static class BitmapTools
{
    public static async Task<ImageAttachment> ChatImageAsync(StorageFile file)
    {
        var bytes=await NativeActions.FileBytesAsync(file,20*1024*1024);return await ChatImageAsync(bytes,file.Name);
    }
    public static Task<ImageAttachment> ChatImageAsync(byte[] bytes,string name)=>Task.Run(()=>{var (encoded,width,height)=Normalize(bytes,1400);var url="data:image/jpeg;base64,"+Convert.ToBase64String(encoded);if(url.Length>1_500_000)throw new InvalidDataException("图片压缩后仍过大，请选择较小图片");return new ImageAttachment(Guid.NewGuid().ToString(),name,"image/jpeg",url,width,height);});
    public static (byte[] Bytes,int Width,int Height) Normalize(byte[] bytes,int edge)
    {
        using var image=SKImage.FromEncodedData(bytes)??throw new InvalidDataException("图片格式无效");var scale=Math.Min(1,edge/(double)Math.Max(image.Width,image.Height));var width=Math.Max(1,(int)Math.Round(image.Width*scale));var height=Math.Max(1,(int)Math.Round(image.Height*scale));using var surface=SKSurface.Create(new SKImageInfo(width,height));surface.Canvas.Clear(SKColors.White);surface.Canvas.DrawImage(image,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear));using var result=surface.Snapshot();using var data=result.Encode(SKEncodedImageFormat.Jpeg,85);return(data.ToArray(),width,height);
    }
    public static Task<byte[]> BlurAsync(byte[] bytes,int radius)=>Task.Run(()=>{using var image=SKImage.FromEncodedData(bytes)??throw new InvalidDataException("背景图片格式无效");using var surface=SKSurface.Create(new SKImageInfo(image.Width,image.Height));using var paint=new SKPaint();if(radius>0)paint.ImageFilter=SKImageFilter.CreateBlur(radius,radius);surface.Canvas.Clear(SKColors.Transparent);surface.Canvas.DrawImage(image,0,0,paint);using var result=surface.Snapshot();using var data=result.Encode(SKEncodedImageFormat.Png,90);return data.ToArray();});
}
