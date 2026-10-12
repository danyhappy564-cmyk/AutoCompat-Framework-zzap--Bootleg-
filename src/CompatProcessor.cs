using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace AutoCompatFramework;

public sealed class PassResult
{
    public int AmmoToChambers { get; set; }
    public int AmmoToCartridges { get; set; }
    public int AttachmentsToSlots { get; set; }
    public int BaseConflictsAdded { get; set; }
    public int ClonedConflictsAdded { get; set; }
    public int ConflictsVoided { get; set; }
    public int ManualAdditions { get; set; }
    public HashSet<MongoId> ModifiedItems { get; } = [];

    public bool NothingChanged =>
        AmmoToChambers == 0 && AmmoToCartridges == 0 && AttachmentsToSlots == 0 &&
        BaseConflictsAdded == 0 && ClonedConflictsAdded == 0 && ConflictsVoided == 0 && ManualAdditions == 0;
}

/// <summary>바닐라 아이템 ID 목록(WEAPON / MOD / AMMO) — data/vanillaItems.json.</summary>
public sealed class VanillaItems
{
    public HashSet<MongoId> Weapon { get; } = [];
    public HashSet<MongoId> Mod { get; } = [];
    public HashSet<MongoId> Ammo { get; } = [];
}

/// <summary>
/// 핵심 계산(원작 mod.ts 의 processCompatibility). 모드 무기·부품·탄약을 찾아서
///  1) 같은 구경의 모드 탄을 모드 무기 약실 / 모드 탄창에 넣고
///  2) 바닐라 부품이 들어가는 칸에 그 복제본(모드 부품)도 들어가게 하고
///  3) (옵션) 충돌 목록을 복제본에 물려주고 4) ManualAdd 를 적용한다.
/// 다른 모드가 아이템을 다 등록한 뒤에 한 번만 실행해야 한다.
/// </summary>
public sealed class CompatProcessor
{
    private readonly Dictionary<MongoId, TemplateItem> _items;
    private readonly ModConfig _config;
    private readonly ConfigSets _sets;
    private readonly VanillaItems _vanilla;
    private readonly Func<MongoId, MongoId, bool> _isOf;
    private readonly Reporter _reporter;
    private readonly BaseIdResolver _resolver;

    public CompatProcessor(
        Dictionary<MongoId, TemplateItem> items,
        ModConfig config,
        ConfigSets sets,
        VanillaItems vanilla,
        Func<MongoId, MongoId, bool> isOfBaseclass,
        Reporter reporter,
        BaseIdResolver resolver)
    {
        _items = items;
        _config = config;
        _sets = sets;
        _vanilla = vanilla;
        _isOf = isOfBaseclass;
        _reporter = reporter;
        _resolver = resolver;
    }

    /// <summary>"mm" 을 구경 이름에서 지운다(원작 choccyPatch): 모드마다 "9x19mm" / "9x19" 로 달라서 같은 구경끼리 맞추려는 처리.</summary>
    public static void NormalizeCalibers(Dictionary<MongoId, TemplateItem> items)
    {
        foreach (var item in items.Values)
        {
            var p = item.Properties;
            if (p is null) continue;
            if (!string.IsNullOrEmpty(p.Caliber)) p.Caliber = Regex.Replace(p.Caliber, "mm", "", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(p.AmmoCaliber)) p.AmmoCaliber = Regex.Replace(p.AmmoCaliber, "mm", "", RegexOptions.IgnoreCase);
        }
    }

    private bool IsModded(TemplateItem item, HashSet<MongoId> vanillaIds) =>
        !vanillaIds.Contains(item.Id) || !Util.HasVanillaPrefab(item);

    private bool IsVanillaPrefabId(MongoId id) => _items.TryGetValue(id, out var it) && Util.HasVanillaPrefab(it);

