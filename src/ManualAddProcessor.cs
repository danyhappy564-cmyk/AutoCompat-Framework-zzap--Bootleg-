using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace AutoCompatFramework;

/// <summary>
/// config 의 ManualAdd 처리: "이 부품/탄을 저 무기·탄창에 꽂을 수 있게 강제로 넣어라".
/// 어느 칸에 넣을지는 (탄약 → 약실/탄창 칸) (탄창 → mod_magazine) (그 외 → 부품 종류별 표준 칸, 복제 원본이 들어가는 칸, mod_tactical 계열, 첫 칸) 순서로 정한다.
/// </summary>
public sealed class ManualAddProcessor
{
    private static readonly Regex TacticalSlot = new(@"^mod_tactical(_\d{3})?$", RegexOptions.Compiled);

    private static readonly (string Slot, MongoId Base)[] CommonModTypes =
    [
        ("mod_foregrip", BaseClassIds.FOREGRIP),
        ("mod_scope", BaseClassIds.OPTIC_SCOPE),
        ("mod_magazine", BaseClassIds.MAGAZINE),
        ("mod_muzzle", BaseClassIds.MUZZLE),
        ("mod_stock", BaseClassIds.STOCK),
        ("mod_barrel", BaseClassIds.BARREL),
        ("mod_handguard", BaseClassIds.HANDGUARD),
        ("mod_launcher", BaseClassIds.UBGL),
        ("mod_tactical", BaseClassIds.MOUNT),
        ("mod_tactical", BaseClassIds.FLASHLIGHT),
        ("mod_tactical", BaseClassIds.LIGHT_LASER_DESIGNATOR),
        ("mod_tactical", BaseClassIds.TACTICAL_COMBO),
        ("mod_bipod", BaseClassIds.BIPOD),
        ("mod_muzzle", BaseClassIds.COMPENSATOR),
        ("mod_muzzle", BaseClassIds.SILENCER),
        ("mod_pistol_grip", BaseClassIds.PISTOL_GRIP),
        ("mod_receiver", BaseClassIds.RECEIVER),
        ("mod_charge", BaseClassIds.CHARGING_HANDLE),
        ("mod_gas_block", BaseClassIds.GAS_BLOCK),
        ("mod_tactical", BaseClassIds.RAIL_COVER),
        ("mod_sight_rear", BaseClassIds.SIGHTS),
        ("mod_sight_front", BaseClassIds.SIGHTS),
        ("mod_scope", BaseClassIds.ASSAULT_SCOPE),
        ("mod_muzzle", BaseClassIds.FLASH_HIDER),
        ("mod_scope", BaseClassIds.COLLIMATOR),
        ("mod_sight_rear", BaseClassIds.IRON_SIGHT),
        ("mod_sight_front", BaseClassIds.IRON_SIGHT),
        ("mod_scope", BaseClassIds.COMPACT_COLLIMATOR),
        ("mod_muzzle", BaseClassIds.COMB_MUZZLE_DEVICE),
    ];

    private readonly Dictionary<MongoId, TemplateItem> _items;
    private readonly ModConfig _config;
    private readonly ConfigSets _sets;
    private readonly Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> _itemSlots;
    private readonly Func<MongoId, MongoId, bool> _isOf;
    private readonly Reporter _reporter;
    private readonly Dictionary<MongoId, MongoId> _itemToBase;
    private readonly List<TemplateItem> _allItems;
    private readonly Dictionary<MongoId, List<string>> _compatibleSlotsByBase = [];

    public ManualAddProcessor(
        Dictionary<MongoId, TemplateItem> items,
        ModConfig config,
        ConfigSets sets,
        Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> itemSlots,
        Func<MongoId, MongoId, bool> isOfBaseclass,
        Reporter reporter,
        Dictionary<MongoId, MongoId> itemToBase,
        List<TemplateItem> allItems)
    {
        _items = items;
        _config = config;
        _sets = sets;
        _itemSlots = itemSlots;
        _isOf = isOfBaseclass;
        _reporter = reporter;
        _itemToBase = itemToBase;
        _allItems = allItems;
    }

