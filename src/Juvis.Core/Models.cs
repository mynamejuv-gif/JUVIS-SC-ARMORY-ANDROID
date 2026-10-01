using System.Text.Json.Serialization;
namespace Juvis.Core;

public record Item
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Type { get; init; } = "";
    public string SubType { get; init; } = "";
    public int? Size { get; init; }
    public string Grade { get; init; } = "";
    public string Class { get; init; } = "";
    public string Description { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string WebUrl { get; init; } = "";
    public string Version { get; init; } = "";
    public string Source { get; init; } = "";
    public string Model { get; init; } = "";
    public string[] AlternativeNames { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string[] RequiredTags { get; init; } = [];
    public bool RestrictionsKnown { get; init; }
    public decimal? BuyPrice { get; init; }
    public List<string> Shops { get; init; } = [];
    public Dictionary<string, string> Stats { get; init; } = [];
    public AmmunitionInfo Ammunition { get; init; } = new();
    public WeaponPerformance Performance { get; init; } = new();
    public override string ToString() => Name;
}
public record AmmunitionInfo
{
    public bool NotApplicable { get; init; }
    public string Caliber { get; init; } = "";
    public string AmmoType { get; init; } = "";
    public string MagazineType { get; init; } = "";
    public string MagazineName { get; init; } = "";
    public int? Capacity { get; init; }
    public string[] CompatibleMagazineIds { get; init; } = [];
    public string[] CompatibleMagazineNames { get; init; } = [];
    public string[] CompatibleAmmunitionNames { get; init; } = [];
    public string EnergySource { get; init; } = "";
    public int? EnergyCapacity { get; init; }
    public decimal? EnergyRegenerationPerSecond { get; init; }
    public int? MagazineMinSize { get; init; }
    public int? MagazineMaxSize { get; init; }
    public string[] MagazinePortRequiredTags { get; init; } = [];
    public string[] MagazinePortTags { get; init; } = [];
}
public record Commodity(string Id, string Name, string Code, decimal? Buy, decimal? Sell, bool Illegal, string ImageKey = "")
{
    public string[] AlternativeNames { get; init; } = [];
}
public record Ingredient(string Id, string Name, decimal Quantity, string Unit);
public record Blueprint(string Id, string Name, string Version, decimal Seconds, bool DefaultUnlocked,
    List<Ingredient> Ingredients, List<string> Missions, string WebUrl)
{
    public string Key { get; init; } = "";
    public bool MissionsChecked { get; init; }
}
public record PortType(string Type, string[] SubTypes);
public record Port(string Id, string Name, bool? Editable, int? MinSize, int? MaxSize,
    List<PortType> Types, string[] RequiredTags, string[] Tags, Item? Installed, string Version)
{
    public string DisplayName { get; init; } = "";
    public string RawType { get; init; } = "";
    public bool Bespoke { get; init; }
}
public record Vehicle(string Id, string Name, string Manufacturer, bool Ground, string ImageUrl, string Version, List<Port> Ports)
{
    public string Model { get; init; } = "";
    public string[] AlternativeNames { get; init; } = [];
    public override string ToString() => Name;
}
public record GearState(bool Owned = false, bool Need = false, bool Favorite = false, string Name = "");
public record BuildEntry(string PortId, string ItemId, string ItemName, string Version);
public class UserState
{
    public Dictionary<string, StorageLocation> StorageLocations { get; set; } = [];
    public Dictionary<string, StoredItem> Inventory { get; set; } = [];
    public Dictionary<string, Dictionary<string, TrackedSlot>> TrackedLoadouts { get; set; } = [];
    public List<InventoryEvent> InventoryHistory { get; set; } = [];
    public Dictionary<string, GearState> Gear { get; set; } = [];
    public HashSet<string> Blueprints { get; set; } = [];
    public HashSet<string> Vehicles { get; set; } = [];
    public Dictionary<string, int> CraftPlan { get; set; } = [];
    public Dictionary<string, List<BuildEntry>> Builds { get; set; } = [];
    public ZeroHeroRunState ZeroHero { get; set; } = new();
    [JsonIgnore] public bool HasZeroHeroData { get; set; }
    public string LastModule { get; set; } = "Catalog";
}
public record StorageLocation(string Id, string Name);
public record StoredItem(string LocationId, Item Item, int Quantity);
public record TrackedSlot(Item? Item, bool MountChildrenUnknown = false);
public record InventoryEvent(string Id, DateTimeOffset At, string Description);
public class Catalog
{
    public GuideSnapshot Guide { get; set; } = new("", []);
    public List<Item> Items { get; set; } = [];
    public List<Commodity> Commodities { get; set; } = [];
    public List<Blueprint> Blueprints { get; set; } = [];
    public List<Vehicle> Vehicles { get; set; } = [];
    public Dictionary<string, DateTimeOffset> Synced { get; set; } = [];
    public bool StarterSnapshot { get; set; } = true;
}
public record BackupEnvelope(string Format, int SchemaVersion, DateTimeOffset CreatedUtc, UserState State);
public enum Fit { Direct, CheckRequired, Incompatible, Fixed }
public record FitResult(Fit Fit, string Reason);
