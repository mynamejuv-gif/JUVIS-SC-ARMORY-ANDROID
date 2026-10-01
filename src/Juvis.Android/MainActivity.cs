using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Juvis.Core;
using System.Text.Json;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace Juvis.AndroidApp;

[Activity(Label = "JUVIS SC ARMORY", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public partial class MainActivity : Activity
{
    readonly Color bg = Color.ParseColor("#06131B"), panel = Color.ParseColor("#0C202C"), cyan = Color.ParseColor("#4DDFFF"),
        ink = Color.ParseColor("#E0F1FA"), muted = Color.ParseColor("#8AAAB9");
    Armory armory = null!;
    ImageCache images = null!;
    BundledImageIndex bundledImages = new(new());
    readonly HttpClient http = new();
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    LinearLayout root = null!, body = null!, nav = null!;
    TextView status = null!;
    string module = "Catalog";
    string query = "", category = "All categories", gearFilter = "All saved", ammoFilter = "All ammunition";
    int page;
    bool ready, busy;
    int screenGeneration;
    Action? back;
    ScrollView scroll = null!;
    BackCallback? backCallback;
    const int PageSize = 30;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(bg);
        root.SetFitsSystemWindows(true);
        var header = new LinearLayout(this) { Orientation = Orientation.Vertical };
        header.SetPadding(Dp(20), Dp(18), Dp(20), Dp(10));
        header.AddView(Label("J U V I S   /   SC ARMORY", 21, cyan));
        header.AddView(Label("YOUR FIELD COMPANION", 10, muted));
        root.AddView(header);
        status = Label("Opening your armory…", 12, muted);
        status.SetPadding(Dp(20), Dp(4), Dp(20), Dp(8)); root.AddView(status);
        scroll = new ScrollView(this) { FillViewport = true };
        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(16), Dp(8), Dp(16), Dp(24));
        scroll.AddView(body);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        nav = Row();
        nav.SetPadding(Dp(4), Dp(5), Dp(4), Dp(8));
        root.AddView(nav);
        SetContentView(root);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            backCallback = new BackCallback(HandleBack);
            OnBackInvokedDispatcher?.RegisterOnBackInvokedCallback(0, backCallback);
        }
        try
        {
            var folder = FilesDir!.AbsolutePath;
            armory = new Armory(new LocalStore(folder), new ApiClient(http));
            images = new ImageCache(System.IO.Path.Combine(CacheDir!.AbsolutePath, "images"), http);
            using (var indexStream = Assets!.Open("bundled-images/image-index.json"))
                bundledImages = await BundledImageIndex.Load(indexStream);
            await armory.Initialize(async () => {
                using var stream = Assets!.Open("starter.json");
                return (await JsonSerializer.DeserializeAsync<Catalog>(stream, LocalStore.Json))!;
            });
            if (lifetime.IsCancellationRequested) return;
            ready = true;
            module = savedInstanceState?.GetString("module") ?? armory.State.LastModule;
            if (!new[] { "Catalog", "ZeroHero", "Craft", "Vehicles", "My Gear", "More", "Commodities", "Blueprints", "Inventory" }.Contains(module)) module = "Catalog";
            Draw();
        }
        catch (Exception ex)
        {
            status.Text = "Could not open local data";
            body.AddView(Label("Your existing files have been retained. " + ex.Message, 16));
            body.AddView(Button("Close and retry", () => { Finish(); return Task.CompletedTask; }));
        }
    }
    protected override void OnSaveInstanceState(Bundle outState) { outState.PutString("module", module); base.OnSaveInstanceState(outState); }
    protected override void OnDestroy()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && backCallback != null) OnBackInvokedDispatcher?.UnregisterOnBackInvokedCallback(backCallback);
        lifetime.Cancel(); operation?.Cancel(); http.Dispose(); base.OnDestroy();
    }
    public override void OnBackPressed() => HandleBack();
    void HandleBack()
    {
        if (back != null) { var target = back; back = null; target(); }
        else if (ready && module != "Catalog") Navigate("Catalog");
        else Finish();
    }
    int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density + .5f);
    TextView Label(string text, float size = 14, Color? color = null)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(color ?? ink);
        view.SetPadding(0, Dp(4), 0, Dp(4));
        return view;
    }
    LinearLayout Row() => new(this) { Orientation = Orientation.Horizontal };
    LinearLayout Card()
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var shape = new GradientDrawable(); shape.SetColor(panel); shape.SetCornerRadius(Dp(12)); shape.SetStroke(Dp(1), Color.ParseColor("#234454"));
        card.Background = shape; card.SetPadding(Dp(14), Dp(10), Dp(14), Dp(12));
        card.LayoutParameters = new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(12) };
        return card;
    }
    Button Button(string text, Func<Task> action, bool accent = false)
    {
        var b = new Button(this) { Text = text, TextSize = 12 };
        b.SetAllCaps(false); b.SetTextColor(accent ? bg : cyan); b.SetMinHeight(Dp(48));
        var shape = new GradientDrawable(); shape.SetColor(accent ? cyan : Color.ParseColor("#102B3A")); shape.SetCornerRadius(Dp(8));
        b.Background = shape;
        b.LayoutParameters = new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6), BottomMargin = Dp(4) };
        b.Click += async (_, _) => { b.Enabled = false; try { await action(); } catch (Exception ex) { Error(ex); } finally { if (!b.IsDisposed()) b.Enabled = true; } };
        return b;
    }
    void Error(Exception ex)
    {
        if (IsFinishing || IsDestroyed) return;
        var message = ex is System.OperationCanceledException ? "Request cancelled or timed out. Cached data is still available; tap the action again to retry." : ex.Message;
        status.Text = message;
        new AlertDialog.Builder(this).SetTitle("JUVIS")!.SetMessage(message)!.SetPositiveButton("OK", (_, _) => { })!.Show();
    }
    new void Title(string title, string subtitle)
    {
        body.AddView(Label(title, 26)); body.AddView(Label(subtitle, 13, muted));
    }
    void Navigate(string target)
    {
        module = target; page = 0; query = ""; back = null;
        Draw();
        _ = SaveModule();
    }
    async Task SaveModule() { try { await armory.Change(s => s.LastModule = module); } catch (Exception ex) { Error(ex); } }
    void Draw()
    {
        if (!ready) return;
        screenGeneration++;
        body.RemoveAllViews(); back = null;
        scroll.ScrollTo(0, 0);
        if (!busy) status.Text = $"{armory.Catalog.Items.Count:N0} items cached  ·  " + (armory.Catalog.Synced.Count == 0 ? "Starter snapshot · sync in More" : "Offline ready · timestamps in More");
        nav.RemoveAllViews();
        foreach (var destination in new[] { (Target: "Catalog", Label: "Catalog"), (Target: "ZeroHero", Label: "0 → Hero"), (Target: "Craft", Label: "Craft"), (Target: "Vehicles", Label: "Vehicles"), (Target: "My Gear", Label: "My Gear"), (Target: "More", Label: "More") })
        {
            var b = Button(destination.Label, () => { Navigate(destination.Target); return Task.CompletedTask; }, module == destination.Target);
            b.TextSize = 9; b.SetPadding(Dp(1), 0, Dp(1), 0);
            nav.AddView(b, new LinearLayout.LayoutParams(0, Dp(52), 1) { LeftMargin = Dp(2), RightMargin = Dp(2) });
        }
        switch (module)
        {
            case "Catalog": CatalogScreen(); break;
            case "ZeroHero": ZeroHeroScreen(); break;
            case "My Gear": CatalogScreen(true); break;
            case "Craft": CraftScreen(); break;
            case "Blueprints": BlueprintsScreen(); break;
            case "Commodities": CommoditiesScreen(); break;
            case "Vehicles": VehiclesScreen(); break;
            case "Inventory": InventoryScreen(); break;
            default: MoreScreen(); break;
        }
    }
    void Detail(string title, string subtitle, Action returnTo)
    {
        screenGeneration++;
        body.RemoveAllViews(); back = returnTo;
        scroll.ScrollTo(0, 0);
        body.AddView(Button("‹ Back", () => { back = null; returnTo(); return Task.CompletedTask; })); Title(title, subtitle);
    }
    EditText Search(string hint, Action<string> changed)
    {
        var edit = new EditText(this) { Hint = hint, Text = query, TextSize = 16 };
        edit.SetSingleLine(true);
        edit.SetTextColor(ink); edit.SetHintTextColor(muted); edit.SetMinHeight(Dp(52));
        edit.TextChanged += (_, _) => { query = edit.Text ?? ""; page = 0; changed(query); };
        body.AddView(edit); return edit;
    }
    void Choice(LinearLayout parent, string[] options, string current, Action<string> changed)
    {
        var spinner = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, options);
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        spinner.SetSelection(Math.Max(0, Array.IndexOf(options, current)));
        spinner.ItemSelected += (_, args) => { var chosen = options[args.Position]; if (chosen != current) { current = chosen; changed(chosen); } };
        parent.AddView(spinner, new LinearLayout.LayoutParams(-1, Dp(52)));
    }
    static bool Matches(string q, params string[] values) => q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => values.Any(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)));
    void Pager<T>(LinearLayout target, List<T> results, Action<T> render, Action refresh)
    {
        target.RemoveAllViews(); page = Math.Clamp(page, 0, Math.Max(0, (results.Count - 1) / PageSize));
        target.AddView(Label($"{results.Count:N0} results · page {page + 1} / {Math.Max(1, (results.Count + PageSize - 1) / PageSize)}", 12, muted));
        foreach (var item in results.Skip(page * PageSize).Take(PageSize)) render(item);
        if (results.Count == 0) target.AddView(Label("No matches. Try another search or sync this module in More.", 15));
        if (page > 0) target.AddView(Button("Previous page", () => { page--; refresh(); return Task.CompletedTask; }));
        if ((page + 1) * PageSize < results.Count) target.AddView(Button("Next page", () => { page++; refresh(); return Task.CompletedTask; }));
    }
    void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("No valid web link is available.");
        StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url)));
    }
    Task AskGemini(string prompt)
    {
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        clipboard.PrimaryClip = ClipData.NewPlainText("JUVIS research prompt", prompt);
        new AlertDialog.Builder(this).SetTitle("Prompt copied")!
            .SetMessage("Open Gemini and paste the prompt into a new chat. It includes the selected item or build, not your full backup.")!
            .SetPositiveButton("Open Gemini", (_, _) => { try { OpenUrl(GeminiPrompt.Url); } catch (Exception ex) { Error(ex); } })!
            .SetNegativeButton("Stay here", (_, _) => { })!.Show();
        return Task.CompletedTask;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
sealed class BackCallback(Action action) : Java.Lang.Object, global::Android.Window.IOnBackInvokedCallback
{
    public void OnBackInvoked() => action();
}

internal static class ViewExtensions { public static bool IsDisposed(this View v) => v.Handle == IntPtr.Zero; }
