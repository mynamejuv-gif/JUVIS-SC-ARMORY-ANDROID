using Android.Widget;
using Juvis.Core;
namespace Juvis.AndroidApp;

public partial class MainActivity
{
    string vehicleFilter = "All vehicles", goal = "Balanced";
    bool advancedLoadout;
    void VehiclesScreen()
    {
        Title("Ship & ground upgrades", "Choose a vehicle. Inspect its ports. Save a proposed build.");
        var results = new LinearLayout(this) { Orientation = Orientation.Vertical };
        void Render() => Pager(results, armory.Catalog.Vehicles.Where(v => CatalogPresentation.VehicleName(v) != null && Matches(query, CatalogPresentation.VehicleName(v)!, v.Manufacturer) && (vehicleFilter switch {
            "My vehicles" => armory.State.Vehicles.Contains(v.Id), "Ships" => !v.Ground, "Ground" => v.Ground, _ => true })).OrderBy(v => v.Name).ToList(), v => {
            var card = Card(); card.AddView(Label(v.Ground ? "GROUND VEHICLE" : "SHIP", 10, cyan)); card.AddView(Label(CatalogPresentation.VehicleName(v)!, 20));
            card.AddView(Label((CatalogPresentation.Name(v.Manufacturer) ?? "Manufacturer unavailable") + (armory.State.Vehicles.Contains(v.Id) ? " · ✓ Owned" : ""), 12, muted));
            card.AddView(Button("Loadout & upgrades  ›", () => { VehicleScreen(v); return Task.CompletedTask; })); results.AddView(card);
        }, Render);
        Search("Search ships and ground vehicles", _ => Render());
        Choice(body, ["All vehicles", "My vehicles", "Ships", "Ground"], vehicleFilter, c => { vehicleFilter = c; page = 0; Render(); });
        body.AddView(results); Render();
    }
    void VehicleScreen(Vehicle v, bool proposed = false)
    {
        var stock = armory.Catalog.Vehicles.FirstOrDefault(x => x.Id == v.Id) ?? v;
        v = LocalInventory.EffectiveVehicle(armory.State, stock);
        var vehicleName = CatalogPresentation.VehicleName(v);
        if (vehicleName == null) { Draw(); return; }
        Detail(vehicleName, $"{(v.Ground ? "Ground vehicle" : "Ship")} / {CatalogPresentation.Name(v.Manufacturer) ?? "Manufacturer unavailable"}\nLoadout patch: {ApiParser.First(v.Ports.FirstOrDefault()?.Version ?? "", v.Version, "unknown")}", Draw);
        var generation = screenGeneration;
        if (armory.Catalog.Vehicles.Count(other => other.Id == v.Id) > 1)
        {
            body.AddView(Label("Source identity conflict: multiple vehicle variants share this ID. Ownership and build editing are paused for these variants to prevent changes applying to the wrong vehicle. Existing saves are retained in your backup.", 15, cyan));
            body.AddView(Button("Export existing saves", () => { ExportBackup(); return Task.CompletedTask; }));
            return;
        }
        body.AddView(Button(armory.State.Vehicles.Contains(v.Id) ? "✓ Owned vehicle · tap to unmark" : "Mark vehicle owned", async () => {
            await armory.Change(s => { if (!s.Vehicles.Add(v.Id)) s.Vehicles.Remove(v.Id); }); VehicleScreen(v, proposed);
        }));
        Choice(body, ["Balanced", "Combat", "Defense", "Fast Travel", "Stealth", "Industrial / Mining", "Budget"], goal, g => goal = g);
        body.AddView(Button("✦ Ask Gemini for upgrade suggestions", () => AskGemini(GeminiPrompt.Vehicle(v with { Name = vehicleName }, armory.State.Builds.GetValueOrDefault(v.Id) ?? [], goal)), true));
        body.AddView(Button("Refresh stock loadout / retry", async () => {
            status.Text = "Loading ports and installed components… (20-second timeout)";
            var loaded = await armory.Api.LoadVehicle(stock, lifetime.Token);
            await armory.RememberVehicle(loaded); if (generation == screenGeneration) { VehicleScreen(loaded, proposed); status.Text = $"{loaded.Ports.Count} ports cached"; }
        }));
        var tabs = Row();
        tabs.AddView(Button("Current loadout", () => { VehicleScreen(v); return Task.CompletedTask; }, !proposed), new LinearLayout.LayoutParams(0, Dp(50), 1));
        tabs.AddView(Button("Proposed build", () => { VehicleScreen(v, true); return Task.CompletedTask; }, proposed), new LinearLayout.LayoutParams(0, Dp(50), 1));
        body.AddView(tabs);
        if (armory.State.TrackedLoadouts.ContainsKey(v.Id)) body.AddView(Label("Showing your tracked fitted equipment. API stock refresh preserves these manual changes.", 13, cyan));
        if (proposed)
        {
            var build = armory.State.Builds.GetValueOrDefault(v.Id) ?? [];
            if (build.Count == 0) body.AddView(Label("Open a stock port, choose a compatible candidate, then add it to your build.", 15, muted));
            var visibleBuild = build.Where(entry => CatalogPresentation.Name(armory.Catalog.Items.FirstOrDefault(i => i.Id == entry.ItemId) is { } cached ? CatalogPresentation.ItemName(cached) : null, entry.ItemName) != null).ToList();
            foreach (var entry in visibleBuild)
            {
                var port = v.Ports.FirstOrDefault(p => p.Id == entry.PortId);
                var item = armory.Catalog.Items.FirstOrDefault(i => i.Id == entry.ItemId);
                var fit = port != null && item != null ? Compatibility.Check(port, item) : new FitResult(Fit.CheckRequired, "Refresh vehicle and component data");
                var entryName = CatalogPresentation.Name(item == null ? null : CatalogPresentation.ItemName(item), entry.ItemName)!;
                var previousName = port?.Installed == null ? "Empty" : CatalogPresentation.ItemName(port.Installed) ?? "Installed component name unavailable";
                var card = Card(); card.AddView(Label(entryName, 19));
                if (port != null) card.AddView(Label(CatalogPresentation.PortName(port), 11, muted));
                card.AddView(Label($"Was: {previousName}\n{fit.Fit}: {fit.Reason}", 13, cyan));
                if (item != null) card.AddView(Button("Component details", () => { ItemScreen(item, () => VehicleScreen(v, true)); return Task.CompletedTask; }));
                card.AddView(Button("Remove from proposed build", async () => { await armory.Change(s => s.Builds[v.Id].RemoveAll(e => e.PortId == entry.PortId)); VehicleScreen(v, true); }));
                body.AddView(card);
            }
            if (build.Count > visibleBuild.Count) body.AddView(Label($"{build.Count - visibleBuild.Count} saved build entr{(build.Count - visibleBuild.Count == 1 ? "y is" : "ies are")} hidden because no trustworthy display name is available. The saved data remains in exports.", 13, muted));
            if (build.Count > 0) body.AddView(Button("Apply build & store removed equipment", () => { ApplyBuildScreen(stock); return Task.CompletedTask; }, true));
            body.AddView(Label("Planning only: this does not modify your in-game ship. Refresh data after a patch to recheck saved candidates.", 13, muted));
            return;
        }
        if (v.Ports.Count == 0) body.AddView(Label("No loadout cached yet. Tap Refresh stock loadout. If the request fails, cached data remains available and the same button retries.", 15, muted));
        var portSearch = Entry("Filter equipment: shields, nose guns, mining…");
        body.AddView(portSearch);
        var advanced = new CheckBox(this) { Text = "Advanced View — all raw ports", Checked = advancedLoadout };
        advanced.SetTextColor(cyan); advanced.SetMinHeight(Dp(48)); body.AddView(advanced);
        var portCards = new LinearLayout(this) { Orientation = Orientation.Vertical }; body.AddView(portCards);
        var expanded = new HashSet<string>();
        void RenderPorts()
        {
            portCards.RemoveAllViews();
            var groups = LoadoutPresentation.Group(v.Ports, advancedLoadout);
            var visible = groups.Values.Sum(g => g.Count);
            portCards.AddView(Label($"{visible} of {v.Ports.Count} ports · fixed / internal / unknown hidden by default", 12, muted));
            foreach (var group in groups)
            {
                var rows = group.Value.Where(p => Matches(portSearch.Text ?? "", LoadoutPresentation.ContextName(p), p.Installed == null ? "" : CatalogPresentation.ItemName(p.Installed) ?? "", string.Join(" ", p.Types.Select(t => t.Type)), group.Key)).ToList();
                if (rows.Count == 0) continue;
                var section = Card();var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
                var title = Button((expanded.Contains(group.Key) ? "▾ " : "▸ ") + group.Key.ToUpperInvariant() + $" ({rows.Count})", () => { if (!expanded.Add(group.Key)) expanded.Remove(group.Key); RenderPorts(); return Task.CompletedTask; });
                section.AddView(title);
                if (expanded.Contains(group.Key)) foreach (var port in rows)
                {
                    var installedName = port.Installed == null ? "Empty / unidentified slot" : CatalogPresentation.ItemName(port.Installed) ?? "Installed name unavailable";
                    var entry = Card(); entry.AddView(Label(LoadoutPresentation.ContextName(port), 13, cyan)); entry.AddView(Label(installedName, 18));
                    entry.AddView(Label($"Allowed S{port.MinSize?.ToString() ?? "?"}–S{port.MaxSize?.ToString() ?? "?"} · {LoadoutPresentation.Visibility(port)}", 12, muted));
                    if (advancedLoadout) entry.AddView(Label($"Raw port: {port.Id}\nType: {port.RawType} · editable: {port.Editable?.ToString() ?? "unknown"}", 11, muted));
                    if (port.Installed != null) entry.AddView(Button("Installed component details", () => { ItemScreen(LoadoutPresentation.InstalledForComparison(port, armory.Catalog.Items) ?? port.Installed, () => VehicleScreen(stock)); return Task.CompletedTask; }));
                    if (LoadoutPresentation.Visibility(port) == PortVisibility.Upgradeable) entry.AddView(Button("Show compatible upgrades  ›", () => { CompatibleScreen(v, port); return Task.CompletedTask; }));
                    content.AddView(entry);
                }
                section.AddView(content); portCards.AddView(section);
            }
        }
        portSearch.TextChanged += (_, _) => RenderPorts();
        advanced.CheckedChange += (_, e) => { advancedLoadout = e.IsChecked; RenderPorts(); };
        RenderPorts();
    }

