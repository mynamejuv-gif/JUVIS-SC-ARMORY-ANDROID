using Android.Text;
using Android.Widget;
using Juvis.Core;
namespace Juvis.AndroidApp;

public partial class MainActivity
{
    string zeroHeroView = "Roadmap";

    void ZeroHeroScreen()
    {
        // Zero Hero refreshes itself after tab changes and saved progress. Replace
        // the current page instead of appending the refreshed controls below it.
        screenGeneration++;
        body.RemoveAllViews();
        back = null;
        scroll.ScrollTo(0, 0);

        Title("ZERO → HERO", $"Post-wipe run {armory.State.ZeroHero.RunNumber} · started {armory.State.ZeroHero.StartedUtc.ToLocalTime():d MMM yyyy}");
        var patch = Card();
        patch.AddView(Label($"LIVE {ZeroHero.LivePatch}", 18, cyan));
        patch.AddView(Label($"PTU {ZeroHero.PtuPatch} · not LIVE\nKnowledge checked {ZeroHero.VerifiedDate}. Older or unknown records are marked OUTDATED / VERIFY.", 12, muted));
        body.AddView(patch);

        var tabs = Row();
        foreach (var view in new[] { "Roadmap", "Resources", "Contracts", "Industry", "Fleet" })
        {
            var selected = view == zeroHeroView;
            var button = Button(view == "Resources" ? "Stock" : view, () => { zeroHeroView = view; ZeroHeroScreen(); return Task.CompletedTask; }, selected);
            button.TextSize = 9;
            tabs.AddView(button, new LinearLayout.LayoutParams(0, Dp(48), 1) { LeftMargin = Dp(1), RightMargin = Dp(1) });
        }
        body.AddView(tabs);
        switch (zeroHeroView)
        {
            case "Resources": ZeroHeroResources(); break;
            case "Contracts": ZeroHeroContracts(); break;
            case "Industry": ZeroHeroIndustry(); break;
            case "Fleet": ZeroHeroFleet(); break;
            default: ZeroHeroRoadmap(); break;
        }
    }

    void ZeroHeroRoadmap()
    {
        var completed = armory.State.ZeroHero.CompletedSteps.Count;
        body.AddView(Label($"PROGRESSION ROADMAP · {completed}/{ZeroHero.Steps.Count} complete", 18));
        foreach (var step in ZeroHero.Steps)
        {
            var done = armory.State.ZeroHero.CompletedSteps.Contains(step.Id);
            var card = Card();
            card.AddView(Label(step.Stage, 10, done ? cyan : muted));
            card.AddView(Label((done ? "✓ " : "") + step.Title, 19));
            card.AddView(Label(step.Description, 13, muted));
            card.AddView(Button(done ? "Mark incomplete" : "Complete this step", async () => {
                await armory.Change(s => { if (!s.ZeroHero.CompletedSteps.Add(step.Id)) s.ZeroHero.CompletedSteps.Remove(step.Id); });
                ZeroHeroScreen();
            }, !done));
            body.AddView(card);
        }
        body.AddView(Button("New Wipe Run", async () => {
            if (!await Confirm("Start a new wipe run?", "This clears only ZERO → HERO milestones, tracked resource quantities, contract needs, base-prep checks and the rebuild queue. The knowledge database, catalog, normal inventory, gear, blueprints, craft plan, ships and saved loadouts stay intact.")) return;
            await armory.Change(s => ZeroHero.NewRun(s, DateTimeOffset.UtcNow));
            zeroHeroView = "Roadmap"; ZeroHeroScreen(); status.Text = "New wipe run started · knowledge and Armory saves retained";
        }));
    }

    void ZeroHeroResources()
    {
        var plan = ZeroHero.ResourcePlan(armory.Catalog, armory.State);
        var missing = plan.Where(r => r.Missing > 0).Sum(r => r.Missing);
        body.AddView(Label("RESOURCE INVENTORY", 18));
        body.AddView(Label($"{plan.Count} known or plan-linked resources · {missing:0.###} units still missing", 13, muted));
        body.AddView(Label("PRIORITY KEEP is automatic for active contract and blueprint requirements. You can override any recommendation.", 12, muted));
        foreach (var row in plan.Take(60))
        {
            var card = Card();
            card.AddView(Label(row.Disposition.ToString().Replace("PriorityKeep", "PRIORITY KEEP").ToUpperInvariant(), 10, row.Disposition == ResourceDisposition.Sell ? muted : cyan));
            card.AddView(Label(row.Name, 18));
            card.AddView(Label($"Owned {row.Owned:0.###} · Needed {row.Needed:0.###} · Missing {row.Missing:0.###}", 14, row.Missing > 0 ? cyan : muted));
            card.AddView(Label(PatchLabel(row.Validity, row.Version), 11, row.Validity == PatchValidity.Live ? muted : cyan));
            card.AddView(Button("Track / recommendation", () => { ZeroHeroResourceScreen(row); return Task.CompletedTask; }));
            body.AddView(card);
        }
        if (plan.Count > 60) body.AddView(Label($"Showing the 60 highest-priority entries of {plan.Count}. Active and missing requirements are always sorted first.", 12, muted));
    }

