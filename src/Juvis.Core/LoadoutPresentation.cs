using System.Globalization;
using System.Text.Json;

namespace Juvis.Core;

public enum PortVisibility { Upgradeable, Fixed, Unknown }
public static class LoadoutPresentation
{
    public static readonly string[] Categories = ["Weapons", "Avionics", "Liveries", "Propulsion", "Utility", "Misc."];
    static readonly Dictionary<string, string[]> Types = new() {
        ["Weapons"] = ["WeaponGun", "Turret", "Missile", "MissileLauncher", "Bomb", "BombLauncher", "WeaponDefensive", "EMP", "QuantumInterdictionGenerator"],
        ["Avionics"] = ["Radar", "Computer", "Blade", "Scanner", "Sensor", "QuantumDrive", "JumpDrive", "Navigation"],
        ["Liveries"] = ["Paints", "Paint", "Skin", "Livery", "Flair_Cockpit"],
        ["Propulsion"] = ["Thruster", "Engine", "FuelTank", "FuelIntake", "QuantumFuelTank"],
        ["Utility"] = ["TractorBeam", "TractorBeamArm", "WeaponMining", "MiningArm", "MiningModifier", "SalvageModifier", "WeaponRepair", "Utility", "CargoGrid"],
        ["Misc."] = ["Shield", "ShieldController", "PowerPlant", "Cooler", "LifeSupport", "Misc"]
    };
    static IEnumerable<string> PortTypes(Port p) => p.Types.Select(t => t.Type).Append(p.RawType).Append(p.Installed?.Type ?? "");
    static bool Eq(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    public static string Category(Port p) => Categories.FirstOrDefault(c => PortTypes(p).Any(t => Types[c].Any(s => Eq(s,t)))) ?? "Misc.";
    public static PortVisibility Visibility(Port p)
    {
        if (p.Bespoke || p.Editable == false) return PortVisibility.Fixed;
        if (new[] { "helper", "placeholder", "debug" }.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
            PortTypes(p).Any(t => new[] { "Door", "Seat", "SeatAccess", "Display", "Light", "Animation", "FlightController" }.Any(s => Eq(s,t))))
            return PortVisibility.Fixed;
        return p.Editable == true && PortTypes(p).Any(t => Types.Values.SelectMany(x => x).Any(s => Eq(s,t)))
            ? PortVisibility.Upgradeable : PortVisibility.Unknown;
    }
    public static Dictionary<string,List<Port>> Group(IEnumerable<Port> ports, bool advanced) => Categories.ToDictionary(c=>c,
        c=>ports.Where(p=>(advanced||Visibility(p)==PortVisibility.Upgradeable)&&Category(p)==c).ToList());
    public static string ContextName(Port p)
    {
        var parts=p.Id.Split('/').Select(n=>System.Text.RegularExpressions.Regex.Replace(n,"^hardpoint_","",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Replace('_',' ').Trim('$',' '));
        var safe=parts.Select(n=>CatalogPresentation.Name(n)).Where(n=>n!=null).ToArray();
        return safe.Length>0?string.Join(" / ",safe):CatalogPresentation.PortName(p);
    }
    public static bool ParentChanged(Port port, IEnumerable<BuildEntry> build) => build.Any(e=>port.Id.StartsWith(e.PortId+"/",StringComparison.Ordinal));
    public static Item? InstalledForComparison(Port port, IEnumerable<Item> items)
    {
        if (port.Installed == null) return null;
        // Match exact identity AND patch; never borrow statistics from a similarly named variant.
        return items.FirstOrDefault(i=>i.Id==port.Installed.Id && i.Version==port.Version && i.Performance.HasData) ?? port.Installed;
    }
}

public record WeaponPerformance
{
    public decimal? BurstDps { get; init; }
    public decimal? SustainedDps { get; init; }
    public decimal? Alpha { get; init; }
    public decimal? EffectiveRange { get; init; }
    public decimal? MaximumRange { get; init; }
    public decimal? Velocity { get; init; }
    public decimal? Rpm { get; init; }
    public decimal? AmmoCapacity { get; init; }
    public decimal? CapacitorShots { get; init; }
    public decimal? CapacitorRegen { get; init; }
    public decimal? CapacitorDelay { get; init; }
    public decimal? CostPerShot { get; init; }
    public decimal? PowerDemand { get; init; }
    public string DamageType { get; init; } = "";
    public bool Energy { get; init; }
    public bool HasData => BurstDps != null || Alpha != null || MaximumRange != null || Rpm != null;
    public static string Value(decimal? value, string unit="") => value?.ToString("0.##",CultureInfo.InvariantCulture)+ (value == null ? "Data unavailable" : unit);
    public string AmmoEnergy => Energy
        ? $"Capacitor {Value(CapacitorShots," shots")}\nRegen {Value(CapacitorRegen," shots/s")}\nDelay {Value(CapacitorDelay," s")}\nCost/shot {Value(CostPerShot," source units")}" 
        : Value(AmmoCapacity," rounds");
    public static WeaponPerformance Parse(JsonElement item)
    {
        var w=item.Get("vehicle_weapon"); var d=w.Get("damage"); var cap=w.Get("capacitor");
        var a=w.Get("ammunition").ValueKind==JsonValueKind.Object?w.Get("ammunition"):item.Get("ammunition");
        decimal? N(JsonElement e) => e.Number() is >=0 and var n ? n : null;
        var alpha=d.Get("alpha"); if(alpha.ValueKind!=JsonValueKind.Object)alpha=a.Get("impact_damage_map");
        var types=alpha.ValueKind==JsonValueKind.Object?alpha.EnumerateObject().Where(x=>N(x.Value)>0).Select(x=>x.Name switch { "physical"=>"Ballistic","energy"=>"Laser / energy",_=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(x.Name) }).ToArray():[];
        var type=string.Join(" + ",types); if(type.Length==0)type=CatalogPresentation.Name(w.S("type"))??"";
        var energy=new[] {"laser","energy","distortion"}.Any(s=>type.Contains(s,StringComparison.OrdinalIgnoreCase));
        return new() { BurstDps=N(d.Get("burst")),SustainedDps=N(d.Get("sustained_60s")),Alpha=N(d.Get("alpha_total"))??N(w.Get("damage_per_shot")),
            EffectiveRange=N(w.Get("effective_range")),MaximumRange=N(a.Get("range"))??N(w.Get("range")),Velocity=N(a.Get("speed")),Rpm=N(w.Get("rpm")),
            AmmoCapacity=energy?null:N(w.Get("capacity"))??N(a.Get("capacity")),CapacitorShots=N(cap.Get("max_ammo_load")),CapacitorRegen=N(cap.Get("regen_per_second")),
            CapacitorDelay=N(cap.Get("cooldown")),CostPerShot=N(cap.Get("costs_per_shot")),PowerDemand=N(item.Get("resource_network").Get("usage").Get("power").Get("max")),DamageType=type,Energy=energy };
    }
}