    void CompatibleScreen(Vehicle v, Port port)
    {
        var vehicleName = CatalogPresentation.VehicleName(v);
        if (vehicleName == null) { VehiclesScreen(); return; }
        var currentName = port.Installed == null ? "Empty" : CatalogPresentation.ItemName(port.Installed) ?? "Installed component name unavailable";
        Detail("Compatible upgrades", $"{vehicleName}\n{CatalogPresentation.PortName(port)}\nCurrent: {currentName}", () => VehicleScreen(v));
        var generation = screenGeneration;
        body.AddView(Label("Candidates with incomplete data are shown for review; only confirmed matches can be added. Mount fit does not guarantee a performance improvement.", 13, muted));
        body.AddView(Button("Sync candidates for this port / retry", async () => {
            var types = string.Join(",", port.Types.Select(t => t.Type));
            if (types.Length == 0) throw new InvalidOperationException("Port type is unavailable; refresh the stock loadout first.");
            var route = "items?filter[type]=" + Uri.EscapeDataString(types);
            if (port.Version.Length > 0) route += "&version=" + Uri.EscapeDataString(port.Version);
            var items = await armory.Api.WikiPages(route, ApiParser.WikiItem, new Progress<string>(s => status.Text = s), lifetime.Token);
            await armory.Cache(c => { var map = c.Items.ToDictionary(i => i.Id); foreach (var i in items) map[i.Id] = i; c.Items = map.Values.ToList(); });
            if (generation == screenGeneration) CompatibleScreen(v, port);
        }));
        var candidates = armory.Catalog.Items.Select(i => (Item: i, Result: Compatibility.Check(port, i)))
            .Where(x => CatalogPresentation.ItemName(x.Item) != null && port.Types.Any(t => t.Type.Equals(x.Item.Type, StringComparison.OrdinalIgnoreCase)) && x.Result.Fit is Fit.Direct or Fit.CheckRequired)
            .OrderBy(x => x.Result.Fit).ThenBy(x => CatalogPresentation.ItemName(x.Item)).ToList();
        if (candidates.Count == 0) body.AddView(Label("No candidate matches in this cache. Sync candidates for this port.", 16));
        if (port.Installed?.Type.Equals("WeaponGun", StringComparison.OrdinalIgnoreCase) == true) body.AddView(Button("Refresh installed weapon performance", async () => {
            var refreshed = await armory.Api.LoadItem(port.Installed, lifetime.Token);
            await armory.RememberItem(refreshed); if (generation == screenGeneration) CompatibleScreen(v, port);
        }));
        ComparisonTable(v, port, candidates.Where(c => c.Item.Id != port.Installed?.Id).ToList());
    }
}