    void ZeroHeroResourceScreen(ResourcePlanRow original)
    {
        var row = ZeroHero.ResourcePlan(armory.Catalog, armory.State).FirstOrDefault(r => r.Id == original.Id) ?? original;
        Detail(row.Name, PatchLabel(row.Validity, row.Version), ZeroHeroScreen);
        var card = Card();
        card.AddView(Label($"Owned {row.Owned:0.###} · Needed {row.Needed:0.###} · Missing {row.Missing:0.###}", 18, row.Missing > 0 ? cyan : ink));
        card.AddView(Label("Source", 11, cyan)); card.AddView(Label(row.Acquisition, 14));
        card.AddView(Label("Why keep or sell", 11, cyan)); card.AddView(Label(row.Use, 14));
        body.AddView(card);
        var quantity = Entry("Tracked quantity", row.Owned.ToString("0.###"));
        quantity.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
        body.AddView(quantity);
        body.AddView(Button("Save quantity", async () => {
            if (!decimal.TryParse(quantity.Text, out var value) || value < 0 || value > 1000000) throw new InvalidOperationException("Enter a quantity from 0 to 1,000,000.");
            await armory.Change(s => { if (value == 0) s.ZeroHero.ResourceInventory.Remove(row.Id); else s.ZeroHero.ResourceInventory[row.Id] = value; });
            ZeroHeroResourceScreen(row);
        }, true));
        body.AddView(Label("Run decision", 14, cyan));
        Choice(body, ["PRIORITY KEEP", "KEEP", "SELL"], row.Disposition switch { ResourceDisposition.PriorityKeep => "PRIORITY KEEP", ResourceDisposition.Keep => "KEEP", _ => "SELL" }, choice => {
            var disposition = choice switch { "PRIORITY KEEP" => ResourceDisposition.PriorityKeep, "KEEP" => ResourceDisposition.Keep, _ => ResourceDisposition.Sell };
            _ = SaveResourceChoiceSafely(row.Id, disposition, row);
        });
    }

