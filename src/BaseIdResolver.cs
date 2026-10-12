using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace AutoCompatFramework;

/// <summary>
/// "이 모드 아이템은 어떤 바닐라 아이템을 복제해서 만들었나?" 를 알아내는 부분(원작 findBaseId + modHelper 캐시).
/// 순서: 캐시 → 이름이 같은 바닐라 아이템 → (옵션) 다른 모드 JSON 파일에서 clone/itemTplToClone 추적.
/// </summary>
public sealed class BaseIdResolver
{
    private readonly Dictionary<MongoId, TemplateItem> _items;
    private readonly ModConfig _config;
    private readonly Reporter _reporter;
    private readonly IReadOnlyList<string> _otherModDirectories;
    private readonly string _cachePath;
    private readonly string _signature;

    /// <summary>모드 아이템 ID → 복제 원본 바닐라(또는 이미 해석된) ID. JSON 추적으로 알아낸 것만 캐시에 들어간다.</summary>
    private readonly Dictionary<MongoId, MongoId> _baseIdCache = [];

    /// <summary>"이름 → 그 이름을 가진 첫 바닐라 프리팹 아이템". 원작은 아이템마다 전체를 훑었지만 한 번만 만든다.</summary>
    private readonly Dictionary<string, MongoId> _vanillaByName = new(StringComparer.Ordinal);

    private ModJsonIndex? _jsonIndex;
    private bool _cacheLoaded;

    public bool CacheLoaded => _cacheLoaded;

    public BaseIdResolver(
        Dictionary<MongoId, TemplateItem> items,
        ModConfig config,
        Reporter reporter,
        IReadOnlyList<string> otherModDirectories,
        string cachePath,
        string signature)
    {
        _items = items;
        _config = config;
        _reporter = reporter;
        _otherModDirectories = otherModDirectories;
        _cachePath = cachePath;
        _signature = signature;

        foreach (var (id, item) in items)
        {
            if (!Util.HasVanillaPrefab(item)) continue;
            if (!string.IsNullOrEmpty(item.Name)) _vanillaByName.TryAdd(item.Name, id);
            if (!string.IsNullOrEmpty(item.Properties?.Name)) _vanillaByName.TryAdd(item.Properties!.Name!, id);
        }
    }

    /// <summary>모드 서명(설치된 모드 목록)을 만든다. 모드를 추가/삭제/업데이트하면 값이 바뀌어 캐시가 자동으로 새로 만들어진다.</summary>
    public static string BuildSignature(IEnumerable<SptMod> mods, int itemCount)
    {
        var lines = mods
            .Select(m => $"{m.ModMetadata.ModGuid}@{m.ModMetadata.Version}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .Append($"items={itemCount}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes)[..16];
    }

