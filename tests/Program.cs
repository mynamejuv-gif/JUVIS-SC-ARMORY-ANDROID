using System.Net;
using System.Text.Json;
using Juvis.Core;

if (args.Length == 2 && args[0] == "--performance-snapshot")
{
    var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(args[1]),LocalStore.Json)!;
    var map=catalog.Items.ToDictionary(i=>i.Id);
    foreach(var name in new[]{"performance-weapons-1.json","performance-weapons-2.json"})
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",name)));
        foreach(var row in doc.RootElement.Get("data").Array()) {var snapshotItem=ApiParser.WikiItem(row);if(CatalogPresentation.ItemName(snapshotItem)!=null)map[snapshotItem.Id]=snapshotItem;}
    }
    catalog.Items=map.Values.ToList();
    using var vehicleDoc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","loadout-gladius.json")));
    var vehicle=ApiParser.WikiVehicle(vehicleDoc.RootElement.Get("data"));
    catalog.Vehicles.RemoveAll(v=>v.Id==vehicle.Id);catalog.Vehicles.Add(vehicle);
    File.WriteAllText(args[1],JsonSerializer.Serialize(catalog,LocalStore.Json));
    Console.WriteLine($"Bundled {catalog.Items.Count} items; {catalog.Items.Count(i=>i.Performance.HasData)} with weapon performance.");
    return;
}

if (args.Length == 4 && args[0] == "--enrich-weapons")
{
    var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(args[1]), LocalStore.Json) ?? throw new InvalidDataException("Starter catalog is empty.");
    var enrichStore = new LocalStore(args[2]);
    using var enrichClient = new HttpClient();
    var enrichApp = new Armory(enrichStore, new ApiClient(enrichClient));
    await enrichApp.Initialize(() => Task.FromResult(catalog));
    await enrichApp.Sync("Wiki weapons & ammunition", new Progress<string>(Console.WriteLine), default);
    catalog = enrichApp.Catalog;
    catalog.Items = catalog.Items.Where(i => CatalogPresentation.ItemName(i) != null)
        .Select(i => i with { Name = CatalogPresentation.ItemName(i)!, Manufacturer = CatalogPresentation.Name(i.Manufacturer) ?? "" }).ToList();
    catalog.Commodities = catalog.Commodities.Where(c => CatalogPresentation.CommodityName(c) != null)
        .Select(c => c with { Name = CatalogPresentation.CommodityName(c)! }).ToList();
    catalog.Blueprints = catalog.Blueprints.Where(b => CatalogPresentation.BlueprintName(b) != null)
        .Select(b => b with { Name = CatalogPresentation.BlueprintName(b)!, Ingredients = b.Ingredients.Where(i => CatalogPresentation.HasName(i.Name)).ToList(), Missions = b.Missions.Where(CatalogPresentation.HasName).ToList() }).ToList();
    catalog.Vehicles = catalog.Vehicles.Where(v => CatalogPresentation.VehicleName(v) != null).Select(v => v with {
        Name = CatalogPresentation.VehicleName(v)!, Manufacturer = CatalogPresentation.Name(v.Manufacturer) ?? "",
        Ports = v.Ports.Select(p => p.Installed != null && CatalogPresentation.ItemName(p.Installed) == null ? p with { Installed = null } : p).ToList()
    }).ToList();
    File.WriteAllText(args[3], JsonSerializer.Serialize(catalog, LocalStore.Json));
    var weapons = catalog.Items.Where(WeaponPresentation.IsWeapon).OrderBy(i => CatalogPresentation.ItemName(i)).ToList();
    var applicable = weapons.Where(i => !WeaponPresentation.Ammunition(i, catalog.Items).NotApplicable).ToList();
    var unknown = applicable.Where(i => !WeaponPresentation.Ammunition(i, catalog.Items).HasData).ToList();
    Console.WriteLine($"WEAPON AUDIT: {weapons.Count} displayable weapon-category items; {applicable.Count} ammunition-applicable; {applicable.Count - unknown.Count} with ammunition data; {unknown.Count} unavailable.");
    foreach (var weapon in unknown) Console.WriteLine("AMMO UNAVAILABLE: " + CatalogPresentation.ItemName(weapon));
    return;
}

