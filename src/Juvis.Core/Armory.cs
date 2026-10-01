namespace Juvis.Core;

public sealed class Armory(LocalStore store, ApiClient api)
{
    public Catalog Catalog { get; private set; } = new();
    public UserState State { get; private set; } = new();
    public ApiClient Api => api;
    readonly SemaphoreSlim stateGate = new(1);
    readonly SemaphoreSlim catalogGate = new(1);
    public async Task Initialize(Func<Task<Catalog>> starter)
    {
        // Corrupt files deliberately surface an error instead of silently resetting user data.
        State = await store.Read<UserState>("state.json") ?? new();
        Catalog = await store.Read<Catalog>("catalog.json") ?? await starter();
    }
    public async Task Change(Action<UserState> change)
    {
        await stateGate.WaitAsync();
        try
        {
            var next = Backup.Parse(Backup.Export(State));
            change(next);
            await store.Write("state.json", next);
            State = next;
        }
        finally { stateGate.Release(); }
    }
    public Task Import(UserState incoming) => Change(s =>
    {
        var merged = Backup.Merge(s, incoming);
        s.Gear = merged.Gear; s.Blueprints = merged.Blueprints; s.Vehicles = merged.Vehicles;
        s.CraftPlan = merged.CraftPlan; s.Builds = merged.Builds; s.LastModule = merged.LastModule;
        s.StorageLocations = merged.StorageLocations; s.Inventory = merged.Inventory;
        s.TrackedLoadouts = merged.TrackedLoadouts; s.InventoryHistory = merged.InventoryHistory;
        s.ZeroHero = merged.ZeroHero;
    });
    public async Task Cache(Action<Catalog> update, string? sync = null)
    {
        await catalogGate.WaitAsync();
        try
        {
            var next = System.Text.Json.JsonSerializer.Deserialize<Catalog>(System.Text.Json.JsonSerializer.Serialize(Catalog, LocalStore.Json), LocalStore.Json)!;
            update(next);
            if (sync != null) next.Synced[sync] = DateTimeOffset.UtcNow;
            await store.Write("catalog.json", next);
            Catalog = next;
        }
        finally { catalogGate.Release(); }
    }
    public Task RememberItem(Item i) => Cache(c => { c.Items.RemoveAll(x => x.Id == i.Id); c.Items.Add(i); });
    public Task RememberVehicle(Vehicle v) => Cache(c => { c.Vehicles.RemoveAll(x => x.Id == v.Id); c.Vehicles.Add(v); });
    public Task RememberBlueprint(Blueprint b) => Cache(c => { c.Blueprints.RemoveAll(x => x.Id == b.Id); c.Blueprints.Add(b); });
    public async Task Sync(string source, IProgress<string> progress, CancellationToken ct)
    {
        switch (source)
        {
            case StarterGuide.Source:
                var guide = await api.LoadStarterGuide(ct);
                await Cache(c => c.Guide = guide, source);
                break;
            case "UEX items":
                var uex = await api.UexItems(progress, ct);
                await Cache(c => {
                    var wiki = c.Items.Where(i => i.Source == "Star Citizen Wiki").ToDictionary(i => i.Id);
                    c.Items = uex.Where(i => !wiki.ContainsKey(i.Id)).Concat(wiki.Values).ToList();
                }, source);
                break;
            case "Commodities":
                var commodities = (await api.Get(ApiClient.Uex + "commodities", ct)).Get("data").Array().Select(ApiParser.UexCommodity).ToList();
                if (commodities.Count == 0) throw new InvalidDataException("No commodities returned.");
                await Cache(c => c.Commodities = commodities, source);
                break;
            case "Wiki weapons & ammunition":
                var personalWeapons = await api.WikiPages("items?filter[type]=WeaponPersonal", ApiParser.WikiItem, progress, ct);
                var vehicleWeapons = await api.WikiPages("items?filter[type]=WeaponGun", ApiParser.WikiItem, progress, ct);
                var magazines = await api.WikiPages("items?filter[type]=WeaponAttachment&filter[sub_type]=Magazine", ApiParser.WikiItem, progress, ct);
                var weaponData = personalWeapons.Concat(vehicleWeapons).Concat(magazines).Where(i => i.Id.Length > 0).ToList();
                if (personalWeapons.Count == 0 || vehicleWeapons.Count == 0 || magazines.Count == 0)
                    throw new InvalidDataException("Wiki returned an incomplete weapon or magazine catalog.");
                await Cache(c => {
                    var map = c.Items.ToDictionary(i => i.Id);
                    foreach (var i in weaponData) map[i.Id] = i;
                    c.Items = map.Values.ToList();
                }, source);
                break;
            case "Vehicles":
                var vehicles = (await api.Get(ApiClient.Uex + "vehicles", ct)).Get("data").Array().Select(ApiParser.UexVehicle).ToList();
                if (vehicles.Count == 0) throw new InvalidDataException("No vehicles returned.");
                await Cache(c => c.Vehicles = vehicles.Select(v => v with { Ports = c.Vehicles.FirstOrDefault(x => x.Id == v.Id)?.Ports ?? [] }).ToList(), source);
                break;
            case "Blueprints":
                var blueprints = await api.WikiPages("blueprints", ApiParser.WikiBlueprint, progress, ct);
                if (blueprints.Count == 0) throw new InvalidDataException("No blueprints returned.");
                await Cache(c => c.Blueprints = blueprints.Select(b => PreserveMissionDetails(b, c.Blueprints.FirstOrDefault(x => x.Id == b.Id))).ToList(), source);
                break;
            case "Wiki components":
                var items = await api.WikiPages("items?filter[category]=vehicle-components", ApiParser.WikiItem, progress, ct);
                if (items.Count == 0) throw new InvalidDataException("No components returned.");
                await Cache(c => {
                    var map = c.Items.ToDictionary(i => i.Id);
                    foreach (var i in items) map[i.Id] = i;
                    c.Items = map.Values.ToList();
                }, source);
                break;
            default: throw new ArgumentException("Unknown sync module.");
        }
    }
    public static Blueprint PreserveMissionDetails(Blueprint incoming, Blueprint? cached) =>
        !incoming.MissionsChecked && cached is { MissionsChecked: true } && incoming.Version == cached.Version
            ? incoming with { Missions = cached.Missions, MissionsChecked = true }
            : incoming;
}
