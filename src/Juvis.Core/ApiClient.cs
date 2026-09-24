using System.Net.Http.Headers;
using System.Text.Json;
namespace Juvis.Core;

public sealed class ApiClient(HttpClient http)
{
    public const string Wiki = "https://api.star-citizen.wiki/api/";
    public const string Uex = "https://api.uexcorp.uk/2.0/";
    public string UexToken { private get; set; } = "";
    public Task<GuideSnapshot> LoadStarterGuide(CancellationToken ct) => Task.Run(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, StarterGuide.DataUrl);
        request.Headers.UserAgent.ParseAdd("JuvisAndroid/0.1.5");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Citizen Starter Guide: HTTP {(int)response.StatusCode}. Previous guide data retained.");
        using var doc = JsonDocument.Parse(await ReadBounded(response.Content, 16 * 1024 * 1024, timeout.Token).ConfigureAwait(false));
        return StarterGuide.Parse(doc.RootElement);
    }, ct);
    // Native Android transport may perform synchronous work while reading or disposing streams.
    // Keep the entire response lifetime off the caller's UI thread, even for completed awaits.
    public Task<JsonElement> Get(string url, CancellationToken ct) => Task.Run(() => GetCore(url, ct), ct);
    async Task<JsonElement> GetCore(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || (uri.Host != "api.star-citizen.wiki" && uri.Host != "api.uexcorp.uk"))
            throw new InvalidDataException("Unexpected API destination.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("JuvisAndroid/0.1.8 (+https://api.star-citizen.wiki)");
        if (uri.Host == "api.uexcorp.uk" && !string.IsNullOrWhiteSpace(UexToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UexToken);
        request.Headers.Add("X-Client-Version", "0.1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = uri.Host == "api.star-citizen.wiki" && response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "No matching Wiki record is available for this entry. Your cached details and saved states have been retained."
                : $"{uri.Host}: HTTP {(int)response.StatusCode}. " +
                    (uri.Host == "api.uexcorp.uk" && response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                        ? "Check your UEX token or access permissions." : "The source could not complete this request. Your cached data has been retained.");
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        var bytes = await ReadBounded(response.Content, 32 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(bytes);
        var status = doc.RootElement.S("status");
        if (status.Length > 0 && status != "ok") throw new InvalidDataException("UEX returned " + status + ". Previous cache retained.");
        var dataKind = doc.RootElement.Get("data").ValueKind;
        bool emptyUex = uri.Host == "api.uexcorp.uk" && status == "ok" && dataKind == JsonValueKind.Null;
        if (!emptyUex && dataKind is not (JsonValueKind.Object or JsonValueKind.Array)) throw new InvalidDataException("API response has no data.");
        return doc.RootElement.Clone();
    }
    public async Task<List<T>> WikiPages<T>(string route, Func<JsonElement,T> parse, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<T>();
        var first = Wiki + route + (route.Contains('?') ? "&" : "?") + "page[size]=100";
        string? next = first;
        int expectedPage = 1;
        var visited = new HashSet<string>();
        while (!string.IsNullOrEmpty(next))
        {
            if (!visited.Add(next) || visited.Count > 1000) throw new InvalidDataException("Invalid API pagination; previous cache retained.");
            progress?.Report($"Wiki {route.Split('?')[0]} · page {visited.Count}");
            var root = await Get(next, ct);
            if (root.Get("data").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected a paginated list.");
            result.AddRange(root.Get("data").Array().Select(parse));
            var current = root.Get("meta").Get("current_page").Number();
            var last = root.Get("meta").Get("last_page").Number();
            if (current.HasValue && last.HasValue)
            {
                if (current != expectedPage || last < current || last > 1000) throw new InvalidDataException("Unexpected page sequence; previous cache retained.");
                // The live API can append duplicate page[number] parameters to its links.
                // Rebuild from the original filtered route and validated metadata instead.
                next = current < last ? first + "&page[number]=" + (++expectedPage) : "";
                if (next.Length == 0 && root.Get("meta").Get("total").Number() is { } total && total != result.Count)
                    throw new InvalidDataException("Catalog count changed during sync; retry to keep a complete snapshot.");
            }
            else next = root.Get("links").S("next");
            if (next.Length > 0) await Task.Delay(650, ct);
        }
        return result;
    }
    public async Task<List<Item>> UexItems(IProgress<string>? progress, CancellationToken ct)
    {
        var categories = (await Get(Uex + "categories", ct)).Get("data").Array().Where(e => e.S("type") == "item").ToList();
        var result = new Dictionary<string, Item>();
        foreach (var category in categories)
        {
            progress?.Report("UEX · " + category.S("name"));
            var page = await Get(Uex + "items?id_category=" + Uri.EscapeDataString(category.S("id")), ct);
            foreach (var e in page.Get("data").Array()) { var i = ApiParser.UexItem(e); if (i.Id.Length > 0) result[i.Id] = i; }
            await Task.Delay(550, ct);
        }
        if (result.Count == 0) throw new InvalidDataException("UEX returned no items. Previous catalog retained.");
        return result.Values.ToList();
    }
    public async Task<Vehicle> LoadVehicle(Vehicle vehicle, CancellationToken ct)
    {
        var key = vehicle.Id.StartsWith("uex:") ? vehicle.Name : vehicle.Id;
        var root = await Get(Wiki + "vehicles/" + Uri.EscapeDataString(key) + "?include=ports,components", ct);
        var result = ApiParser.WikiVehicle(root.Get("data"));
        if (string.IsNullOrWhiteSpace(result.Id)) throw new InvalidDataException("Wiki returned no vehicle identity.");
        if (result.Ports.Count == 0) throw new InvalidDataException("No ports returned. Retry or check the Wiki record.");
        return result with { Id = vehicle.Id, Ground = vehicle.Ground };
    }
    public async Task<Item> LoadItem(Item item, CancellationToken ct)
    {
        var key = item.Id.StartsWith("uex:") ? item.Name : item.Id;
        var result = ApiParser.WikiItem((await Get(Wiki + "items/" + Uri.EscapeDataString(key) + "?include=ports", ct)).Get("data"));
        if (string.IsNullOrWhiteSpace(result.Id)) throw new InvalidDataException("Wiki returned no item identity.");
        return result with { Id = item.Id };
    }
    public async Task<Blueprint> LoadBlueprint(Blueprint b, CancellationToken ct) => ApiParser.WikiBlueprint((await Get(Wiki + "blueprints/" + Uri.EscapeDataString(b.Id), ct)).Get("data"));
    public static async Task<byte[]> ReadBounded(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Download exceeds size limit.");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + n > maxBytes) throw new InvalidDataException("Download exceeds size limit.");
            output.Write(buffer, 0, n);
        }
        return output.ToArray();
    }
}