if (args.Length == 2 && args[0] == "--guide-live")
{
    using var client = new HttpClient();
    var app = new Armory(new LocalStore(args[1]), new ApiClient(client));
    await app.Initialize(() => Task.FromResult(new Catalog()));
    await app.Sync(StarterGuide.Source, new Progress<string>(Console.WriteLine), default);
    Console.WriteLine($"LIVE PASS {app.Catalog.Guide.Blueprints.Count} guide records, source build {app.Catalog.Guide.Version}");
    foreach (var name in new[] { "A03 Sniper Rifle", "A03 \"Canuto\" Sniper Rifle" })
        Console.WriteLine($"{name}: {StarterGuide.Match(app.Catalog.Guide, name)?.Missions.Count} mission reports");
    return;
}

if (args.Length == 2 && args[0] == "--live")
{
    using var client = new HttpClient();
    var app = new Armory(new LocalStore(args[1]), new ApiClient(client));
    await app.Initialize(() => Task.FromResult(new Catalog()));
    foreach (var source in new[] { "Commodities", "Vehicles", "Blueprints", "Wiki components", "UEX items", "Wiki weapons & ammunition" })
    {
        if (app.Catalog.Synced.ContainsKey(source)) { Console.WriteLine("Already verified " + source); continue; }
        await app.Sync(source, new Progress<string>(Console.WriteLine), default);
        Console.WriteLine($"LIVE PASS {source}: {app.Catalog.Items.Count} items, {app.Catalog.Blueprints.Count} recipes, {app.Catalog.Vehicles.Count} vehicles, {app.Catalog.Commodities.Count} commodities");
    }
    return;
}

if (args.Length == 3 && args[0] == "--starter")
{
    var dir = args[1];
    JsonElement Root(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file))).RootElement.Clone();
    var wikiItems = Root("items.json").Get("data").Array().Select(ApiParser.WikiItem).ToList();
    var uexItems = Root("uex-items.json").Get("data").Array().Select(ApiParser.UexItem).ToList();
    var vehicle = ApiParser.WikiVehicle(Root("vehicle.json").Get("data"));
    var blueprint = ApiParser.WikiBlueprint(Root("blueprint-detail.json").Get("data"));
    var recipes = Root("blueprints.json").Get("data").Array().Select(ApiParser.WikiBlueprint).ToList();
    recipes.RemoveAll(b => b.Id == blueprint.Id); recipes.Add(blueprint);
    var catalog = new Catalog {
        Items = wikiItems.Concat(uexItems).Concat(vehicle.Ports.Where(p => p.Editable == true && p.Installed != null).Select(p => p.Installed!)).Where(i => i.Id.Length > 0 && i.Name.Length > 0 && !i.Name.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)).DistinctBy(i => i.Id).ToList(),
        Blueprints = recipes, Commodities = Root("commodities.json").Get("data").Array().Select(ApiParser.UexCommodity).ToList(), Vehicles = [vehicle]
    };
    Directory.CreateDirectory(Path.GetDirectoryName(args[2])!);
    File.WriteAllText(args[2], JsonSerializer.Serialize(catalog, LocalStore.Json));
    Console.WriteLine($"Starter: {catalog.Items.Count} items, {catalog.Commodities.Count} commodities, {catalog.Blueprints.Count} recipes, {vehicle.Ports.Count} ports");
    return;
}

