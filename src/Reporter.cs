using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace AutoCompatFramework;

/// <summary>
/// 콘솔 로그 출력 담당. verboseLogging 이 꺼져 있으면 "상세" 로그는 아예 만들지도 않는다(속도).
/// 원작의 logMessage(level, message, verboseOnly) 에 해당한다.
/// </summary>
public sealed class Reporter
{
    private readonly ISptLogger<AutoCompatMod> _logger;
    private readonly LazyLoad<GlobalLocaleDictionary>? _english;
    private GlobalLocaleDictionary? _englishCache;

    public bool Verbose { get; }

    public Reporter(ISptLogger<AutoCompatMod> logger, bool verbose, LazyLoad<GlobalLocaleDictionary>? english)
    {
        _logger = logger;
        Verbose = verbose;
        _english = english;
    }

    public void Info(string message) => _logger.Info(message);
    public void Success(string message) => _logger.Success(message);
    public void Warn(string message) => _logger.Warning(message);
    public void Error(string message) => _logger.Error(message);

    public void Colored(string message, string colorName)
    {
        var color = colorName.ToUpperInvariant() switch
        {
            "CYAN" => Spectre.Console.Color.Aqua,
            "MAGENTA" => Spectre.Console.Color.Fuchsia,
            _ => (Spectre.Console.Color?)null,
        };
        if (color is null) _logger.Info(message);
        else _logger.LogWithColor(message, color, null, null);
    }

    /// <summary>verboseLogging 이 켜졌을 때만 출력(원작의 verboseOnly=true 인 info/debug/warning).</summary>
    public void V(Func<string> message)
    {
        if (Verbose) _logger.Info(message());
    }

    /// <summary>아이템 ID 를 "ID (영어 이름)" 으로. 상세 로그에서만 쓰므로 영어 로케일을 필요할 때 처음 불러온다.</summary>
    public string Name(MongoId id)
    {
        try
        {
            _englishCache ??= _english?.Value;
            if (_englishCache is not null && _englishCache.TryGetValue($"{id} Name", out var name) && !string.IsNullOrEmpty(name))
            {
                return $"{id} ({name})";
            }
        }
        catch
        {
            // 이름을 못 읽어도 로그만 덜 예쁠 뿐이다.
        }
        return $"{id} (Unknown)";
    }
}
