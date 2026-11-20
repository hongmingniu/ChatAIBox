using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatAIBox.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("数据格式无效");
}
public sealed record Source(string Title, string Url);
public sealed record ImageAttachment(string Id, string Name, string Mime, string DataUrl, int? Width = null, int? Height = null);
public sealed record SearchTrace(string Id, string Kind, string[] Queries, string Status, string? Url = null);
public sealed record SummaryPartial(string Id, string Rev, int Offset);
public sealed record ContextSummary(string Text, int Covered, string PrefixHash, SummaryPartial? Partial = null);
public sealed record ChatRecord
{
    public required string Id { get; init; }
    public required string Rev { get; init; }
    public required string Type { get; init; }
    public bool Deleted { get; init; }
    public string? Title { get; init; }
    public long Created { get; init; }
    public string? ChatId { get; init; }
    public string? Role { get; init; }
    public string? Content { get; init; }
    public string? Reasoning { get; init; }
    public Source[]? Sources { get; init; }
    public ImageAttachment[]? Images { get; init; }
    public SearchTrace[]? Searches { get; init; }
    public bool? Interrupted { get; init; }
    public ContextSummary? ContextSummary { get; init; }
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
}
public sealed record AppState(int Schema, string DeviceId, ChatRecord[] Records);
public sealed record Provider
{
    public string Id { get; init; } = "deepseek";
    public string Name { get; init; } = "DeepSeek";
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
    public string ApiKey { get; init; } = "";
    public string[] Models { get; init; } = ["deepseek-flash", "deepseek-v4-pro"];
    public bool Vision { get; init; } = true;
    public bool Search { get; init; } = true;
    public string Thinking { get; init; } = "deepseek";
    public int ContextWindow { get; init; } = 1_048_576;
    public int MaxOutputTokens { get; init; } = 393_216;
    public string AsrModel { get; init; } = "";
}
public sealed record Settings
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "deepseek-flash";
    public string Proxy { get; init; } = "";
    public bool SyncEnabled { get; init; }
    public string SyncKey { get; init; } = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public int Port { get; init; } = 39281;
    public string PeerAddress { get; init; } = "";
    public string SyncMode { get; init; } = "relay";
    public string RelayUrl { get; init; } = "";
    public string RelayAccessKey { get; init; } = "";
    public bool AutoCompress { get; init; } = true;
    public int ContextBudget { get; init; } = Limits.ContextBudget;
    public Provider[] Providers { get; init; } = [];
    public string ActiveProviderId { get; init; } = "deepseek";
    public string Theme { get; init; } = "system";
    public string WindowBackdrop { get; init; } = "acrylic";
    public double FontSize { get; init; } = 15;
    public string FontFamily { get; init; } = "HarmonyOS Sans SC";
    public string FontScope { get; init; } = "all";
    public bool FontCode { get; init; }
    public string FontFileId { get; init; } = "";
    public string FontFileName { get; init; } = "";
    public string BackgroundId { get; init; } = "";
    public string BackgroundName { get; init; } = "";
    public int BackgroundOpacity { get; init; } = 55;
    public int BackgroundBlur { get; init; } = 12;
    public string TtsVoice { get; init; } = "zh-CN-XiaoxiaoNeural";
    public int TtsRate { get; init; }
    public Provider Provider(string? id = null) => (id ?? ActiveProviderId) == "deepseek" ? new() { ApiKey = ApiKey } : Providers.FirstOrDefault(p => p.Id == (id ?? ActiveProviderId)) ?? throw new InvalidDataException("提供方尚未配置");
}
public static class Limits
{
    public const int ChatOutput = 24576, SummaryOutput = 81920, SummaryCharacters = 1_000_000, ContextBudget = 1_019_904, MaxRecords = 30000, MaxEnvelopeBytes = 96 * 1024 * 1024;
}