    public void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                _reporter.Info("Cache file not found, will generate new cache");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_cachePath), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("signature", out var sig) || sig.GetString() != _signature ||
                !root.TryGetProperty("baseIds", out var map) || map.ValueKind != JsonValueKind.Object)
            {
                _reporter.Info("Cache is from a different mod list (or old format); it will be regenerated");
                return;
            }

            foreach (var prop in map.EnumerateObject())
            {
                if (IdParser.TryParse(prop.Name, out var moddedId) &&
                    prop.Value.ValueKind == JsonValueKind.String &&
                    IdParser.TryParse(prop.Value.GetString(), out var baseId) &&
                    _items.ContainsKey(baseId))
                {
                    _baseIdCache[moddedId] = baseId;
                }
                else
                {
                    _reporter.Info($"Skipping invalid cache entry: {prop.Name} -> {prop.Value}");
                }
            }

            _cacheLoaded = true;
            _reporter.Success($"Cache found: Loaded {_baseIdCache.Count} entries from cache");
        }
        catch (Exception ex)
        {
            _reporter.Warn($"Failed to load cache: {ex.Message}");
        }
    }

    public void SaveCache()
    {
        if (_cacheLoaded || !_config.ModFileParsing) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_cachePath)!);
            var map = _baseIdCache.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToString());
            var json = JsonSerializer.Serialize(new { signature = _signature, baseIds = map }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cachePath, json);
            _reporter.Success($"Saved {_baseIdCache.Count} entries to cache");
        }
        catch (Exception ex)
        {
            _reporter.Warn($"Failed to save cache: {ex.Message}");
        }
    }

    /// <summary>복제 원본 ID 를 찾는다. 못 찾으면 null.</summary>
    public MongoId? FindBaseId(TemplateItem item)
    {
        var id = item.Id;

        if (_config.ModFileParsing && _baseIdCache.TryGetValue(id, out var cached))
        {
            if (_items.ContainsKey(cached))
            {
                _reporter.V(() => $"Cache hit for {id}: {cached}");
                return cached;
            }
            _reporter.V(() => $"Invalid cache entry for {id}: baseId {cached} not found in items");
            _baseIdCache.Remove(id);
        }

        var name = !string.IsNullOrEmpty(item.Name) ? item.Name
                 : !string.IsNullOrEmpty(item.Properties?.Name) ? item.Properties!.Name!
                 : id.ToString();

        if (_vanillaByName.TryGetValue(name, out var byName))
        {
            if (byName != id)
            {
                _reporter.V(() => $"Found base item {byName} for {id} via name match");
                return byName;
            }

            _reporter.V(() => $"Name match for {_reporter.Name(id)} resolved to itself, falling back to JSON parsing");
            if (_config.ModFileParsing && !_cacheLoaded)
            {
                var fromJson = ResolveFromJson(id);
                if (fromJson is not null) return fromJson;
            }
            _reporter.V(() => $"Modded item {_reporter.Name(id)}: No base item found for clone mapping");
            return null;
        }

        if (_config.ModFileParsing && !_cacheLoaded)
        {
            var fromJson = ResolveFromJson(id);
            if (fromJson is not null) return fromJson;
            _reporter.V(() => $"Modded item {_reporter.Name(id)}: No base item found for clone mapping");
            return null;
        }

        _reporter.V(() => $"JSON parsing disabled or cache loaded for {_reporter.Name(id)}; relying on name-based lookup");
        return null;
    }

    private MongoId? ResolveFromJson(MongoId id)
    {
        if (_jsonIndex is null)
        {
            var started = DateTime.UtcNow;
            _reporter.Info("AutoCompatFramework: Parsing mod files... Please wait.");
            _jsonIndex = ModJsonIndex.Build(_otherModDirectories, _reporter);
            _reporter.Info($"AutoCompatFramework: Read {_jsonIndex.FilesScanned} mod JSON files in {(DateTime.UtcNow - started).TotalSeconds:0.0}s" +
                           (_jsonIndex.FilesFailed > 0 ? $" ({_jsonIndex.FilesFailed} unreadable, skipped)" : ""));
        }
        return FindFromIndex(id, []);
    }

    private MongoId? FindFromIndex(MongoId itemId, HashSet<MongoId> visited)
    {
        if (!visited.Add(itemId))
        {
            _reporter.V(() => $"Circular clone reference detected for {itemId}");
            return null;
        }

        foreach (var cloneRaw in _jsonIndex!.Get(itemId.ToString()))
        {
            if (cloneRaw is null)
            {
                _reporter.V(() => $"No clone, itemTplToClone, _orig, or _original for {itemId}");
                return null;
            }
            if (!IdParser.TryParse(cloneRaw, out var cloneId)) continue;

            if (_items.TryGetValue(cloneId, out var cloneItem) && Util.HasVanillaPrefab(cloneItem))
            {
                Remember(itemId, cloneId);
                _reporter.V(() => $"Resolved {itemId} to vanilla base {cloneId} via JSON");
                return cloneId;
            }

            var chained = FindFromIndex(cloneId, visited);
            if (chained is { } baseId && _items.ContainsKey(baseId))
            {
                Remember(itemId, baseId);
                _reporter.V(() => $"Resolved {itemId} to base {baseId} via clone chain");
                return baseId;
            }
        }

        _reporter.V(() => $"No base item found for {itemId} in JSON files");
        return null;
    }

    private void Remember(MongoId itemId, MongoId baseId)
    {
        _baseIdCache[itemId] = baseId;
    }
}
