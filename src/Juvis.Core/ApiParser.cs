using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Juvis.Core;

public static class ApiParser
{
    public static JsonElement Get(this JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    public static string Text(this JsonElement e) => e.ValueKind is JsonValueKind.String ? e.GetString() ?? "" : e.ValueKind is JsonValueKind.Number ? e.GetRawText() : "";
    public static string S(this JsonElement e, string key) => e.Get(key).Text();
    public static IEnumerable<JsonElement> Array(this JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
    public static string[] Strings(this JsonElement e) => e.Array().Select(Text).Where(s => s.Length > 0).ToArray();
    public static decimal? Number(this JsonElement e) => decimal.TryParse(e.Text(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    public static bool? Bool(this JsonElement e) => e.ValueKind == JsonValueKind.True || e.Text() == "1" ? true : e.ValueKind == JsonValueKind.False || e.Text() == "0" ? false : null;
    static int? Int(JsonElement e) => e.Number() is { } n && n >= int.MinValue && n <= int.MaxValue ? (int)n : null;
    public static List<JsonElement> Data(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.Get("data").ValueKind != JsonValueKind.Array) throw new InvalidDataException("API did not return a catalog list.");
        return root.Get("data").Array().Select(e => e.Clone()).ToList();
    }
    public static Item WikiItem(JsonElement e)
    {
        var stats = new Dictionary<string, string>();
        foreach (var (label, val) in new[] {
            ("Shield HP", e.Get("shield").Get("max_shield_health")), ("Shield regeneration", e.Get("shield").Get("regeneration")),
            ("Quantum speed (m/s)", e.Get("quantum_drive").Get("standard_jump").Get("drive_speed")),
            ("Power usage", e.Get("resource_network").Get("usage").Get("power").Get("max")),
            ("Coolant usage", e.Get("resource_network").Get("usage").Get("coolant").Get("max")), ("Mass (kg)", e.Get("mass")) })
            if (val.Text().Length > 0) stats[label] = val.Text();
        var description = e.Get("description");
        var img = e.Get("images").Array().FirstOrDefault();
        var personal = e.Get("personal_weapon");
        var vehicle = e.Get("vehicle_weapon");
        var ammo = e.Get("ammunition").ValueKind == JsonValueKind.Object ? e.Get("ammunition") :
            personal.Get("ammunition").ValueKind == JsonValueKind.Object ? personal.Get("ammunition") : vehicle.Get("ammunition");
        var magazinePort = e.Get("ports").Array().FirstOrDefault(p =>
            p.Get("compatible_types").Array().Any(t => t.S("sub_types").Equals("Magazine", StringComparison.OrdinalIgnoreCase) ||
                t.Get("sub_types").Strings().Any(s => s.Equals("Magazine", StringComparison.OrdinalIgnoreCase))));
        var equippedMagazine = magazinePort.Get("equipped_item");
        var ammoType = WeaponAmmoType(e, personal, vehicle, ammo, description.S("en_EN"));
        var ammunitionNotApplicable = AmmunitionNotApplicable(e, personal, vehicle, description.S("en_EN"));
        var capacity = PositiveInt(personal.Get("magazine_size")) ?? PositiveInt(personal.Get("capacity")) ??
            PositiveInt(vehicle.Get("capacity")) ?? PositiveInt(ammo.Get("capacity"));
        var energyCapacity = ammoType.Equals("Energy", StringComparison.OrdinalIgnoreCase)
            ? PositiveInt(vehicle.Get("capacitor").Get("max_ammo_load")) ?? capacity : null;
        var caliber = First(DescriptionValue(e, "Caliber", "Cartridge", "Ammunition Caliber"), CaliberFromDescription(description.S("en_EN")));
        var magazineName = CatalogPresentation.Name(equippedMagazine.S("name")) ?? "";
        var alternativeNames = new[] { e.S("display_name"), e.S("name_full"), e.S("full_name"), e.S("localized_name") }
            .Where(CatalogPresentation.HasName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var compatibleMagazineIds = new[] { magazinePort.S("equipped_item_uuid"), equippedMagazine.S("uuid") }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new Item { Id = e.S("uuid"), Name = e.S("name"), Category = First(e.S("classification_label"), e.S("type_label"), e.S("type")),
            Manufacturer = CatalogPresentation.Name(e.Get("manufacturer").S("name")) ?? "", Type = e.S("type"), SubType = e.S("sub_type"), Size = Int(e.Get("size")),
            Grade = e.S("grade"), Class = e.S("class"), Description = First(description.S("en_EN"), description.Text()),
            ImageUrl = First(img.S("thumbnail_url"), img.S("original_url")), WebUrl = e.S("web_url"), Version = e.S("version"),
            Source = "Star Citizen Wiki", Model = First(e.S("model_name"), e.S("model")), AlternativeNames = alternativeNames,
            Tags = e.Get("tags").Strings(), RequiredTags = e.Get("required_tags").Strings(),
            RestrictionsKnown = e.Get("tags").ValueKind == JsonValueKind.Array && e.Get("required_tags").ValueKind == JsonValueKind.Array,
            BuyPrice = e.Get("uex_prices").Get("purchase").Array().Select(p => p.Get("price_buy").Number()).Where(p => p > 0).DefaultIfEmpty(null).Min(),
            Performance = WeaponPerformance.Parse(e),
            Shops = e.Get("uex_prices").Get("purchase").Array().Select(p => $"{p.S("terminal_name")} · {p.Get("price_buy").Number():N0} aUEC · reported {p.S("date_updated")}").ToList(), Stats = stats,
            Ammunition = new AmmunitionInfo {
                NotApplicable = ammunitionNotApplicable,
                Caliber = caliber, AmmoType = ammoType,
                MagazineType = First(personal.S("magazine_type"), magazinePort.ValueKind == JsonValueKind.Object ? "Magazine" : ""),
                MagazineName = magazineName, Capacity = capacity,
                CompatibleMagazineIds = compatibleMagazineIds,
                CompatibleMagazineNames = magazineName.Length > 0 ? [magazineName] : [],
                EnergySource = DescriptionValue(e, "Energy Source", "Battery", "Power Cell", "Energy Cell"),
                EnergyCapacity = energyCapacity,
                EnergyRegenerationPerSecond = vehicle.Get("capacitor").Get("regen_per_second").Number(),
                MagazineMinSize = Int(magazinePort.Get("sizes").Get("min")), MagazineMaxSize = Int(magazinePort.Get("sizes").Get("max")),
                MagazinePortRequiredTags = magazinePort.Get("required_tags").Strings(), MagazinePortTags = magazinePort.Get("port_tags").Strings()
            } };
    }
    public static Item UexItem(JsonElement e) => new() { Id = First(e.S("uuid"), "uex:item:" + e.S("id")), Name = First(e.S("name"), e.S("name_full")), Category = First(e.S("category"), e.S("section")),
        Manufacturer = CatalogPresentation.Name(e.S("company_name")) ?? "", Model = First(e.S("model"), e.S("model_name")),
        AlternativeNames = new[] { e.S("name_full"), e.S("display_name") }.Where(CatalogPresentation.HasName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Type = e.S("type"), SubType = e.S("sub_type"), Size = Int(e.Get("size")), ImageUrl = e.S("screenshot"), WebUrl = e.S("wiki"), Version = e.S("game_version"), Source = "UEX" };
    public static Commodity UexCommodity(JsonElement e) => new(First(e.S("uuid"), "uex:commodity:" + e.S("id")), e.S("name"), e.S("code"),
        e.Get("price_buy").Number(), e.Get("price_sell").Number(), e.Get("is_illegal").Bool() == true, "uex:commodity:" + e.S("id"));
    public static Blueprint WikiBlueprint(JsonElement e) => new(e.S("uuid"),
        new[] { e.S("output_name"), e.Get("output").S("name") }.FirstOrDefault(CatalogPresentation.HasName) ?? e.S("output_name"),
        e.S("game_version"), e.Get("craft_time_seconds").Number() ?? 0,
        e.Get("is_available_by_default").Bool() == true,
        e.Get("ingredients").Array().Select(i => new Ingredient(First(i.S("resource_type_uuid"), i.S("item_uuid"), i.S("name")), i.S("name"),
            i.Get("quantity_scu").Number() ?? i.Get("quantity").Number() ?? 0, i.Get("quantity_scu").Number() is null ? "units" : "SCU")).ToList(),
        e.Get("unlocking_missions").Array().Select(m => m.S("title")).Where(s => s.Length > 0).Distinct().ToList(), e.S("web_url"))
        { Key = e.S("key"), MissionsChecked = e.Get("unlocking_missions").ValueKind == JsonValueKind.Array };
    public static Vehicle UexVehicle(JsonElement e) => new(First(e.S("uuid"), "uex:vehicle:" + e.S("id")), First(e.S("name_full"), e.S("name")),
        CatalogPresentation.Name(e.S("company_name")) ?? "", e.Get("is_ground_vehicle").Bool() == true, e.S("url_photo"), e.S("game_version"), [])
        { Model = First(e.S("model"), e.S("model_name")), AlternativeNames = new[] { e.S("display_name"), e.S("name") }.Where(CatalogPresentation.HasName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    public static Vehicle WikiVehicle(JsonElement e)
    {
        var version = First(e.S("version"), e.S("game_version"));
        var ports = new List<Port>();
        void Visit(JsonElement p, string prefix, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Vehicle port nesting exceeds supported depth.");
            var id = prefix + p.S("name");
            Item? installed = p.Get("equipped_item").ValueKind == JsonValueKind.Object ? WikiItem(p.Get("equipped_item")) : null;
            if (installed != null && CatalogPresentation.ItemName(installed) == null) installed = null;
            ports.Add(new Port(id, p.S("name"), p.Get("editable").Bool(), Int(p.Get("sizes").Get("min")), Int(p.Get("sizes").Get("max")),
                p.Get("compatible_types").Array().Select(t => new PortType(t.S("type"), t.Get("sub_types").Strings())).ToList(),
                p.Get("required_tags").Strings(), p.Get("port_tags").Strings(), installed, First(p.S("version"), installed?.Version ?? "", version))
                { DisplayName = p.S("display_name"), RawType = p.S("type"), Bespoke = p.Get("is_bespoke").Bool() == true || p.Get("bespoke").Bool() == true });
            foreach (var child in p.Get("ports").Array()) Visit(child, id + "/", depth + 1);
        }
        foreach (var p in e.Get("ports").Array()) Visit(p, "", 0);
        return new(e.S("uuid"), e.S("name"), CatalogPresentation.Name(e.Get("manufacturer").S("name")) ?? "", e.Get("is_ground_vehicle").Bool() == true,
            e.Get("images").Array().FirstOrDefault().S("thumbnail_url"), First(version, ports.FirstOrDefault(p => p.Version.Length > 0)?.Version ?? ""), ports)
            { Model = First(e.S("model"), e.S("model_name")), AlternativeNames = new[] { e.S("display_name"), e.S("name_full") }.Where(CatalogPresentation.HasName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }
    static int? PositiveInt(JsonElement e) => Int(e) is int n && n > 0 ? n : null;
    static string DescriptionValue(JsonElement e, params string[] labels)
    {
        foreach (var row in e.Get("description_data").Array())
            if (labels.Any(label => row.S("name").Equals(label, StringComparison.OrdinalIgnoreCase)))
                return CatalogPresentation.Name(row.S("value"), row.S("type")) ?? "";
        return "";
    }
    static string CaliberFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        var patterns = new[] {
            @"(?<value>(?:\.\d+|\d+(?:\.\d+)?)\s*(?:mm|millimet(?:er|re)s?|gauge|caliber))[-\s]*(?:rounds?|cartridges?|shells?|ammunition)",
            @"(?:caliber|calibre)\s*(?<value>\.\d+|\d+(?:\.\d+)?)\s*(?:rounds?|cartridges?|shells?)"
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(description, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) return Regex.Replace(match.Groups["value"].Value.Trim(), @"(?<=\d)mm\b", " mm", RegexOptions.IgnoreCase);
        }
        return "";
    }
    static string WeaponAmmoType(JsonElement item, JsonElement personal, JsonElement vehicle, JsonElement ammo, string description)
    {
        var explicitType = CatalogPresentation.Name(personal.S("class"), personal.S("ammo_type"), vehicle.S("ammo_type"));
        if (explicitType != null) return explicitType;
        var weaponType = vehicle.S("type");
        if (weaponType.Contains("ballistic", StringComparison.OrdinalIgnoreCase)) return "Ballistic";
        if (new[] { "laser", "energy", "plasma", "distortion", "tachyon" }.Any(x => weaponType.Contains(x, StringComparison.OrdinalIgnoreCase))) return "Energy";
        var damages = ammo.Get("impact_damage").Array().Where(d => d.Get("damage").Number() > 0).Select(d => d.S("name")).ToArray();
        var physical = damages.Any(d => d.Equals("physical", StringComparison.OrdinalIgnoreCase));
        var energy = damages.Any(d => d.Equals("energy", StringComparison.OrdinalIgnoreCase));
        if (physical || energy) return physical && energy ? "Mixed" : physical ? "Ballistic" : "Energy";
        if (item.S("type").Equals("WeaponGun", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(description, @"\b(?:laser|energy|plasma|distortion|tachyon)\b.*\b(?:gun|cannon|repeater|weapon|beam)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Energy";
        return "";
    }
    static bool AmmunitionNotApplicable(JsonElement item, JsonElement personal, JsonElement vehicle, string description)
    {
        var kind = First(personal.S("type"), vehicle.S("type"), item.S("classification_label"), item.S("sub_type"), item.S("type_label"));
        return new[] { "knife", "blade", "grenade", "flare", "gadget", "tractor", "mining", "salvage", "medical", "repair" }
            .Any(value => kind.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(description, @"\b(?:tractor|mining|salvage|repair|medical)\s+(?:beam|tool|device)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
    public static string First(params string[] choices) => choices.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
}
