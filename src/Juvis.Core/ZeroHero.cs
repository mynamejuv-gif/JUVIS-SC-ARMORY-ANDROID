namespace Juvis.Core;

public enum ResourceDisposition { Keep, Sell, PriorityKeep }
public enum PatchValidity { Live, Ptu, Outdated }

public sealed class ZeroHeroRunState
{
    public int RunNumber { get; set; } = 1;
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public HashSet<string> CompletedSteps { get; set; } = [];
    public Dictionary<string, decimal> ResourceInventory { get; set; } = [];
    public Dictionary<string, ResourceDisposition> ResourceChoices { get; set; } = [];
    public Dictionary<string, decimal> ContractNeeds { get; set; } = [];
    public HashSet<string> BasePreparationDone { get; set; } = [];
    public HashSet<string> RebuildQueue { get; set; } = [];
}

public record ZeroHeroStep(string Id, string Stage, string Title, string Description);
public record ZeroHeroResource(string Id, string Name, ResourceDisposition Recommendation, string Acquisition, string Use, string Version);
public record ZeroHeroBaseItem(string Id, string Title, string Description, string Version);
public record ResourcePlanRow(string Id, string Name, decimal Owned, decimal Needed, ResourceDisposition Disposition,
    string Acquisition, string Use, PatchValidity Validity, string Version)
{
    public decimal Missing => Math.Max(0, Needed - Owned);
}

public static class ZeroHero
{
    public const string LivePatch = "4.10.1";
    public const string PtuPatch = "4.10.2";
    public const string VerifiedDate = "1 October 2026";

    public static readonly IReadOnlyList<ZeroHeroStep> Steps =
    [
        new("stabilize", "01 · STABILIZE", "Secure the essentials", "Set a respawn point, equip a multi-tool, tractor attachment, medpens, food and a basic undersuit before risking cargo."),
        new("starter-income", "02 · EARN", "Build the first cash buffer", "Run low-risk contracts with pledge or starter equipment. Avoid tying the whole bankroll up in cargo."),
        new("mobility", "03 · MOBILITY", "Unlock a dependable work loop", "Rent or fit one focused platform for hauling, mining or salvage and keep replacement tools at your home location."),
        new("resource-loop", "04 · RESOURCES", "Keep what the plan consumes", "Track mined, salvaged and looted materials. Priority-keep anything required by active contracts or the craft plan."),
        new("crafting", "05 · CRAFT", "Recover blueprints and production", "Reacquire the exact blueprints you use, then build a materials list from the cached recipes before committing high-quality stock."),
        new("fleet", "06 · FLEET", "Rebuild proven loadouts", "Mark owned ships, recreate saved proposed builds and record fitted components so stock refreshes do not overwrite manual progress."),
        new("base-prep", "07 · BASE PREP", "Stage future construction supplies", "Keep a deliberate reserve, logistics ship and tools. Treat quantities and unavailable features as unverified until they are present in LIVE."),
    ];

    public static readonly IReadOnlyList<ZeroHeroResource> Resources =
    [
        new("pressurized-ice", "Pressurized Ice", ResourceDisposition.PriorityKeep, "Process mined Raw Ice through the current refining/crafting loop.", "Active crafting input; reserve it when a tracked recipe or contract needs it.", "4.10.1 LIVE"),
        new("hephaestanite", "Hephaestanite", ResourceDisposition.Keep, "Ship mining and refining.", "Crafting material; priority increases when it appears in the active craft plan.", "4.10.1 LIVE"),
        new("recycled-material-composite", "Recycled Material Composite", ResourceDisposition.Keep, "Salvage hull scraping.", "Salvage income and a practical construction/repair reserve.", "4.10.1 LIVE"),
        new("construction-materials", "Construction Materials", ResourceDisposition.Keep, "Salvage structural processing.", "Industrial stock and base-building preparation; confirm actual LIVE recipes before committing it.", "4.10.1 LIVE"),
        new("quantainium", "Quantainium", ResourceDisposition.Sell, "Volatile ship-mining deposits; refine promptly.", "High-value income unless an active recipe explicitly requires it.", "4.10.1 LIVE"),
        new("stileron", "Stileron", ResourceDisposition.Sell, "High-risk ship mining in Pyro/Nyx-era deposits.", "High-value income unless an active recipe explicitly requires it.", "4.10.1 LIVE"),
    ];

