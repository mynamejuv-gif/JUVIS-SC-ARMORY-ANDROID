using System.Text.Json;

namespace Juvis.Core;

public sealed class LocalStore(string directory)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, MaxDepth = 48 };
    public async Task<T?> Read<T>(string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path)) return default;
        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(file, Json);
    }
    public async Task Write<T>(string name, T value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var temp = path + ".tmp";
        await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(file, value, Json);
            await file.FlushAsync();
        }
        File.Move(temp, path, true);
    }
}
public static class Backup
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static string Export(UserState state) => JsonSerializer.Serialize(new BackupEnvelope("juvis-android", 1, DateTimeOffset.UtcNow, state), LocalStore.Json);
    public static UserState Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Backup exceeds 8 MB.");
        var b = JsonSerializer.Deserialize<BackupEnvelope>(json, LocalStore.Json) ?? throw new InvalidDataException("Empty backup.");
        if (b.Format != "juvis-android" || b.SchemaVersion != 1) throw new InvalidDataException("Unsupported backup format. Use a JUVIS Android v1 backup; desktop migration needs the original schema.");
        var s = b.State;
        if (s is null || s.Gear is null || s.Blueprints is null || s.Vehicles is null || s.CraftPlan is null || s.Builds is null)
            throw new InvalidDataException("Backup is missing required state.");
        if (s.StorageLocations is null || s.Inventory is null || s.TrackedLoadouts is null || s.InventoryHistory is null ||
            s.StorageLocations.Any(x=>x.Value is null || x.Key!=x.Value.Id || string.IsNullOrWhiteSpace(x.Key) || CatalogPresentation.Name(x.Value.Name)==null) ||
            s.Inventory.Any(x=>x.Value is null || x.Value.Item is null || string.IsNullOrWhiteSpace(x.Value.Item.Id) || x.Value.Quantity<0 || x.Value.Quantity>9999 || !s.StorageLocations.ContainsKey(x.Value.LocationId) || x.Key!=LocalInventory.Key(x.Value.LocationId,x.Value.Item.Id)) ||
            s.TrackedLoadouts.Any(x=>x.Value is null || x.Value.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value is null || (p.Value.Item is not null && string.IsNullOrWhiteSpace(p.Value.Item.Id)))) ||
            s.InventoryHistory.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Id) || x.Description is null))
            throw new InvalidDataException("Backup contains invalid inventory or tracked loadouts.");
        if (s.Gear.Count > 100000 || s.CraftPlan.Any(x => x.Value < 1 || x.Value > 999) || s.Gear.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value is null)
            || s.Builds.Any(x => x.Value is null || x.Value.Any(e => e is null || string.IsNullOrWhiteSpace(e.PortId) || string.IsNullOrWhiteSpace(e.ItemId))))
            throw new InvalidDataException("Backup contains invalid values.");
        return s;
    }
    public static UserState Merge(UserState current, UserState imported)
    {
        var result = Parse(Export(current));
        foreach (var (k, v) in imported.Gear) result.Gear[k] = v;
        result.Blueprints.UnionWith(imported.Blueprints);
        result.Vehicles.UnionWith(imported.Vehicles);
        foreach (var (k, v) in imported.CraftPlan) result.CraftPlan[k] = v;
        foreach (var (k, v) in imported.Builds) result.Builds[k] = v;
        foreach (var (k, v) in imported.StorageLocations) result.StorageLocations[k] = v;
        // Imported quantities replace matching rows; never sum them on repeated imports.
        foreach (var (k, v) in imported.Inventory) result.Inventory[k] = v;
        foreach (var (k, v) in imported.TrackedLoadouts) result.TrackedLoadouts[k] = v;
        result.InventoryHistory = result.InventoryHistory.Concat(imported.InventoryHistory).DistinctBy(e=>e.Id).OrderBy(e=>e.At).TakeLast(100).ToList();
        result.LastModule = imported.LastModule;
        return result;
    }
}