int passed = 0;
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Expected invalid data rejection"); }
var item = new Item { Id = "i", Name = "Test shield", Type = "Shield", SubType = "Shield", Size = 2, RestrictionsKnown = true, Version = "4.10", Tags = ["standard"] };
var port = new Port("p", "Shield port", true, 2, 2, [new("Shield", ["Shield"])], ["standard"], [], null, "4.10");
Test("Exact port restrictions match", () => Check(Compatibility.Check(port, item).Fit == Fit.Direct));
Test("Size alone cannot authorize a mount", () => Check(Compatibility.Check(port, item with { Type = "Cooler" }).Fit == Fit.Incompatible));
Test("Fixed ports are never editable", () => Check(Compatibility.Check(port with { Editable = false }, item).Fit == Fit.Fixed));
Test("Unknown editability is conservative", () => Check(Compatibility.Check(port with { Editable = null }, item).Fit == Fit.CheckRequired));
Test("Wrong size rejected", () => Check(Compatibility.Check(port, item with { Size = 3 }).Fit == Fit.Incompatible));
Test("Missing size requires review", () => Check(Compatibility.Check(port, item with { Size = null }).Fit == Fit.CheckRequired));
Test("Wrong subtype rejected", () => Check(Compatibility.Check(port, item with { SubType = "Other" }).Fit == Fit.Incompatible));
Test("Undefined subtype requires review", () => Check(Compatibility.Check(port, item with { SubType = "UNDEFINED" }).Fit == Fit.CheckRequired));
Test("Required tags checked", () => Check(Compatibility.Check(port, item with { Tags = [] }).Fit == Fit.Incompatible));
Test("Item mount restrictions checked", () => Check(Compatibility.Check(port, item with { RequiredTags = ["special"] }).Fit == Fit.Incompatible));
Test("Unknown restrictions cannot be confirmed", () => Check(Compatibility.Check(port, item with { RestrictionsKnown = false }).Fit == Fit.CheckRequired));
Test("Cross patch fit is not confirmed", () => Check(Compatibility.Check(port, item with { Version = "4.9" }).Fit == Fit.CheckRequired));
var state = new UserState { Gear = new() { ["i"] = new(true, true, true, "Shield") }, Blueprints = ["b"], Vehicles = ["v"], CraftPlan = new() { ["b"] = 3 }, Builds = new() { ["v"] = [new("p", "i", "Shield", "4.10")] }, LastModule = "Craft" };
Test("Backup round trip preserves every module", () => {
    var s = Backup.Parse(Backup.Export(state)); Check(s.Gear["i"].Favorite && s.Gear["i"].Need && s.Gear["i"].Owned && s.Blueprints.Contains("b") && s.Vehicles.Contains("v") && s.CraftPlan["b"] == 3 && s.Builds["v"][0].ItemId == "i" && s.LastModule == "Craft");
});
Test("Backup excludes tokens and catalog", () => Check(!Backup.Export(state).Contains("Token") && !Backup.Export(state).Contains("Catalog")));
Test("Malformed backup rejected", () => Reject(() => Backup.Parse("{}")));
Test("Future schema rejected", () => Reject(() => Backup.Parse(Backup.Export(state).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"))));
Test("Negative recipe quantity rejected", () => { var s = new UserState { CraftPlan = new() { ["b"] = -1 } }; Reject(() => Backup.Parse(Backup.Export(s))); });
Test("Merge retains unrelated state", () => { var s = Backup.Merge(state, new UserState { Gear = new() { ["j"] = new(true) } }); Check(s.Gear.Count == 2 && s.CraftPlan["b"] == 3); });
Test("Craft totals preserve fractional SCU and distinct units", () => {
    var b = new Blueprint("b", "Test", "4.10", 60, false, [new("r", "Ore", .36m, "SCU"), new("r", "Ore", 7, "units")], [], "");
    var totals = Crafting.Requirements([b], state.CraftPlan); Check(totals.Count == 2 && totals.Single(i => i.Unit == "SCU").Quantity == 1.08m && totals.Single(i => i.Unit == "units").Quantity == 21);
});
Test("Nested ports get distinct stable paths", () => {
    using var d = JsonDocument.Parse("""{"uuid":"v","name":"V","ports":[{"name":"left","editable":false,"ports":[{"name":"gun","editable":true}]},{"name":"right","ports":[{"name":"gun","editable":true}]}]}""");
    var v = ApiParser.WikiVehicle(d.RootElement); Check(v.Ports.Count == 4 && v.Ports.Any(p => p.Id == "left/gun") && v.Ports.Any(p => p.Id == "right/gun"));
});
Test("Live API fixture parses real recipes", () => {
    using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blueprint.json")));
    var b = ApiParser.WikiBlueprint(d.RootElement); Check(b.Name == "Omnisky III Cannon" && b.Ingredients.Any(i => i.Quantity == .36m && i.Unit == "SCU") && b.Missions.Count > 0);
});
Test("Live API fixture parses shield tags and images", () => {
    using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "item.json")));
    var i = ApiParser.WikiItem(d.RootElement); Check(i.Type == "Shield" && i.RestrictionsKnown && i.ImageUrl.StartsWith("https://") && i.Version.Length > 0);
});
var temp = Path.Combine(Path.GetTempPath(), "juvis-tests-" + Guid.NewGuid().ToString("N"));
var store = new LocalStore(temp);
await store.Write("state.json", state);
Test("Atomic state persisted", () => Check(store.Read<UserState>("state.json").Result!.Gear["i"].Owned && !File.Exists(Path.Combine(temp, "state.json.tmp"))));
var fake = new FakeHandler();
using var http = new HttpClient(fake);
var api = new ApiClient(http) { UexToken = "test-token" };
fake.Responses.Enqueue("""{"data":[{"uuid":"a"}],"links":{"next":"https://api.star-citizen.wiki/api/items?page=2"}}""");
fake.Responses.Enqueue("""{"data":[{"uuid":"b"}],"links":{"next":null}}""");
var pages = await api.WikiPages("items", e => e.S("uuid"), null, default);
Test("Pagination follows all pages", () => Check(pages.SequenceEqual(new[] { "a", "b" })));
Test("UEX token is not sent to Wiki", () => Check(fake.Headers.All(h => h == null)));
fake.Responses.Enqueue("""{"data":[{"uuid":"a"}],"meta":{"current_page":1,"last_page":2},"links":{"next":"https://api.star-citizen.wiki/api/items?page[number]=1&page[number]=2"}}""");
fake.Responses.Enqueue("""{"data":[{"uuid":"b"}],"meta":{"current_page":2,"last_page":2,"total":2}}""");
var normalizedPages = await api.WikiPages("items", e => e.S("uuid"), null, default);
Test("Duplicate page parameter links normalized", () => Check(normalizedPages.SequenceEqual(new[] { "a", "b" }) && fake.Urls.Last().EndsWith("&page[number]=2")));
fake.Responses.Enqueue("""{"data":[{"uuid":"a"}],"meta":{"current_page":1,"last_page":1,"total":5}}""");
try { await api.WikiPages("items", e => e.S("uuid"), null, default); throw new Exception("Incomplete response accepted"); } catch (InvalidDataException) { passed++; Console.WriteLine("PASS Incomplete catalog count rejected"); }
fake.Responses.Enqueue("""{"status":"requires_id_category","data":[]}""");
try { await api.Get(ApiClient.Uex + "items", default); throw new Exception("API error accepted"); } catch (InvalidDataException) { passed++; Console.WriteLine("PASS UEX application errors rejected"); }
fake.Responses.Enqueue("""{"status":"ok","data":null}""");
var emptyCategory = await api.Get(ApiClient.Uex + "items?id_category=11", default);
Test("UEX successful empty category is accepted", () => Check(!emptyCategory.Get("data").Array().Any()));
var armory = new Armory(store, api); await armory.Initialize(() => Task.FromResult(new Catalog { Items = [item] }));
fake.Responses.Enqueue("""{"status":"error","data":[]}""");
try { await armory.Sync("Commodities", new Progress<string>(), default); } catch (InvalidDataException) { }
Test("Failed sync preserves cache and states", () => Check(armory.Catalog.Items.Count == 1 && armory.State.Gear["i"].Owned));
await Task.WhenAll(Enumerable.Range(0, 10).Select(n => armory.Change(s => s.Gear["n" + n] = new(true))));
Test("Concurrent state writes retain all changes", () => Check(armory.State.Gear.Count == 11));
Test("Bundled UUID image resolves without a network URL", () => {
    var index = new BundledImageIndex(new() { ["be9729fb-c973-4c49-93cb-6a6f7c877408"] = "be9729fb-c973-4c49-93cb-6a6f7c877408.webp" });
    Check(index.AssetFor("BE9729FB-C973-4C49-93CB-6A6F7C877408") == "bundled-images/be9729fb-c973-4c49-93cb-6a6f7c877408.webp");
    Check(index.AssetFor("unknown") is null);
});
Test("Bundled commodities use a separate ID namespace", () => {
    var index = new BundledImageIndex(new() { ["uex:commodity:1"] = "commodities/commodity-1.png" });
    Check(index.AssetFor("uex:commodity:1") == "bundled-images/commodities/commodity-1.png" && index.AssetFor("1") is null);
});
Test("Bundled index rejects traversal and mismatched IDs", () => {
    Reject(() => new BundledImageIndex(new() { ["uex:commodity:1"] = "../commodity-1.png" }));
    Reject(() => new BundledImageIndex(new() { ["uex:commodity:1"] = "commodities/commodity-2.png" }));
    Check(BundledImageIndex.KeyForFile("commodities/../../outside.png") is null);
});
Test("Commodity image key survives UUID-backed records", () => {
    using var d = JsonDocument.Parse("""{"id":1,"uuid":"some-game-uuid","name":"Agricium"}""");
    var c = ApiParser.UexCommodity(d.RootElement); Check(c.Id == "some-game-uuid" && c.ImageKey == "uex:commodity:1");
});
Test("Blueprint nested output supplies a missing or placeholder name", () => {
    foreach (var name in new[] { "", "<= PLACEHOLDER =>" }) {
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(new { uuid = "recipe-id", output_name = name, output = new { name = "Omnisky III Cannon" } }));
        Check(ApiParser.WikiBlueprint(d.RootElement).Name == "Omnisky III Cannon");
    }
});
Test("Incomplete blueprints are hidden without changing saved IDs", () => {
    foreach (var name in new[] { "", " \t", "<= PLACEHOLDER =>" }) {
        var b = new Blueprint("recipe-id", name, "", 10, false, [], [], "");
        Check(!CatalogPresentation.HasName(b.Name));
        Check(CatalogPresentation.BlueprintName(b) == null);
        Check(b.Id == "recipe-id" && b.Name == name);
    }
    Check(CatalogPresentation.HasName("Placeholder Rifle") && CatalogPresentation.HasName("Omnisky III Cannon"));
});
var starterCatalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "starter.json")), LocalStore.Json)!;
Test("Bundled catalog has only displayable record names", () => {
    Check(starterCatalog.Items.All(i => CatalogPresentation.ItemName(i) != null));
    Check(starterCatalog.Commodities.All(c => CatalogPresentation.CommodityName(c) != null));
    Check(starterCatalog.Blueprints.All(b => CatalogPresentation.BlueprintName(b) != null));
    Check(starterCatalog.Vehicles.All(v => CatalogPresentation.VehicleName(v) != null));
    Check(starterCatalog.Vehicles.SelectMany(v => v.Ports).Where(p => p.Installed != null).All(p => CatalogPresentation.ItemName(p.Installed!) != null));
});
Test("Bundled weapon ammunition coverage is complete for applicable records", () => {
    var weapons = starterCatalog.Items.Where(WeaponPresentation.IsWeapon).ToList();
    var applicable = weapons.Select(i => WeaponPresentation.Ammunition(i, starterCatalog.Items)).Where(a => !a.NotApplicable).ToList();
    Check(weapons.Count == 586 && applicable.Count == 524 && applicable.All(a => a.HasData));
});
Test("Bundled vehicle retains a confirmed slot-compatible upgrade", () => {
    var vehicle = starterCatalog.Vehicles.Single(v => v.Name == "Cutlass Black");
    var kozane = starterCatalog.Items.Single(i => CatalogPresentation.ItemName(i) == "6MA 'Kozane'");
    var shield = vehicle.Ports.First(p => p.Editable == true && p.Types.Any(t => t.Type == "Shield"));
    Check(Compatibility.Check(shield, kozane).Fit == Fit.Direct);
    Check(!CatalogPresentation.PortName(shield).Contains("hardpoint", StringComparison.OrdinalIgnoreCase));
});
Test("Name resolver rejects internal values and uses trustworthy alternatives", () => {
    foreach (var bad in new[] { "PLACEHOLDER", "<= PLACEHOLDER =>", "< UNINITIALIZED >", "hardpoint_left_weapon", "01234567-89ab-cdef-0123-456789abcdef", "item_weapon_debug" })
        Check(!CatalogPresentation.HasName(bad), bad);
    var named = new Item { Name = "<= PLACEHOLDER =>", AlternativeNames = ["P4-AR Rifle"] };
    var modeled = new Item { Name = "", Manufacturer = "Behring", Model = "FS-9" };
    Check(CatalogPresentation.ItemName(named) == "P4-AR Rifle" && CatalogPresentation.ItemName(modeled) == "Behring FS-9");
});
Test("Hidden names do not remove persistent state from backups", () => {
    var hidden = new UserState { Gear = new() { ["stable-id"] = new(true, false, false, "<= PLACEHOLDER =>") } };
    Check(Backup.Parse(Backup.Export(hidden)).Gear["stable-id"].Owned);
});
Test("Weapon ammunition parser uses structured fields and magazine relationships", () => {
    using var d = JsonDocument.Parse("""{"uuid":"weapon","name":"P4-AR Rifle","type":"WeaponPersonal","personal_weapon":{"class":"Ballistic","magazine_size":40},"ammunition":{"capacity":40},"ports":[{"name":"magazine_attach","display_name":"Magazine","sizes":{"min":1,"max":1},"compatible_types":[{"type":"WeaponAttachment","sub_types":["Magazine"]}],"required_tags":["p4_mag"],"port_tags":["p4_mag"],"equipped_item_uuid":"mag","equipped_item":{"uuid":"mag","name":"P4-AR Magazine (40 cap)"}}],"description_data":[{"name":"Caliber","value":"5.56 mm"}]}""");
    var weapon = ApiParser.WikiItem(d.RootElement);
    var magazine = new Item { Id = "mag", Name = "P4-AR Magazine (40 cap)", Type = "WeaponAttachment", SubType = "Magazine", Size = 1, Tags = ["p4_mag"], RestrictionsKnown = true, Ammunition = new() { Caliber = "5.56 mm", Capacity = 40 } };
    var ammo = WeaponPresentation.Ammunition(weapon, [weapon, magazine]);
    Check(ammo.AmmoType == "Ballistic" && ammo.Caliber == "5.56 mm" && ammo.Capacity == 40 && ammo.CompatibleMagazines.SequenceEqual(new[] { "P4-AR Magazine (40 cap)" }));
    Check(ammo.SearchText.Contains("5.56 mm") && !ammo.SearchText.Contains("stable-id"));
});
Test("Energy weapon ammunition reports capacitor data separately", () => {
    using var d = JsonDocument.Parse("""{"uuid":"laser","name":"Laser Repeater","type":"WeaponGun","vehicle_weapon":{"type":"Laser Repeater","capacitor":{"max_ammo_load":75,"regen_per_second":15}},"ammunition":{"capacity":0,"impact_damage":[{"name":"Energy","damage":42}]}}""");
    var ammo = ApiParser.WikiItem(d.RootElement).Ammunition;
    Check(ammo.AmmoType == "Energy" && ammo.EnergyCapacity == 75 && ammo.EnergyRegenerationPerSecond == 15 && ammo.Capacity == null);
});
Test("Category aliases unite component categories without changing source records", () => {
    Check(CatalogPresentation.Category("Cooler") == CatalogPresentation.Category("Coolers"));
    Check(CatalogPresentation.Category("Power") == "Power Plants");
    Check(CatalogPresentation.Category("Quantum Drive") == "Quantum Drives");
    Check(CatalogPresentation.Category("Shield") == "Shield Generators");
    Check(CatalogPresentation.Category("Personal Weapons") == "Personal Weapons");
});
fake.Responses.Enqueue("""{"data":{"uuid":"wiki-item-id","name":"Test item"}}""");
var refreshedItem = await api.LoadItem(new Item { Id = "uex:item:42", Name = "Test item" }, default);
Test("Wiki item refresh preserves the key used by gear states", () => Check(refreshedItem.Id == "uex:item:42"));
fake.Responses.Enqueue("""{"data":{"uuid":"wiki-vehicle-id","name":"Test vehicle","ports":[{"name":"shield"}]}}""");
var refreshedVehicle = await api.LoadVehicle(new Vehicle("uex:vehicle:42", "Test vehicle", "", false, "", "", []), default);
Test("Wiki vehicle refresh preserves ownership and proposed-build keys", () => Check(refreshedVehicle.Id == "uex:vehicle:42" && refreshedVehicle.Ports.Count == 1));
Test("API request and streamed body never use the caller thread", () => {
    using var handler = new ThreadGuardHandler(Environment.CurrentManagedThreadId, false);
    using var client = new HttpClient(handler);
    var result = new ApiClient(client).Get(ApiClient.Wiki + "items/test", default).GetAwaiter().GetResult();
    Check(result.Get("data").S("name") == "Network test" && handler.Reads > 0);
});
Test("Image download and cache hit avoid caller-thread transport", () => {
    var folder = Path.Combine(Path.GetTempPath(), "juvis-image-thread-" + Guid.NewGuid());
    try {
        using var handler = new ThreadGuardHandler(Environment.CurrentManagedThreadId, true);
        using var client = new HttpClient(handler);
        var cache = new ImageCache(folder, client);
        var path = cache.Get("https://example.com/test.png", default).GetAwaiter().GetResult();
        Check(path != null && File.Exists(path) && handler.Reads > 0);
        Check(cache.Get("https://example.com/test.png", default).GetAwaiter().GetResult() == path && handler.Requests == 1);
    } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
fake.StatusCode = HttpStatusCode.NotFound;
fake.Responses.Enqueue("{}");
try { await api.LoadItem(new Item { Id = "uex:item:2624", Name = "ADP Arms" }, default); throw new Exception("Expected missing Wiki record"); }
catch (HttpRequestException ex) {
    Test("Missing Wiki record explains retained data without suggesting a UEX token", () =>
        Check(ex.StatusCode == HttpStatusCode.NotFound && ex.Message.Contains("No matching Wiki record") && !ex.Message.Contains("token")));
}
var guideJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "starter-guide.json"));
using var guideDoc = JsonDocument.Parse(guideJson);
var guideData = StarterGuide.Parse(guideDoc.RootElement);
Test("Guide source parses faction, system and reputation separately from recipes", () => {
    var baseItem = StarterGuide.Match(guideData, "A03 Sniper Rifle")!;
    Check(baseItem.Missions.Count == 2 && baseItem.Missions[1].Faction == "Citizens For Prosperity" && baseItem.Missions[1].System == "Pyro" && baseItem.Missions[1].MinReputation == 800);
});
Test("Guide matching never substitutes base A03 for Canuto", () => {
    Check(StarterGuide.Match(guideData, "A03 \"Canuto\" Sniper Rifle")!.Missions.Count == 0);
    Check(StarterGuide.Match(guideData, "A03 Sniper Rifle", "unknown-key") == null);
    Check(StarterGuide.Match(guideData, "Canuto") == null);
    var exact = guideData.Blueprints.First();
    Check(StarterGuide.Match(guideData, "Different display name", exact.Key) == exact);
});
Test("Ambiguous guide names require a blueprint identity", () => {
    var a = new GuideBlueprint("a", "Same name", 1, []);
    var b = a with { Key = "b" };
    Check(StarterGuide.Match(new("test", [a,b]), "Same name") == null);
    Check(StarterGuide.Match(new("test", [a,b]), "Same name", "b") == b);
});
Test("Incomplete guide responses are rejected", () => {
    foreach (var json in new[] { "{}", "{\"_build\":\"test\",\"items\":[]}", "{\"_build\":\"test\",\"items\":[{\"blueprint\":\"a\",\"name\":\"A\"}]}" }) {
        using var doc = JsonDocument.Parse(json); Reject(() => StarterGuide.Parse(doc.RootElement));
    }
});
Test("Wiki distinguishes an unchecked list from a checked empty mission response", () => {
    using var list = JsonDocument.Parse("{\"uuid\":\"b\",\"key\":\"key\"}");
    using var detail = JsonDocument.Parse("{\"uuid\":\"b\",\"key\":\"key\",\"unlocking_missions\":[]}");
    Check(!ApiParser.WikiBlueprint(list.RootElement).MissionsChecked && ApiParser.WikiBlueprint(detail.RootElement).MissionsChecked);
    Check(ApiParser.WikiBlueprint(detail.RootElement).Key == "key");
});
Test("Mission refresh clears removed unlocks and does not carry them across patches", () => {
    var old = new Blueprint("b", "B", "patch-1", 10, false, [], ["Old mission"], "") { MissionsChecked = true };
    var list = old with { Missions = [], MissionsChecked = false };
    Check(Armory.PreserveMissionDetails(list, old).Missions.Count == 1);
    Check(Armory.PreserveMissionDetails(list with { Version = "patch-2" }, old).Missions.Count == 0);
    Check(Armory.PreserveMissionDetails(list with { MissionsChecked = true }, old).Missions.Count == 0);
});
fake.StatusCode = HttpStatusCode.OK;
fake.Responses.Enqueue(guideJson);
await armory.Sync(StarterGuide.Source, new Progress<string>(), default);
var savedGuideDate = armory.Catalog.Synced[StarterGuide.Source];
Test("Guide sync persists offline and excludes the UEX token", () => {
    Check(fake.Headers.Last() == null && fake.Urls.Last() == StarterGuide.DataUrl);
    Check(store.Read<Catalog>("catalog.json").GetAwaiter().GetResult()!.Guide.Blueprints.Count == 2);
});
fake.Responses.Enqueue("{}");
try { await armory.Sync(StarterGuide.Source, new Progress<string>(), default); throw new Exception("Invalid guide accepted"); }
catch (InvalidDataException) {
    Test("Failed guide sync preserves records, timestamp and user states", () =>
        Check(armory.Catalog.Guide.Blueprints.Count == 2 && armory.Catalog.Synced[StarterGuide.Source] == savedGuideDate && armory.State.Gear["i"].Owned));
}
Test("Sync all continues after failures and runs every source once", () => {
    var seen = new List<string>();
    var report = SyncCoordinator.Run((source, progress, ct) => {
        seen.Add(source);
        if (source == "Commodities") throw new HttpRequestException("Offline");
        if (source == "Vehicles") throw new TaskCanceledException("Timeout");
        return Task.CompletedTask;
    }, new Progress<string>(), default).GetAwaiter().GetResult();
    Check(seen.SequenceEqual(SyncCoordinator.Sources) && report.Updated.Count == SyncCoordinator.Sources.Count - 2 && report.Failed.Count == 2 && !report.Cancelled);
});
Test("Sync all cancellation retains completed results and skips remaining sources", () => {
    using var cancel = new CancellationTokenSource();
    var seen = new List<string>();
    var report = SyncCoordinator.Run((source, progress, ct) => {
        seen.Add(source);
        if (seen.Count == 2) { cancel.Cancel(); ct.ThrowIfCancellationRequested(); }
        return Task.CompletedTask;
    }, new Progress<string>(), cancel.Token).GetAwaiter().GetResult();
    Check(report.Cancelled && report.Updated.Count == 1 && report.Failed.Count == 0 && seen.Count == 2);
});
Test("Sync all does not start when already cancelled", () => {
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    var report = SyncCoordinator.Run((source, progress, ct) => throw new Exception("Must not run"), new Progress<string>(), cancel.Token).GetAwaiter().GetResult();
    Check(report.Cancelled && report.Updated.Count == 0 && report.Failed.Count == 0);
});

NewFeatureTests.Run(Test);
Console.WriteLine($"\n{passed} tests passed.");

sealed class ThreadGuardHandler(int callerThread, bool image) : HttpMessageHandler
{
    public int Reads, Requests;
    void Guard() { if (Environment.CurrentManagedThreadId == callerThread) throw new InvalidOperationException("Transport ran on the caller thread"); }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guard(); Requests++;
        var stream = new GuardStream(System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"name\":\"Network test\"}}"), () => { Guard(); Reads++; });
        var content = new StreamContent(stream);
        content.Headers.ContentType = new(image ? "image/png" : "application/json");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
    sealed class GuardStream(byte[] bytes, Action guard) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { guard(); return base.ReadAsync(buffer, cancellationToken); }
        protected override void Dispose(bool disposing) { if (disposing) guard(); base.Dispose(disposing); }
    }
}

sealed class FakeHandler : HttpMessageHandler
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Queue<string> Responses { get; } = new();
    public List<string?> Headers { get; } = [];
    public List<string> Urls { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Headers.Add(request.Headers.Authorization?.ToString());
        Urls.Add(request.RequestUri!.OriginalString);
        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = new StringContent(Responses.Dequeue()) });
    }
}