    public static readonly IReadOnlyList<ZeroHeroBaseItem> BasePreparation =
    [
        new("cargo", "Reserve cargo capacity", "Keep one dependable hauler and do not strand the entire reserve in a single load.", "4.10.1 LIVE"),
        new("salvage", "Salvage reserve", "Stage RMC and construction materials, but verify the actual recipe and quantity when base construction is available in LIVE.", "4.10.1 LIVE"),
        new("mining", "Mining reserve", "Retain crafting inputs from the active plan; sell surplus high-value ore to fund the rebuild.", "4.10.1 LIVE"),
        new("tools", "Tools and replacements", "Keep tractor, repair, medical and utility replacements at the chosen staging location.", "4.10.1 LIVE"),
        new("ptu-watch", "PTU watch", "4.10.2 is PTU-only. Do not treat PTU recipes, balance or persistence behavior as dependable LIVE requirements.", "4.10.2 PTU"),
    ];

    public static PatchValidity Validity(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return PatchValidity.Outdated;
        if (version.Contains(PtuPatch, StringComparison.OrdinalIgnoreCase) && version.Contains("PTU", StringComparison.OrdinalIgnoreCase)) return PatchValidity.Ptu;
        if (version.Contains(LivePatch, StringComparison.OrdinalIgnoreCase)) return PatchValidity.Live;
        return PatchValidity.Outdated;
    }

    public static string Key(string? id, string name)
    {
        var value = string.IsNullOrWhiteSpace(id) ? name : id;
        return new string(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
    }

    public static void NewRun(UserState state, DateTimeOffset now)
    {
        var next = Math.Max(1, state.ZeroHero?.RunNumber + 1 ?? 1);
        state.ZeroHero = new ZeroHeroRunState { RunNumber = next, StartedUtc = now };
    }

    public static List<ResourcePlanRow> ResourcePlan(Catalog catalog, UserState state)
    {
        var run = state.ZeroHero ?? new();
        var rows = new Dictionary<string, ResourcePlanRow>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, string name, decimal needed, ResourceDisposition defaultDisposition, string acquisition, string use, string version)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var named = rows.FirstOrDefault(x => x.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var key = string.IsNullOrEmpty(named.Key) ? Key(id, name) : named.Key;
            if (rows.TryGetValue(key, out var old)) needed += old.Needed;
            var disposition = run.ResourceChoices.GetValueOrDefault(key,
                needed > 0 ? ResourceDisposition.PriorityKeep : defaultDisposition);
            rows[key] = new(key, name, run.ResourceInventory.GetValueOrDefault(key), needed, disposition,
                acquisition, use, Validity(version), version);
        }

        foreach (var resource in Resources)
            Add(resource.Id, resource.Name, 0, resource.Recommendation, resource.Acquisition, resource.Use, resource.Version);

        foreach (var commodity in catalog.Commodities.Where(c => CatalogPresentation.CommodityName(c) != null))
            Add(commodity.Id, CatalogPresentation.CommodityName(commodity)!, 0, ResourceDisposition.Sell,
                "Check the current mining, salvage, loot or market source.", "No active requirement in this run.", "");

        foreach (var ingredient in Crafting.Requirements(catalog.Blueprints, state.CraftPlan).Where(i => CatalogPresentation.HasName(i.Name)))
            Add(ingredient.Id, ingredient.Name, ingredient.Quantity, ResourceDisposition.PriorityKeep,
                "Mine, salvage, loot or trade according to the exact resource.", "Required by the active blueprint craft plan.",
                catalog.Blueprints.FirstOrDefault(b => b.Ingredients.Any(i => i.Id == ingredient.Id))?.Version ?? "");

        foreach (var requirement in run.ContractNeeds.Where(x => x.Value > 0))
        {
            var known = Resources.FirstOrDefault(r => Key(r.Id, r.Name).Equals(requirement.Key, StringComparison.OrdinalIgnoreCase));
            Add(requirement.Key, known?.Name ?? requirement.Key.Replace('-', ' '), requirement.Value,
                ResourceDisposition.PriorityKeep, known?.Acquisition ?? "Confirm the source shown by the contract.",
                "Required by a tracked contract.", known?.Version ?? $"{LivePatch} LIVE");
        }

        return rows.Values.OrderByDescending(r => r.Disposition == ResourceDisposition.PriorityKeep)
            .ThenByDescending(r => r.Missing > 0).ThenBy(r => r.Name).ToList();
    }
}
