using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace AutoCompatFramework;

/// <summary>여러 클래스가 같이 쓰는 작은 도구들.</summary>
public static class Util
{
    /// <summary>게임 원본 프리팹 경로(assets/content/...)를 쓰는 아이템인가 = 바닐라 모델을 쓰는가.</summary>
    public static bool HasVanillaPrefab(TemplateItem? item) =>
        item?.Properties?.Prefab?.Path?.StartsWith("assets/content/", StringComparison.Ordinal) ?? false;

    /// <summary>슬롯의 첫 번째 필터(허용 아이템 목록). 없거나 이름이 비었으면 false.</summary>
    public static bool TryGetFilter(Slot slot, out HashSet<MongoId> filter)
    {
        filter = null!;
        if (string.IsNullOrEmpty(slot.Name)) return false;
        var first = slot.Properties?.Filters?.FirstOrDefault();
        if (first?.Filter is null) return false;
        filter = first.Filter;
        return true;
    }

    /// <summary>무기/부품이 가진 칸 3종류(Slots, Chambers, Cartridges)를 이름표와 함께 돌려준다.</summary>
    public static IEnumerable<(string Type, Slot Slot)> AllSlots(TemplateItem item)
    {
        var p = item.Properties;
        if (p is null) yield break;
        foreach (var s in p.Slots ?? []) yield return ("Slots", s);
        foreach (var s in p.Chambers ?? []) yield return ("Chambers", s);
        foreach (var s in p.Cartridges ?? []) yield return ("Cartridges", s);
    }
}
