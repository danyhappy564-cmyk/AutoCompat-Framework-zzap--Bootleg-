using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoCompatFramework;

/// <summary>
/// 다른 모드 폴더의 .json/.jsonc 를 전부 한 번만 읽어서 "아이템 ID → 복제 원본 ID" 표를 만든다.
/// 원작(modHelper.findBaseIdFromJson)은 아이템 하나마다 모든 파일을 다시 읽었지만, 결과는 같고 훨씬 빠르다.
/// 찾는 규칙은 원작과 동일:
///   1) 파일 최상위 키가 그 ID 면 그 값을 항목으로 본다 (WTT 등의 "{ID: {itemTplToClone: ...}}" 형식)
///   2) 아니면 파일 어디든 "_id" 가 그 ID 인 객체를 항목으로 본다
///   항목에서 clone → itemTplToClone → _orig → _original 순으로 첫 값을 복제 원본으로 쓴다.
/// </summary>
public sealed class ModJsonIndex
{
    private static readonly Regex IdPattern = new("^[0-9a-fA-F]{24}$", RegexOptions.Compiled);
    private static readonly string[] CloneKeys = ["clone", "itemTplToClone", "_orig", "_original"];
    private const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>ID → 파일 순서대로의 후보들(복제 원본 ID, 없으면 null).</summary>
    private readonly Dictionary<string, List<string?>> _entries = new(StringComparer.OrdinalIgnoreCase);

    public int FilesScanned { get; private set; }
    public int FilesFailed { get; private set; }

    public IReadOnlyList<string?> Get(string itemId) =>
        _entries.TryGetValue(itemId, out var list) ? list : [];

    public static ModJsonIndex Build(IEnumerable<string> modDirectories, Reporter reporter)
    {
        var index = new ModJsonIndex();
        foreach (var dir in modDirectories)
        {
            foreach (var file in EnumerateJsonFiles(dir, reporter))
            {
                index.AddFile(file, reporter);
            }
        }
        return index;
    }

    private static IEnumerable<string> EnumerateJsonFiles(string dir, Reporter reporter)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
                            && !f.Contains("tsconfig.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            reporter.Warn($"AutoCompatFramework: Error scanning directory {dir}: {ex.Message}");
            yield break;
        }
        reporter.V(() => $"Scanning directory {dir}");
        foreach (var f in files) yield return f;
    }

    private void AddFile(string path, Reporter reporter)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                reporter.V(() => $"Skipped {path}: file too large");
                return;
            }

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = 256,
            });
            FilesScanned++;

            // 파일 하나 안에서는 "최상위 키" 가 "_id 를 가진 중첩 객체" 보다 우선, 같은 종류 안에서는 먼저 나온 것이 우선.
            var direct = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var nested = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (!IdPattern.IsMatch(prop.Name) || IsFalsy(prop.Value)) continue;
                    direct.TryAdd(prop.Name, prop.Value.ValueKind == JsonValueKind.Object ? ReadCloneId(prop.Value) : null);
                }
            }
            Collect(root, nested);

            foreach (var (id, clone) in direct) Append(id, clone);
            foreach (var (id, clone) in nested)
            {
                if (!direct.ContainsKey(id)) Append(id, clone);
            }
        }
        catch (Exception ex)
        {
            FilesFailed++;
            reporter.V(() => $"Failed to parse {path}: {ex.Message}");
        }
    }

    private void Append(string id, string? clone)
    {
        if (!_entries.TryGetValue(id, out var list))
        {
            list = [];
            _entries[id] = list;
        }
        list.Add(clone);
    }

    /// <summary>JS 의 truthy 판정(null/false/0/"" 은 항목으로 안 친다).</summary>
    private static bool IsFalsy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => true,
        JsonValueKind.String => e.GetString()!.Length == 0,
        JsonValueKind.Number => e.TryGetDouble(out var d) && d == 0,
        _ => false,
    };

    /// <summary>"_id" 가 24자리 ID 인 객체를 전부 모은다(깊이 우선, 먼저 나온 것 우선).</summary>
    private static void Collect(JsonElement element, Dictionary<string, string?> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("_id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString()!;
                    if (IdPattern.IsMatch(id)) into.TryAdd(id, ReadCloneId(element));
                }
                foreach (var prop in element.EnumerateObject()) Collect(prop.Value, into);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray()) Collect(child, into);
                break;
        }
    }

    private static string? ReadCloneId(JsonElement obj)
    {
        foreach (var key in CloneKeys)
        {
            if (obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }
}
