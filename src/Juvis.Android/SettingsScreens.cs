using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Widget;
using Juvis.Core;
using System.Text;
namespace Juvis.AndroidApp;

public partial class MainActivity
{
    const int ExportCode = 40, ImportCode = 41;
    string syncSummary = "";
    void MoreScreen()
    {
        Title("Data & tools", "Sync when connected. Keep browsing offline.");
        body.AddView(Button("Commodities", () => { Navigate("Commodities"); return Task.CompletedTask; }));
        body.AddView(Button("Blueprint library", () => { Navigate("Blueprints"); return Task.CompletedTask; }));
        body.AddView(Button("Local inventory by location", () => { Navigate("Inventory"); return Task.CompletedTask; }, true));
        var card = Card(); card.AddView(Label("Sync sources", 21));
        card.AddView(Label("The bundled starter data includes a weapon and ammunition snapshot refreshed on 15 September 2026. Sync all sources for broader coverage. Images load when viewed; detailed Wiki records and vehicle loadouts refresh on their pages. Failed or cancelled syncs keep the previous cache.", 13, muted));
        var syncAll = Button("Sync all sources", RunSyncAll, true);
        syncAll.Enabled = !busy; card.AddView(syncAll);
        if (syncSummary.Length > 0) card.AddView(Label(syncSummary, 13, muted));
        foreach (var source in SyncCoordinator.Sources)
        {
            card.AddView(Label(source + " · " + (armory.Catalog.Synced.TryGetValue(source, out var date) ? date.ToLocalTime().ToString("g") : "not synced"), 12, muted));
        }
        var cancel = Button("Cancel active sync", () => { operation?.Cancel(); return Task.CompletedTask; });
        cancel.Enabled = busy; card.AddView(cancel);
        body.AddView(card);
        body.AddView(Label($"Citizen Starter Guide: {armory.Catalog.Guide.Blueprints.Count:N0} blueprint records cached. Community mission reports are shown separately on item and recipe pages.", 13, muted));
        body.AddView(Button("Citizen Starter Guide website", () => { OpenUrl(StarterGuide.Home); return Task.CompletedTask; }));
        var token = new EditText(this) { Hint = "Optional UEX bearer token (session only)", InputType = InputTypes.ClassText | InputTypes.TextVariationPassword };
        token.SetSingleLine(true);
        token.SetTextColor(ink); token.SetHintTextColor(muted); body.AddView(token);
        body.AddView(Button("Use token for this session", () => { armory.Api.UexToken = token.Text?.Trim() ?? ""; token.Text = ""; status.Text = "UEX token set for this session; excluded from backups."; return Task.CompletedTask; }));
        body.AddView(Label("Backup & restore", 21));
        body.AddView(Label("Exports gear states, favorites, owned blueprints, craft plan, owned vehicles, proposed builds, location inventory, tracked fitted equipment, inventory history and ZERO → HERO run progress. Catalogs, images, the read-only knowledge database and tokens are excluded. Save outside the app before uninstalling.", 13, muted));
        body.AddView(Button("Export backup to a file", () => { ExportBackup(); return Task.CompletedTask; }, true));
        body.AddView(Button("Import & merge backup", () => { ImportBackup(); return Task.CompletedTask; }));
        body.AddView(Label($"{bundledImages.Count:N0} bundled images · always available offline", 16));
        body.AddView(Label($"Downloaded image cache · {images.Bytes / 1048576.0:0.0} MB / 100 MB", 16));
        body.AddView(Button("Clear downloaded images", async () => { await images.Clear(); Draw(); }));
        body.AddView(Label("JUVIS Android 0.2.0-beta.1 · Native C#\nCommunity data: UEX and Star Citizen Wiki. Unofficial fan companion; not affiliated with Cloud Imperium Games.\nDesktop backup migration awaits the Windows source/schema.", 12, muted));
        body.AddView(Button("UEX data source", () => { OpenUrl("https://uexcorp.space/"); return Task.CompletedTask; }));
        body.AddView(Button("Star Citizen Wiki data source", () => { OpenUrl("https://api.star-citizen.wiki/"); return Task.CompletedTask; }));
    }
    async Task RunSyncAll()
    {
        if (busy) throw new InvalidOperationException("A sync is already running. Wait or cancel it first.");
        busy = true;
        var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = current;
        syncSummary = "Sync in progress…";
        Draw();
        try
        {
            var report = await SyncCoordinator.Run(armory.Sync, new Progress<string>(s =>
            {
                if (!IsDestroyed && ReferenceEquals(operation, current)) status.Text = s;
            }), current.Token);
            syncSummary = $"{(report.Cancelled ? "Sync cancelled" : "Sync complete")} · {report.Updated.Count}/{SyncCoordinator.Sources.Count} sources updated";
            if (report.Failed.Count > 0)
                syncSummary += "\nPrevious cache retained for:\n" + string.Join("\n", report.Failed.Select(f => f.Source + ": " + f.Message));
            if (report.Cancelled) syncSummary += "\nCompleted updates are saved. Remaining sources were not updated.";
        }
        finally
        {
            operation = null; busy = false; current.Dispose();
            if (!IsDestroyed)
            {
                if (module == "More") Draw();
                status.Text = syncSummary.Split('\n')[0];
            }
        }
    }
    async Task RunSync(string source)
    {
        if (busy) throw new InvalidOperationException("A sync is already running. Wait or cancel it first.");
        busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            status.Text = "Starting " + source + "…";
            await armory.Sync(source, new Progress<string>(s => { if (!IsDestroyed) status.Text = s; }), operation.Token);
            status.Text = source + " synced successfully";
            if (module == "More") Draw();
        }
        finally { busy = false; operation.Dispose(); operation = null; }
    }
    void ExportBackup()
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable); intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, "JUVIS-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json");
        StartActivityForResult(intent, ExportCode);
    }
    void ImportBackup()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable); intent.SetType("*/*");
        StartActivityForResult(intent, ImportCode);
    }
    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data == null) return;
        try
        {
            if (requestCode == ExportCode)
            {
                await using var output = ContentResolver!.OpenOutputStream(data.Data, "wt") ?? throw new IOException("Could not open the destination.");
                var bytes = Encoding.UTF8.GetBytes(Backup.Export(armory.State));
                await output.WriteAsync(bytes); await output.FlushAsync();
                status.Text = "Backup exported. Keep this file before uninstalling.";
            }
            else if (requestCode == ImportCode)
            {
                await using var input = ContentResolver!.OpenInputStream(data.Data) ?? throw new IOException("Could not open the backup.");
                using var memory = new MemoryStream(); var buffer = new byte[8192]; int n;
                while ((n = await input.ReadAsync(buffer)) > 0) { if (memory.Length + n > Backup.MaxBytes) throw new InvalidDataException("Backup exceeds 8 MB."); memory.Write(buffer, 0, n); }
                var incoming = Backup.Parse(Encoding.UTF8.GetString(memory.ToArray()));
                new AlertDialog.Builder(this).SetTitle("Merge this backup?")!
                    .SetMessage($"{incoming.Gear.Count} gear states, {incoming.Blueprints.Count} blueprints, {incoming.CraftPlan.Count} planned recipes, {incoming.Builds.Count} vehicle builds, {incoming.StorageLocations.Count} storage locations, {incoming.Inventory.Count} inventory entries and ZERO → HERO run {incoming.ZeroHero.RunNumber}. Imported values win where the same item exists. A copy of your current state is saved first.")!
                    .SetNegativeButton("Cancel", (_, _) => { })!
                    .SetPositiveButton("Import", async (_, _) => {
                        try {
                            var store = new LocalStore(FilesDir!.AbsolutePath);
                            await store.Write("before-import.json", new BackupEnvelope("juvis-android", 1, DateTimeOffset.UtcNow, armory.State));
                            await armory.Import(incoming); Draw(); status.Text = "Backup merged successfully";
                        } catch (Exception ex) { Error(ex); }
                    })!.Show();
            }
        }
        catch (Exception ex) { Error(ex); }
    }
}