    async Task SaveResourceChoiceSafely(string id, ResourceDisposition disposition, ResourcePlanRow row)
    {
        try
        {
            await armory.Change(s => s.ZeroHero.ResourceChoices[id] = disposition);
            if (!IsDestroyed) ZeroHeroResourceScreen(row);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    void ZeroHeroContracts()
    {
        body.AddView(Label("CONTRACT REQUIREMENTS", 18));
        body.AddView(Label("Track the exact material and quantity shown in your accepted contract. Requirements can change by contract and patch, so the app does not invent a number.", 13, muted));
        var plan = ZeroHero.ResourcePlan(armory.Catalog, armory.State);
        var current = armory.State.ZeroHero.ContractNeeds.OrderBy(x => x.Key).ToList();
        foreach (var requirement in current)
        {
            var row = plan.FirstOrDefault(r => r.Id.Equals(requirement.Key, StringComparison.OrdinalIgnoreCase))
                ?? plan.FirstOrDefault(r => ZeroHero.Key("", r.Name).Equals(requirement.Key, StringComparison.OrdinalIgnoreCase));
            var card = Card();
            var owned = row?.Owned ?? armory.State.ZeroHero.ResourceInventory.GetValueOrDefault(requirement.Key);
            var missing = Math.Max(0, requirement.Value - owned);
            card.AddView(Label(row?.Name ?? requirement.Key.Replace('-', ' '), 18));
            card.AddView(Label($"Required {requirement.Value:0.###} · Owned {owned:0.###} · Missing {missing:0.###}", 14, missing > 0 ? cyan : muted));
            card.AddView(Button("Remove requirement", async () => { await armory.Change(s => s.ZeroHero.ContractNeeds.Remove(requirement.Key)); ZeroHeroScreen(); }));
            body.AddView(card);
        }
        var name = Entry("Contract resource name");
        var amount = Entry("Required quantity", "1"); amount.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
        body.AddView(name); body.AddView(amount);
        body.AddView(Button("Add contract requirement", async () => {
            var display = name.Text?.Trim() ?? "";
            if (!CatalogPresentation.HasName(display)) throw new InvalidOperationException("Enter the resource name exactly as shown by the contract.");
            if (!decimal.TryParse(amount.Text, out var value) || value <= 0 || value > 1000000) throw new InvalidOperationException("Enter a quantity above 0 and no more than 1,000,000.");
            var key = ZeroHero.ResourcePlan(armory.Catalog, armory.State)
                .FirstOrDefault(r => r.Name.Equals(display, StringComparison.OrdinalIgnoreCase))?.Id
                ?? ZeroHero.Key("", display);
            await armory.Change(s => s.ZeroHero.ContractNeeds[key] = value);
            ZeroHeroScreen();
        }, true));
        var live = Card(); live.AddView(Label("ORISON RELIEF SUPPORT", 17, cyan));
        live.AddView(Label("Available in LIVE 4.10.1. The loop includes resource gathering and crafted aid connected to the TH-01 Propulsor. Track the exact objective quantity above after accepting the contract.", 13));
        live.AddView(Label("LIVE · current module note", 11, muted)); body.AddView(live);
    }

    void ZeroHeroIndustry()
    {
        body.AddView(Label("MINING · SALVAGE · CRAFTING", 18));
        var mining = Card(); mining.AddView(Label("MINING", 11, cyan)); mining.AddView(Label("Fund the rebuild, then reserve active recipe inputs", 18));
        mining.AddView(Label("The 4.10.1 resource map spans ship, ROC and hand mining. Use the Stock tab for the run ledger; volatile ore should be refined promptly. Location and price data remain source-dependent.", 13, muted)); body.AddView(mining);
        var salvage = Card(); salvage.AddView(Label("SALVAGE", 11, cyan)); salvage.AddView(Label("Separate income from preparation stock", 18));
        salvage.AddView(Label("Track Recycled Material Composite and Construction Materials separately. Keep a deliberate reserve only when a contract, craft plan or base-prep target justifies it.", 13, muted)); body.AddView(salvage);
        body.AddView(Button("Open blueprint craft plan", () => { Navigate("Craft"); return Task.CompletedTask; }, true));
        body.AddView(Label("BASE-BUILDING PREPARATION", 18));
        foreach (var item in ZeroHero.BasePreparation)
        {
            var done = armory.State.ZeroHero.BasePreparationDone.Contains(item.Id);
            var card = Card(); card.AddView(Label((done ? "✓ " : "") + item.Title, 17)); card.AddView(Label(item.Description, 13, muted));
            card.AddView(Label(PatchLabel(ZeroHero.Validity(item.Version), item.Version), 11, item.Version.Contains("PTU") ? cyan : muted));
            card.AddView(Button(done ? "Mark not ready" : "Mark prepared", async () => { await armory.Change(s => { if (!s.ZeroHero.BasePreparationDone.Add(item.Id)) s.ZeroHero.BasePreparationDone.Remove(item.Id); }); ZeroHeroScreen(); }, !done));
            body.AddView(card);
        }
    }

    void ZeroHeroFleet()
    {
        body.AddView(Label("SHIP REBUILD TRACKER", 18));
        body.AddView(Label("Queue owned ships for recovery, then open their existing loadout pages to restore and apply saved builds.", 13, muted));
        var owned = armory.Catalog.Vehicles.Where(v => armory.State.Vehicles.Contains(v.Id) && CatalogPresentation.VehicleName(v) != null).OrderBy(v => v.Name).ToList();
        if (owned.Count == 0) body.AddView(Label("No owned ships are marked yet. Open Vehicles and mark the ships you want to rebuild.", 14, muted));
        foreach (var vehicle in owned.Take(50))
        {
            var queued = armory.State.ZeroHero.RebuildQueue.Contains(vehicle.Id);
            var saved = armory.State.Builds.GetValueOrDefault(vehicle.Id)?.Count ?? 0;
            var fitted = armory.State.TrackedLoadouts.GetValueOrDefault(vehicle.Id)?.Count ?? 0;
            var card = Card(); card.AddView(Label((queued ? "REBUILD QUEUE" : "OWNED") + " · " + (vehicle.Ground ? "GROUND" : "SHIP"), 10, queued ? cyan : muted));
            card.AddView(Label(CatalogPresentation.VehicleName(vehicle)!, 18));
            card.AddView(Label($"{saved} proposed changes · {fitted} tracked fitted slots · {PatchLabel(ZeroHero.Validity(vehicle.Version), vehicle.Version)}", 12, muted));
            card.AddView(Button("Open loadout", () => { VehicleScreen(vehicle); return Task.CompletedTask; }, true));
            card.AddView(Button(queued ? "Remove from rebuild queue" : "Add to rebuild queue", async () => { await armory.Change(s => { if (!s.ZeroHero.RebuildQueue.Add(vehicle.Id)) s.ZeroHero.RebuildQueue.Remove(vehicle.Id); }); ZeroHeroScreen(); }));
            body.AddView(card);
        }
        body.AddView(Button("Browse all vehicles", () => { Navigate("Vehicles"); return Task.CompletedTask; }));
    }

    static string PatchLabel(PatchValidity validity, string version) => validity switch
    {
        PatchValidity.Live => "LIVE · " + (string.IsNullOrWhiteSpace(version) ? ZeroHero.LivePatch : version),
        PatchValidity.Ptu => "PTU · not LIVE · " + version,
        _ => "OUTDATED / VERIFY · " + (string.IsNullOrWhiteSpace(version) ? "version unknown" : version),
    };
}
