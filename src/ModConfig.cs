using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;

namespace AutoCompatFramework;

/// <summary>config.jsonc 한 줄 한 줄에 대응하는 설정 값들.</summary>
public sealed class ModConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("verboseLogging")] public bool VerboseLogging { get; set; }
    [JsonPropertyName("modFileParsing")] public bool ModFileParsing { get; set; } = true;
    [JsonPropertyName("inheritBaseConflicts")] public bool InheritBaseConflicts { get; set; }
    [JsonPropertyName("inheritCloneConflicts")] public bool InheritCloneConflicts { get; set; }
    [JsonPropertyName("VoidConflicts")] public List<string> VoidConflicts { get; set; } = [];
    [JsonPropertyName("ManualAdd")] public List<ManualAddEntry> ManualAdd { get; set; } = [];
    [JsonPropertyName("whitelist")] public List<string> Whitelist { get; set; } = [];
    [JsonPropertyName("blacklist")] public List<string> Blacklist { get; set; } = [];

    /// <summary>JSON 안의 주석(//)과 끝 쉼표를 허용해서 읽는다.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };
}

public sealed class ManualAddEntry
{
    [JsonPropertyName("attachmentIds"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> AttachmentIds { get; set; } = [];

    [JsonPropertyName("targetItemIds"), JsonConverter(typeof(StringOrListConverter))]
    public List<string> TargetItemIds { get; set; } = [];
}

/// <summary>"abc" 한 개로 써도, ["abc","def"] 배열로 써도 되게 하는 변환기.</summary>
public sealed class StringOrListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<string>();
        if (reader.TokenType == JsonTokenType.String)
        {
            var single = reader.GetString();
            if (!string.IsNullOrEmpty(single)) result.Add(single);
            return result;
        }
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var s = reader.GetString();
                    if (!string.IsNullOrEmpty(s)) result.Add(s);
                }
                else
                {
                    reader.Skip();
                }
            }
            return result;
        }
        reader.Skip();
        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}

/// <summary>설정의 ID 문자열들을 MongoId 집합으로 바꿔 둔 것(잘못된 ID 는 모아서 경고).</summary>
public sealed class ConfigSets
{
    public HashSet<MongoId> Blacklist { get; } = [];
    public HashSet<MongoId> Whitelist { get; } = [];
    public HashSet<MongoId> VoidConflicts { get; } = [];
    public List<string> Invalid { get; } = [];

    public static ConfigSets From(ModConfig config)
    {
        var sets = new ConfigSets();
        Fill(config.Blacklist, sets.Blacklist, sets.Invalid, "blacklist");
        Fill(config.Whitelist, sets.Whitelist, sets.Invalid, "whitelist");
        Fill(config.VoidConflicts, sets.VoidConflicts, sets.Invalid, "VoidConflicts");
        return sets;
    }

    private static void Fill(IEnumerable<string> source, HashSet<MongoId> target, List<string> invalid, string label)
    {
        foreach (var raw in source)
        {
            if (IdParser.TryParse(raw, out var id)) target.Add(id);
            else invalid.Add($"{label}: {raw}");
        }
    }
}

/// <summary>문자열 → MongoId 변환(24자리 16진수만 인정, 대소문자 무시).</summary>
public static class IdParser
{
    public static bool TryParse(string? raw, out MongoId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim().ToLowerInvariant();
        if (s.Length != 24) return false;
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        id = new MongoId(s);
        return true;
    }
}
