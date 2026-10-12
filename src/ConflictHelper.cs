using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace AutoCompatFramework;

public sealed class ConflictResults
{
    public int BaseConflictsAdded { get; set; }
    public int ClonedConflictsAdded { get; set; }
    public int ConflictsVoided { get; set; }
}

/// <summary>
/// "같이 못 다는 부품(ConflictingItems)" 목록을 복제본에도 물려주는 부분(원작 conflictHelper).
/// inheritBaseConflicts / inheritCloneConflicts 가 꺼져 있으면(기본값) 아무것도 추가하지 않는다.
/// </summary>
public static class ConflictHelper
{
    public static ConflictResults Handle(
        Dictionary<MongoId, TemplateItem> items,
        ModConfig config,
        ConfigSets sets,
        Dictionary<MongoId, List<MongoId>> baseToClones,
        Reporter reporter,
        IEnumerable<KeyValuePair<MongoId, MongoId>> moddedToBase,
        HashSet<MongoId> modifiedItems)
    {
        var results = new ConflictResults();

        foreach (var (moddedId, baseId) in moddedToBase)
        {
            if (sets.Blacklist.Contains(moddedId)) continue;
            if (!items.TryGetValue(moddedId, out var moddedItem) || !items.TryGetValue(baseId, out var baseItem)) continue;
            if (moddedItem.Properties is null || baseItem.Properties is null) continue;

            var baseConflicts = baseItem.Properties.ConflictingItems ?? [];
            var moddedConflicts = moddedItem.Properties.ConflictingItems;

            if (config.InheritBaseConflicts)
            {
                foreach (var conflictId in baseConflicts.ToList())
                {
                    if (sets.VoidConflicts.Contains(conflictId))
                    {
                        reporter.V(() => $"Skipped adding base conflict {reporter.Name(conflictId)} to modded item {reporter.Name(moddedId)}: in VoidConflicts");
                        results.ConflictsVoided++;
                        continue;
                    }

                    moddedConflicts ??= [];
                    if (moddedConflicts.Add(conflictId))
                    {
                        modifiedItems.Add(moddedId);
                        reporter.V(() => $"Added base conflict {reporter.Name(conflictId)} to modded item {reporter.Name(moddedId)}");
                        results.BaseConflictsAdded++;
                    }
                }
            }
            else
            {
                reporter.V(() => "Skipped base conflict inheritance due to inheritBaseConflicts: false");
            }

            if (config.InheritCloneConflicts)
            {
                foreach (var conflictId in baseConflicts.ToList())
                {
                    if (!baseToClones.TryGetValue(conflictId, out var clones)) continue;

                    foreach (var cloneId in clones.Where(id => !sets.Blacklist.Contains(id)).ToList())
                    {
                        if (sets.VoidConflicts.Contains(cloneId))
                        {
                            reporter.V(() => $"Skipped adding cloned conflict {reporter.Name(cloneId)} to modded item {reporter.Name(moddedId)}: in VoidConflicts");
                            results.ConflictsVoided++;
                            continue;
                        }

                        moddedConflicts ??= [];
                        if (moddedConflicts.Add(cloneId))
                        {
                            modifiedItems.Add(moddedId);
                            reporter.V(() => $"Added cloned conflict {reporter.Name(cloneId)} to modded item {reporter.Name(moddedId)}");
                            results.ClonedConflictsAdded++;
                        }
                    }
                }
            }
            else
            {
                reporter.V(() => "Skipped cloned conflict inheritance due to inheritCloneConflicts: false");
            }

            if (moddedConflicts is not null) moddedItem.Properties.ConflictingItems = moddedConflicts;
        }

        return results;
    }
}
