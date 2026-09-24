namespace Juvis.Core;

public record BuildApplication(List<(Port Port, Item NewItem, Item? Previous)> Changes, List<(Port Port, Item Item)> RemovedChildren, Dictionary<string,int> Missing);
public static class LocalInventory
{
    public static string Key(string location, string item) => Uri.EscapeDataString(location)+"/"+Uri.EscapeDataString(item);
    public static string AddLocation(UserState s, string name)
    {
        name=name.Trim();
        if (CatalogPresentation.Name(name)==null || name.Length>100) throw new InvalidDataException("Enter a location name of 1–100 characters.");
        var existing=s.StorageLocations.Values.FirstOrDefault(l=>l.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(existing!=null)return existing.Id;
        var id=Guid.NewGuid().ToString("N");s.StorageLocations[id]=new(id,name);return id;
    }
    public static void Adjust(UserState s,string location,Item item,int delta)
    {
        if(!s.StorageLocations.ContainsKey(location))throw new InvalidOperationException("Choose a storage location.");
        if(string.IsNullOrWhiteSpace(item.Id)||CatalogPresentation.ItemName(item)==null)throw new InvalidDataException("The item has no reliable identity or name.");
        var key=Key(location,item.Id);var old=s.Inventory.GetValueOrDefault(key)?.Quantity??0;
        var quantity=(long)old+delta;
        if(quantity<0||quantity>9999)throw new InvalidOperationException("Quantity must stay between 0 and 9,999.");
        // Keep zero-quantity entries as tombstones for idempotent backup merges.
        s.Inventory[key]=new(location,item,(int)quantity);
    }
    public static void Move(UserState s,string from,string to,Item item,int quantity)
    {
        if(from==to)throw new InvalidOperationException("Choose a different destination.");
        if(quantity<1||!s.StorageLocations.ContainsKey(to))throw new InvalidOperationException("Choose a destination and positive quantity.");
        if((s.Inventory.GetValueOrDefault(Key(from,item.Id))?.Quantity??0)<quantity)throw new InvalidOperationException("Not enough items at the source location.");
        if((long)(s.Inventory.GetValueOrDefault(Key(to,item.Id))?.Quantity??0)+quantity>9999)throw new InvalidOperationException("Destination quantity would exceed 9,999.");
        Adjust(s,from,item,-quantity);Adjust(s,to,item,quantity);
        Log(s,$"Moved {quantity} × {CatalogPresentation.ItemName(item)} from {s.StorageLocations[from].Name} to {s.StorageLocations[to].Name}.");
    }
    public static Vehicle EffectiveVehicle(UserState s,Vehicle stock)
    {
        if(!s.TrackedLoadouts.TryGetValue(stock.Id,out var tracked))return stock;
        return stock with { Ports=stock.Ports.Select(p=> {
            var result=tracked.TryGetValue(p.Id,out var slot)?p with { Installed=slot.Item }:p;
            if(tracked.Any(t=>t.Value.MountChildrenUnknown&&p.Id.StartsWith(t.Key+"/",StringComparison.Ordinal)))result=result with { Editable=false };
            return result;
        }).ToList() };
    }
    public static BuildApplication Preview(UserState s,Vehicle stock,IEnumerable<Item> catalog,string location)
    {
        if(!s.StorageLocations.ContainsKey(location))throw new InvalidOperationException("Choose where removed equipment should be stored.");
        var build=s.Builds.GetValueOrDefault(stock.Id)??[];var vehicle=EffectiveVehicle(s,stock);
        var items=catalog.GroupBy(i=>i.Id).ToDictionary(g=>g.Key,g=>g.First());
        var changes=new List<(Port Port,Item NewItem,Item? Previous)>();
        foreach(var entry in build)
        {
            var port=vehicle.Ports.FirstOrDefault(p=>p.Id==entry.PortId);
            if(port==null||!items.TryGetValue(entry.ItemId,out var item))throw new InvalidOperationException("Refresh the vehicle and candidates before applying this build.");
            if(port.Installed?.Id==item.Id)continue;
            if(LoadoutPresentation.Visibility(port)!=PortVisibility.Upgradeable||Compatibility.Check(port,item).Fit!=Fit.Direct)
                throw new InvalidOperationException("Recheck compatibility for "+CatalogPresentation.ItemName(item)+" before applying.");
            if(build.Any(e=>port.Id.StartsWith(e.PortId+"/",StringComparison.Ordinal)))throw new InvalidOperationException("Plan the parent mount separately from its child slots.");
            changes.Add((port,item,port.Installed));
        }
        if(changes.Count==0)throw new InvalidOperationException("No equipment changes to apply.");
        if(changes.Select(c=>c.Port.Id).Distinct().Count()!=changes.Count)throw new InvalidOperationException("The build contains duplicate slots.");
        var children=vehicle.Ports.Where(p=>changes.Any(c=>p.Id.StartsWith(c.Port.Id+"/",StringComparison.Ordinal))&&p.Installed!=null)
            .Select(p=>(Port:p,Item:p.Installed!)).ToList();
        var missing=new Dictionary<string,int>();
        foreach(var g in changes.GroupBy(c=>c.NewItem.Id))
        {
            var need=g.Count()-(s.Inventory.GetValueOrDefault(Key(location,g.Key))?.Quantity??0);
            if(need>0)missing[g.Key]=need;
        }
        // Check final quantities before any state is changed, including assembly contents.
        var deltas=new Dictionary<string,(Item Item,int Delta)>();
        void Delta(Item item,int n){var old=deltas.GetValueOrDefault(item.Id);deltas[item.Id]=(item,old.Delta+n);}
        foreach(var c in changes){if(c.Previous!=null)Delta(c.Previous,1);Delta(c.NewItem,-1);}
        foreach(var c in children)Delta(c.Item,1);
        foreach(var (id,d) in deltas)
        {
            var qty=(long)(s.Inventory.GetValueOrDefault(Key(location,id))?.Quantity??0)+d.Delta+missing.GetValueOrDefault(id);
            if(qty<0||qty>9999)throw new InvalidOperationException("Applying this build would exceed inventory quantity limits.");
        }
        return new(changes,children,missing);
    }
    public static void Apply(UserState s,Vehicle stock,IEnumerable<Item> catalog,string location,bool confirmExternalParts)
    {
        var preview=Preview(s,stock,catalog,location);
        if(preview.Missing.Count>0&&!confirmExternalParts)throw new InvalidOperationException("Some new parts are not in local inventory. Add them first or confirm that you supplied them separately.");
        s.TrackedLoadouts.TryAdd(stock.Id,[]);var tracked=s.TrackedLoadouts[stock.Id];
        // Consume before depositing so every new part's source is recorded consistently.
        foreach(var c in preview.Changes)
        {
            if((s.Inventory.GetValueOrDefault(Key(location,c.NewItem.Id))?.Quantity??0)>0)Adjust(s,location,c.NewItem,-1);
        }
        foreach(var c in preview.Changes)
        {
            if(c.Previous!=null && CatalogPresentation.ItemName(c.Previous)!=null)Adjust(s,location,c.Previous,1);
            tracked[c.Port.Id]=new(c.NewItem,stock.Ports.Any(p=>p.Id.StartsWith(c.Port.Id+"/",StringComparison.Ordinal)));
        }
        foreach(var c in preview.RemovedChildren){if(CatalogPresentation.ItemName(c.Item)!=null)Adjust(s,location,c.Item,1);tracked[c.Port.Id]=new(null);}
        s.Builds[stock.Id]=[];
        Log(s,$"Applied {preview.Changes.Count} change(s) to {CatalogPresentation.VehicleName(stock)} at {s.StorageLocations[location].Name}. Stored {preview.Changes.Count(c=>c.Previous!=null)+preview.RemovedChildren.Count} removed component(s); {preview.Missing.Values.Sum()} new part(s) supplied separately.");
    }
    public static void Log(UserState s,string text)
    {
        s.InventoryHistory.Add(new(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,text));
        if(s.InventoryHistory.Count>100)s.InventoryHistory.RemoveRange(0,s.InventoryHistory.Count-100);
    }
}