    public int Run(HashSet<MongoId> modifiedItems)
    {
        var added = 0;

        foreach (var manual in _config.ManualAdd)
        {
            if (manual.AttachmentIds.Count == 0 || manual.TargetItemIds.Count == 0)
            {
                _reporter.V(() => "Pass 1: Invalid ManualAdd entry (empty attachmentIds or targetItemIds)");
                continue;
            }

            foreach (var attachmentRaw in manual.AttachmentIds)
            {
                if (!IdParser.TryParse(attachmentRaw, out var attachmentId))
                {
                    _reporter.V(() => $"Pass 1: Invalid attachmentId in ManualAdd entry: {attachmentRaw}");
                    continue;
                }
                if (!_items.TryGetValue(attachmentId, out var attachment))
                {
                    _reporter.V(() => $"Pass 1: ManualAdd attachmentId {attachmentId} not found in database");
                    continue;
                }
                if (!_isOf(attachmentId, BaseClassIds.MOD) && !_isOf(attachmentId, BaseClassIds.AMMO))
                {
                    _reporter.V(() => $"Pass 1: ManualAdd attachmentId {_reporter.Name(attachmentId)} is neither a mod nor ammo");
                    continue;
                }
                if (_sets.Blacklist.Contains(attachmentId))
                {
                    _reporter.V(() => $"Pass 1: Skipped ManualAdd attachment {_reporter.Name(attachmentId)}: in blacklist");
                    continue;
                }

                foreach (var targetRaw in manual.TargetItemIds)
                {
                    if (!IdParser.TryParse(targetRaw, out var targetId))
                    {
                        _reporter.V(() => $"Pass 1: Invalid targetItemId in ManualAdd entry: {targetRaw}");
                        continue;
                    }
                    if (!_items.TryGetValue(targetId, out var target))
                    {
                        _reporter.V(() => $"Pass 1: ManualAdd targetItemId {targetId} not found in database");
                        continue;
                    }
                    if (_sets.Blacklist.Contains(targetId))
                    {
                        _reporter.V(() => $"Pass 1: Skipped ManualAdd to target {_reporter.Name(targetId)}: in blacklist");
                        continue;
                    }

                    var (slotName, filter) = PickSlot(attachmentId, attachment, targetId, target);
                    if (slotName is null || filter is null)
                    {
                        _reporter.V(() => $"Pass 1: No valid slot found for attachment {_reporter.Name(attachmentId)} on target item {_reporter.Name(targetId)}");
                        continue;
                    }

                    if (filter.Add(attachmentId))
                    {
                        modifiedItems.Add(targetId);
                        _reporter.V(() => $"Pass 1: Manually added attachment {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {slotName}");
                        added++;
                    }
                    else
                    {
                        _reporter.V(() => $"Pass 1: Skipped ManualAdd {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {slotName}: already included");
                    }
                }
            }
        }

        return added;
    }

    private (string? SlotName, HashSet<MongoId>? Filter) PickSlot(MongoId attachmentId, TemplateItem attachment, MongoId targetId, TemplateItem target)
    {
        var targetSlots = _itemSlots.TryGetValue(targetId, out var m) ? m : [];

        // 1) 탄약: 무기면 첫 약실, 탄창이면 첫 탄약 칸
        if (_isOf(attachmentId, BaseClassIds.AMMO))
        {
            if (_isOf(targetId, BaseClassIds.WEAPON))
            {
                var chamber = target.Properties?.Chambers?.FirstOrDefault();
                if (chamber is not null && Util.TryGetFilter(chamber, out var f)) return (chamber.Name, f);
            }
            else if (_isOf(targetId, BaseClassIds.MAGAZINE))
            {
                var cartridge = target.Properties?.Cartridges?.FirstOrDefault();
                if (cartridge is not null && Util.TryGetFilter(cartridge, out var f)) return (cartridge.Name, f);
            }
            return (null, null);
        }

        // 2) 탄창: 무기의 mod_magazine 칸. 탄창이 쓰는 구경과 무기 구경이 같을 때만
        if (_isOf(attachmentId, BaseClassIds.MAGAZINE))
        {
            if (!targetSlots.TryGetValue("mod_magazine", out var magazineSlot)) return (null, null);

            var magazineCalibers = new HashSet<string>();
            foreach (var cartridge in attachment.Properties?.Cartridges ?? [])
            {
                if (!Util.TryGetFilter(cartridge, out var cartridgeFilter)) continue;
                foreach (var ammoId in cartridgeFilter)
                {
                    var caliber = (_items.TryGetValue(ammoId, out var ammo) ? ammo.Properties?.Caliber : null)?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(caliber)) magazineCalibers.Add(caliber);
                }
            }
            var targetCaliber = (target.Properties?.AmmoCaliber ?? "").ToLowerInvariant();
            if (magazineCalibers.Contains(targetCaliber))
            {
                _reporter.V(() => $"Pass 1: Matched magazine {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} mod_magazine slot via caliber {targetCaliber}");
                return ("mod_magazine", magazineSlot);
            }
            return (null, null);
        }