    public PassResult Run()
    {
        var result = new PassResult();

        var allItems = _items.Values.ToList();
        var allWeapons = allItems.Where(x => _isOf(x.Id, BaseClassIds.WEAPON)).ToList();
        var allMods = allItems.Where(x => _isOf(x.Id, BaseClassIds.MOD)).ToList();
        var allAmmo = allItems.Where(x => _isOf(x.Id, BaseClassIds.AMMO)).ToList();

        var moddedWeapons = allWeapons.Where(x => IsModded(x, _vanilla.Weapon)).ToList();
        var moddedAttachments = allMods.Where(x => IsModded(x, _vanilla.Mod)).ToList();
        var moddedAmmo = allAmmo.Where(x => IsModded(x, _vanilla.Ammo)).ToList();

        _reporter.Info($"AutoCompatFramework: Found {moddedWeapons.Count} modded weapons, {moddedAttachments.Count} modded attachments, {moddedAmmo.Count} modded ammo");
        _reporter.V(() => "Excluding ammo from clone mapping, using caliber-based logic for chambers and cartridges");

        // ── 복제 원본 매핑 ──
        var itemToBase = new Dictionary<MongoId, MongoId>();
        var baseToClones = new Dictionary<MongoId, List<MongoId>>();

        if (_config.ModFileParsing) _resolver.LoadCache();
        BuildCloneMaps(moddedWeapons, itemToBase, baseToClones);
        BuildCloneMaps(moddedAttachments, itemToBase, baseToClones);
        if (_config.ModFileParsing) _resolver.SaveCache();

        // ── 구경 → 탄약 ──
        var caliberToAmmo = new Dictionary<string, List<MongoId>>();
        foreach (var ammo in allAmmo.Concat(moddedAmmo))
        {
            var caliber = (ammo.Properties?.Caliber is { Length: > 0 } c ? c : ammo.Properties?.AmmoCaliber ?? "").ToLowerInvariant();
            if (caliber.Length == 0)
            {
                _reporter.V(() => $"Ammo {_reporter.Name(ammo.Id)}: No valid Caliber or ammoCaliber found");
                continue;
            }
            if (!caliberToAmmo.TryGetValue(caliber, out var list)) caliberToAmmo[caliber] = list = [];
            if (!list.Contains(ammo.Id)) list.Add(ammo.Id);
        }

        var weaponsToProcess = moddedWeapons;
        var magazinesToProcess = moddedAttachments.Where(x => _isOf(x.Id, BaseClassIds.MAGAZINE)).ToList();
        var slottedItemsToProcess = moddedWeapons.Concat(moddedAttachments).ToList();

        var manualAddTargets = new HashSet<MongoId>();
        foreach (var manual in _config.ManualAdd)
        {
            foreach (var raw in manual.TargetItemIds)
            {
                if (IdParser.TryParse(raw, out var id)) manualAddTargets.Add(id);
            }
        }

        var itemsToProcessForSlots = slottedItemsToProcess
            .Concat(allItems.Where(x => _sets.Whitelist.Contains(x.Id) || manualAddTargets.Contains(x.Id)));

        // ── 칸 이름(소문자) → 허용 목록 표 ──
        var itemSlots = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>();
        foreach (var item in itemsToProcessForSlots)
        {
            var slotsMap = new Dictionary<string, HashSet<MongoId>>();
            foreach (var (type, slot) in Util.AllSlots(item))
            {
                if (!Util.TryGetFilter(slot, out var filter))
                {
                    _reporter.V(() => $"Item {_reporter.Name(item.Id)}: {type} slot {slot.Name} has invalid or missing filters");
                    continue;
                }
                slotsMap[slot.Name!.ToLowerInvariant()] = filter;
            }

            if (_reporter.Verbose)
            {
                if (slotsMap.Count == 0) _reporter.V(() => $"No slots found for item {_reporter.Name(item.Id)}");
                else _reporter.V(() => $"Slots for item {_reporter.Name(item.Id)}: {string.Join(", ", slotsMap.Keys)}");
            }
            itemSlots[item.Id] = slotsMap;
        }

        // ── "전용(proprietary) 칸" 판정: 칸 허용 목록이 비었거나 전부 모드 전용 아이템이면 그 모드 전용 칸 ──
        bool IsProprietarySlot(HashSet<MongoId> filter) => filter.Count == 0 || filter.All(id => !IsVanillaPrefabId(id));

        var proprietaryItems = new HashSet<MongoId>();
        foreach (var item in slottedItemsToProcess)
        {
            if (!itemSlots.TryGetValue(item.Id, out var slotsMap)) continue;
            foreach (var (slotName, filter) in slotsMap)
            {
                if (!slotName.Contains("mod_") || !IsProprietarySlot(filter)) continue;
                foreach (var acceptedId in filter)
                {
                    if (!IsVanillaPrefabId(acceptedId)) proprietaryItems.Add(acceptedId);
                }
            }
        }

        var nonProprietaryItems = new HashSet<MongoId>();
        foreach (var item in slottedItemsToProcess)
        {
            if (!itemSlots.TryGetValue(item.Id, out var slotsMap)) continue;
            foreach (var (slotName, filter) in slotsMap)
            {
                if (slotName.Contains("mod_") && !IsProprietarySlot(filter)) nonProprietaryItems.UnionWith(filter);
            }
        }
        proprietaryItems.ExceptWith(nonProprietaryItems);
        _reporter.V(() => $"Proprietary items: {string.Join(", ", proprietaryItems)}");

        List<MongoId> ModdedAmmoForCaliber(string caliber) =>
            caliberToAmmo.TryGetValue(caliber, out var ids)
                ? ids.Where(id => !IsVanillaPrefabId(id) && !_sets.Blacklist.Contains(id) && !proprietaryItems.Contains(id)).ToList()
                : [];

        // ── 1) 모드 탄 → 모드 무기 약실 ──
        foreach (var weapon in weaponsToProcess)
        {
            if (_sets.Blacklist.Contains(weapon.Id)) continue;
            var caliber = (weapon.Properties?.AmmoCaliber ?? "").ToLowerInvariant();
            if (caliber.Length == 0 || !caliberToAmmo.ContainsKey(caliber)) continue;

            var moddedAmmoForCaliber = ModdedAmmoForCaliber(caliber);
            foreach (var chamber in weapon.Properties?.Chambers ?? [])
            {
                if (!Util.TryGetFilter(chamber, out var filter)) continue;
                foreach (var ammoId in moddedAmmoForCaliber)
                {
                    if (filter.Add(ammoId))
                    {
                        result.ModifiedItems.Add(weapon.Id);
                        _reporter.V(() => $"Added modded ammo {_reporter.Name(ammoId)} to {_reporter.Name(weapon.Id)} chamber slot {chamber.Name}");
                        result.AmmoToChambers++;
                    }
                }
            }
        }

        // ── 2) 모드 탄 → 모드 탄창 ──
        foreach (var magazine in magazinesToProcess)
        {
            if (_sets.Blacklist.Contains(magazine.Id)) continue;
            foreach (var cartridge in magazine.Properties?.Cartridges ?? [])
            {
                if (!Util.TryGetFilter(cartridge, out var filter)) continue;

                var magazineCalibers = new HashSet<string>();
                foreach (var ammoId in filter)
                {
                    var c = (_items.TryGetValue(ammoId, out var a) ? a.Properties?.Caliber : null)?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(c)) magazineCalibers.Add(c);
                }

                foreach (var magCaliber in magazineCalibers)
                {
                    foreach (var ammoId in ModdedAmmoForCaliber(magCaliber))
                    {
                        if (filter.Add(ammoId))
                        {
                            result.ModifiedItems.Add(magazine.Id);
                            _reporter.V(() => $"Added modded ammo {_reporter.Name(ammoId)} to {_reporter.Name(magazine.Id)} cartridge slot {cartridge.Name}");
                            result.AmmoToCartridges++;
                        }
                    }
                }
            }
        }

