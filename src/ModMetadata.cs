using SPTarkov.Server.Core.Models.Spt.Mod;
using SemVerRange = SemanticVersioning.Range;
using SemVerVersion = SemanticVersioning.Version;

namespace AutoCompatFramework;

/// <summary>
/// 모드 정보(이름·버전·작성자)를 SPT 서버에 알려주는 부분.
/// ModGuid 는 원작(Solid-Rhoads)과 겹치지 않게 하되, 원작이 4.x 로 나오면 동시 설치가 막히도록 같은 계열 값을 쓴다.
/// </summary>
public record ModMetadata : IModMetadata
{
    public const string ModGuidValue = "com.solidrhoads.autocompatframework";

    public string ModGuid { get; init; } = ModGuidValue;
    public string Name { get; init; } = "AutoCompat Framework";
    public string Author { get; init; } = "Solid-Rhoads";
    public List<string>? Contributors { get; init; } = ["R_F (SPT 4.1 port)"];
    public SemVerVersion Version { get; init; } = new("1.3.2");
    public SemVerRange SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemVerRange>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/Solid-Rhoads/AutoCompat-Framework";
    public string License { get; init; } = "MIT";
}