        // 3) 그 외 부품
        _itemToBase.TryGetValue(attachmentId, out var baseId);
        var hasBase = _itemToBase.ContainsKey(attachmentId);

        string? slotName = null;
        HashSet<MongoId>? targetFilter = null;

        // 3-1) 부품 종류별 표준 칸 이름
        foreach (var (slot, baseClass) in CommonModTypes)
        {
            if (!_isOf(attachmentId, baseClass)) continue;
            if (targetSlots.TryGetValue(slot, out var found))
            {
                slotName = slot;
                targetFilter = found;
                if (found.Count > 0)
                {
                    _reporter.V(() => $"Pass 1: Matched attachment {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {slot} via base class");
                    break;
                }
            }
        }
        if (targetFilter is null) slotName = null;

        // 3-2) 복제 원본이 들어가는 칸 이름으로 추론
        if (slotName is null && hasBase)
        {
            foreach (var possible in CompatibleSlotsFor(baseId))
            {
                if (targetSlots.TryGetValue(possible, out var found))
                {
                    slotName = possible;
                    targetFilter = found;
                    _reporter.V(() => $"Pass 1: Matched attachment {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {possible} via clone-based inference from base {_reporter.Name(baseId)}");
                    break;
                }
            }
        }

        // 3-3) mod_tactical, mod_tactical_001 ... 칸
        if (slotName is null)
        {
            foreach (var (possible, filter) in targetSlots)
            {
                if (!TacticalSlot.IsMatch(possible)) continue;

                if (_isOf(attachmentId, BaseClassIds.MOUNT) ||
                    _isOf(attachmentId, BaseClassIds.FLASHLIGHT) ||
                    _isOf(attachmentId, BaseClassIds.LIGHT_LASER_DESIGNATOR) ||
                    _isOf(attachmentId, BaseClassIds.TACTICAL_COMBO) ||
                    _isOf(attachmentId, BaseClassIds.RAIL_COVER))
                {
                    slotName = possible;
                    targetFilter = filter;
                    _reporter.V(() => $"Pass 1: Matched attachment {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {possible} via dynamic tactical slot matching");
                    break;
                }
                if (hasBase && filter.Contains(baseId))
                {
                    slotName = possible;
                    targetFilter = filter;
                    _reporter.V(() => $"Pass 1: Matched attachment {_reporter.Name(attachmentId)} to {_reporter.Name(targetId)} slot {possible} via base item in tactical slot filter");
                    break;
                }
            }
        }

        // 3-4) 마지막 수단: 부품 자신의 첫 칸 이름과 같은 칸
        if (slotName is null)
        {
            var own = attachment.Properties?.Slots?.FirstOrDefault();
            if (own is not null && Util.TryGetFilter(own, out _) && targetSlots.TryGetValue(own.Name!.ToLowerInvariant(), out var found))
            {
                slotName = own.Name;
                targetFilter = found;
                _reporter.V(() => $"Pass 1: Fallback to attachment slot {own.Name} for {_reporter.Name(attachmentId)} on {_reporter.Name(targetId)}");
            }
        }

        return (slotName, targetFilter);
    }

    /// <summary>복제 원본 ID 를 허용하는 칸들의 이름(소문자). 전체 아이템을 한 번 훑어서 원본별로 기억해 둔다.</summary>
    private List<string> CompatibleSlotsFor(MongoId baseId)
    {
        if (_compatibleSlotsByBase.TryGetValue(baseId, out var cached)) return cached;

        var names = new List<string>();
        foreach (var item in _allItems)
        {
            foreach (var (_, slot) in Util.AllSlots(item))
            {
                if (!Util.TryGetFilter(slot, out var filter) || !filter.Contains(baseId)) continue;
                var lower = slot.Name!.ToLowerInvariant();
                if (!names.Contains(lower)) names.Add(lower);
            }
        }

        _compatibleSlotsByBase[baseId] = names;
        return names;
    }
}
