using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace AutoCompatFramework;

/// <summary>
/// 서버가 켜질 때 한 번 실행되는 진입점(원작의 postDBLoad 에 해당).
/// 아이템 등록은 Preload 에서 끝나므로, 그보다 한참 뒤(PostLoad + 100000)에 돌려서 다른 모드의 아이템·슬롯 수정이 모두 끝난 뒤 계산한다.
/// 아이템을 새로 만들지는 않고 기존 슬롯의 허용 목록만 늘리므로 DB 무결성 검사에 걸리지 않는다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100000)]
public class AutoCompatMod(
    ISptLogger<AutoCompatMod> logger,
    TemplateTable templates,
    LocaleTable locales,
    ItemHelper itemHelper,
    ModHelper modHelper,
    IReadOnlyList<SptMod> installedMods) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            // 이 모드가 실패해도 서버는 계속 켜져야 한다.
            logger.Error($"AutoCompatFramework: unexpected error, compatibility step skipped: {ex}");
        }
        return Task.CompletedTask;
    }

    private void Run()
    {
        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        // 설정은 로그 레벨(verbose)에 영향을 주므로 가장 먼저 읽는다.
        var startup = new Reporter(logger, false, null);
        startup.Colored("AutoCompatibility Framework: Beginning Calculations...", "CYAN");

        ModConfig config;
        try
        {
            var path = Path.Combine(modFolder, "config", "config.jsonc");
            config = JsonSerializer.Deserialize<ModConfig>(File.ReadAllText(path), ModConfig.ReadOptions) ?? new ModConfig();
        }
        catch (Exception ex)
        {
            logger.Error($"AutoCompatFramework: Failed to parse config.jsonc: {ex.Message}");
            return;
        }

        if (!config.Enabled)
        {
            logger.Info("AutoCompatFramework disabled in config.jsonc");
            return;
        }

        locales.Global.TryGetValue("en", out var english);
        var reporter = new Reporter(logger, config.VerboseLogging, english);

        var sets = ConfigSets.From(config);
        foreach (var bad in sets.Invalid) reporter.Warn($"AutoCompatFramework: ignored invalid ID in config ({bad})");

        VanillaItems vanilla;
        try
        {
            vanilla = LoadVanilla(Path.Combine(modFolder, "data", "vanillaItems.json"));
        }
        catch (Exception ex)
        {
            logger.Error($"AutoCompatFramework: Failed to load data/vanillaItems.json: {ex.Message}");
            return;
        }

        var items = templates.Items;

        CompatProcessor.NormalizeCalibers(items);

        var ownFolder = Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar);
        var otherMods = installedMods
            .Where(m => m.ModMetadata.ModGuid != ModMetadata.ModGuidValue &&
                        !string.Equals(Path.GetFullPath(m.Directory).TrimEnd(Path.DirectorySeparatorChar), ownFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var resolver = new BaseIdResolver(
            items,
            config,
            reporter,
            otherMods.Select(m => m.Directory).ToList(),
            Path.Combine(modFolder, "cache", "cache.json"),
            BaseIdResolver.BuildSignature(otherMods, items.Count));

        var processor = new CompatProcessor(items, config, sets, vanilla, (tpl, baseClass) => itemHelper.IsOfBaseclass(tpl, baseClass), reporter, resolver);
        var result = processor.Run();

        if (!config.VerboseLogging)
        {
            reporter.Info("AutoCompatFramework Summary:");
            reporter.Info($"- Added {result.AmmoToChambers} ammo to chambers");
            reporter.Info($"- Added {result.AmmoToCartridges} ammo to cartridges");
            reporter.Info($"- Added {result.AttachmentsToSlots} attachments to slots");
            reporter.Info($"- Added {result.BaseConflictsAdded} base conflicts");
            reporter.Info($"- Added {result.ClonedConflictsAdded} cloned conflicts");
            reporter.Info($"- Voided {result.ConflictsVoided} conflicts");
            reporter.Info($"- Added {result.ManualAdditions} manual additions");
        }
        else if (result.NothingChanged)
        {
            reporter.V(() => "No new compatibilities, conflicts, or manual additions added.");
        }

        reporter.Success("AutoCompatFramework: Mod Cross-compatibility applied successfully.");
        PrintRandomQuote(modFolder, reporter);
    }

    private static VanillaItems LoadVanilla(string path)
    {
        var vanilla = new VanillaItems();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Fill(doc.RootElement, "WEAPON", vanilla.Weapon);
        Fill(doc.RootElement, "MOD", vanilla.Mod);
        Fill(doc.RootElement, "AMMO", vanilla.Ammo);
        return vanilla;
    }

    private static void Fill(JsonElement root, string key, HashSet<MongoId> target)
    {
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var e in array.EnumerateArray())
        {
            if (IdParser.TryParse(e.GetString(), out var id)) target.Add(id);
        }
    }

    private static void PrintRandomQuote(string modFolder, Reporter reporter)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(modFolder, "data", "logMessages.json")));
            var messages = doc.RootElement.EnumerateArray().ToList();
            if (messages.Count == 0) return;
            var pick = messages[Random.Shared.Next(messages.Count)];
            reporter.Colored(pick.GetProperty("message").GetString() ?? "", pick.TryGetProperty("textColor", out var c) ? c.GetString() ?? "" : "");
        }
        catch (Exception ex)
        {
            reporter.Error($"AutoCompatFramework: Failed to load logMessages.json: {ex.Message}");
        }
    }
}
