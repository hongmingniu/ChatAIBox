using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatAIBox.Core;

public sealed record Envelope(int V, string Nonce, string Ciphertext);
public sealed record SyncMessage(string Kind, string Id, long At, ChatRecord[] Records, string[][]? Known = null);
public static partial class SyncCrypto
{
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex KeyPattern();
    public static byte[] Key(string key) => KeyPattern().IsMatch(key) ? Convert.FromHexString(key) : throw new InvalidDataException("配对密钥必须为 64 位十六进制字符");
    public static Envelope Seal<T>(T value, string key)
    {
        var plain = Encoding.UTF8.GetBytes(Json.Write(value));
        if (plain.Length > Limits.MaxEnvelopeBytes - 16) throw new InvalidDataException("同步数据过大");
        var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[plain.Length + 16];
        using var aes = new AesGcm(Key(key), 16);
        aes.Encrypt(nonce, plain, cipher.AsSpan(0, plain.Length), cipher.AsSpan(plain.Length));
        return new(1, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher));
    }
    public static T Open<T>(Envelope box, string key)
    {
        var nonce = Convert.FromBase64String(box.Nonce); var cipher = Convert.FromBase64String(box.Ciphertext);
        if (box.V != 1 || nonce.Length != 12 || cipher.Length < 16 || cipher.Length > Limits.MaxEnvelopeBytes) throw new InvalidDataException("加密信封无效");
        var plain = new byte[cipher.Length - 16]; using var aes = new AesGcm(Key(key), 16);
        aes.Decrypt(nonce, cipher.AsSpan(0, plain.Length), cipher.AsSpan(plain.Length), plain);
        return Json.Read<T>(Encoding.UTF8.GetString(plain));
    }
    public static string Room(string key) => Convert.ToHexStringLower(HMACSHA256.HashData(Key(key), Encoding.UTF8.GetBytes("ChatAIBox relay channel v1")));
    public static void ValidateResponse(SyncMessage value, string id)
    {
        if(value.Kind != "response" || value.Id != id || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - value.At) > 300000) throw new InvalidDataException("同步响应验证失败");
    }
    public static Dictionary<string,string>? Inventory(string[][]? known)
    {
        if(known is null)return null;
        if(known.Length>Limits.MaxRecords)throw new InvalidDataException("同步清单过大");
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in known){if(pair is not {Length:2}||pair[0] is not {Length:>0 and <=160}||pair[1] is not {Length:53}||!result.TryAdd(pair[0],pair[1]))throw new InvalidDataException("同步清单无效");}
        return result;
    }
}