        // ── 3) 바닐라 부품이 들어가는 칸에 그 복제본도 허용 ──
        foreach (var item in slottedItemsToProcess)
        {
            if (_sets.Blacklist.Contains(item.Id)) continue;
            if (!itemSlots.TryGetValue(item.Id, out var slotsMap)) continue;

            foreach (var (slotName, filter) in slotsMap)
            {
                if (IsProprietarySlot(filter) && !_sets.Whitelist.Contains(item.Id)) continue;

                var newAttachments = new List<MongoId>();
                foreach (var acceptedId in filter.ToList())
                {
                    if (!baseToClones.TryGetValue(acceptedId, out var clones)) continue;
                    foreach (var cloneId in clones)
                    {
                        if (_sets.Blacklist.Contains(cloneId) || proprietaryItems.Contains(cloneId)) continue;
                        if (!filter.Contains(cloneId)) newAttachments.Add(cloneId);
                    }
                }

                foreach (var newAttach in newAttachments)
                {
                    if (!filter.Add(newAttach)) continue;
                    result.ModifiedItems.Add(item.Id);
                    _reporter.V(() => $"Added modded attachment {_reporter.Name(newAttach)} to {_reporter.Name(item.Id)} slot {slotName}");
                    result.AttachmentsToSlots++;
                }
            }
        }

        // ── 4) 충돌 목록 상속 ──
        var conflictTargets = itemToBase.Where(kv => _items.ContainsKey(kv.Key) && _items.ContainsKey(kv.Value));
        var conflicts = ConflictHelper.Handle(_items, _config, _sets, baseToClones, _reporter, conflictTargets, result.ModifiedItems);
        result.BaseConflictsAdded = conflicts.BaseConflictsAdded;
        result.ClonedConflictsAdded = conflicts.ClonedConflictsAdded;
        result.ConflictsVoided = conflicts.ConflictsVoided;

        // ── 5) ManualAdd ──
        var manualAdd = new ManualAddProcessor(_items, _config, _sets, itemSlots, _isOf, _reporter, itemToBase, allItems);
        result.ManualAdditions = manualAdd.Run(result.ModifiedItems);

        return result;
    }

    private void BuildCloneMaps(List<TemplateItem> moddedList, Dictionary<MongoId, MongoId> itemToBase, Dictionary<MongoId, List<MongoId>> baseToClones)
    {
        foreach (var moddedItem in moddedList)
        {
            if (_resolver.FindBaseId(moddedItem) is not { } baseId || !_items.ContainsKey(baseId)) continue;

            itemToBase[moddedItem.Id] = baseId;
            if (!baseToClones.TryGetValue(baseId, out var clones)) baseToClones[baseId] = clones = [];
            clones.Add(moddedItem.Id);
        }
    }
}
