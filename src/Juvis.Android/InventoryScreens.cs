using Android.App;
using Android.Text;
using Android.Widget;
using Juvis.Core;
namespace Juvis.AndroidApp;

public partial class MainActivity
{
    string inventoryLocation="";
    EditText Entry(string hint,string value="",bool numeric=false)
    {
        var edit=new EditText(this){Hint=hint,Text=value,TextSize=16};
        edit.SetTextColor(ink);edit.SetHintTextColor(muted);edit.SetMinHeight(Dp(52));edit.SetSingleLine(true);
        if(numeric)edit.InputType=InputTypes.ClassNumber;
        return edit;
    }
    static int Quantity(EditText edit)
    {
        if(!int.TryParse(edit.Text,out var n)||n<1||n>9999)throw new InvalidOperationException("Enter a quantity from 1 to 9,999.");
        return n;
    }
    Task<bool> Confirm(string title,string message)
    {
        var completion=new TaskCompletionSource<bool>();
        var dialog=new AlertDialog.Builder(this).SetTitle(title)!.SetMessage(message)!
            .SetPositiveButton("Confirm",(_,_)=>completion.TrySetResult(true))!
            .SetNegativeButton("Cancel",(_,_)=>completion.TrySetResult(false))!.Create()!;
        dialog.CancelEvent+=(_,_)=>completion.TrySetResult(false);dialog.Show();return completion.Task;
    }
    void AddLocationForm(LinearLayout target,Action after)
    {
        var entry=Entry("Add location: station, city or outpost");target.AddView(entry);
        target.AddView(Button("Save storage location",async()=>{await armory.Change(s=>inventoryLocation=LocalInventory.AddLocation(s,entry.Text??""));after();}));
    }
    string SelectLocation(LinearLayout target,Action<string> changed,string? exclude=null)
    {
        var locations=armory.State.StorageLocations.Values.Where(l=>l.Id!=exclude).OrderBy(l=>l.Name).ToArray();
        if(locations.Length==0)return "";
        var selected=locations.FirstOrDefault(l=>l.Id==inventoryLocation)??locations[0];
        Choice(target,locations.Select(l=>l.Name).ToArray(),selected.Name,name=>changed(locations.First(l=>l.Name==name).Id));
        return selected.Id;
    }
    void InventoryScreen()
    {
        Title("Local inventory","Your equipment by location. Manually tracked in JUVIS; not connected to in-game storage.");
        var locations=armory.State.StorageLocations.Values.OrderBy(l=>l.Name).ToArray();
        var filter=locations.FirstOrDefault(l=>l.Id==inventoryLocation)?.Name??"All locations";
        Choice(body,new[]{"All locations"}.Concat(locations.Select(l=>l.Name)).ToArray(),filter,name=>{inventoryLocation=locations.FirstOrDefault(l=>l.Name==name)?.Id??"";Draw();});
        var matches=armory.State.Inventory.Values.Where(x=>x.Quantity>0&&(inventoryLocation.Length==0||x.LocationId==inventoryLocation)&&CatalogPresentation.ItemName(x.Item)!=null).OrderBy(x=>CatalogPresentation.ItemName(x.Item)).ToList();
        var total=matches.Sum(x=>(long)x.Quantity);
        body.AddView(Label($"{total} {(total==1?"item":"items")} · {matches.Count} equipment {(matches.Count==1?"entry":"entries")}",13,muted));
        var listing=new LinearLayout(this){Orientation=Orientation.Vertical};body.AddView(listing);
        if(matches.Count==0)listing.AddView(Label("No equipment stored here yet. Add equipment from the catalog or apply a vehicle build at this location.",15,muted));
        else Pager(listing,matches,x=>{
            var card=Card();card.AddView(Label(CatalogPresentation.ItemName(x.Item)!,19));
            card.AddView(Label($"Quantity {x.Quantity} · {armory.State.StorageLocations[x.LocationId].Name}",15,cyan));
            card.AddView(Button("Details",()=>{ItemScreen(armory.Catalog.Items.FirstOrDefault(i=>i.Id==x.Item.Id)??x.Item,Draw);return Task.CompletedTask;}));
            card.AddView(Button("Move / adjust quantity",()=>{ManageStoredItem(x);return Task.CompletedTask;}));listing.AddView(card);
        },Draw);
        body.AddView(Button("Add equipment from catalog",()=>{Navigate("Catalog");return Task.CompletedTask;},true));
        body.AddView(Label("Open any item, then choose Add to local inventory.",13,muted));
        AddLocationForm(body,Draw);
        body.AddView(Button("Export inventory & all saved data",()=>{ExportBackup();return Task.CompletedTask;}));
        body.AddView(Label("Recent activity",19));
        foreach(var e in armory.State.InventoryHistory.TakeLast(10).Reverse())body.AddView(Label($"{e.At.ToLocalTime():g}\n{e.Description}",12,muted));
    }
    void AddToInventory(Item item,Action returnTo)
    {
        Detail("Add to local inventory",CatalogPresentation.ItemName(item)??"Equipment",returnTo);
        if(armory.State.StorageLocations.Count==0){body.AddView(Label("Create your first storage location."));AddLocationForm(body,()=>AddToInventory(item,returnTo));return;}
        body.AddView(Label("Storage location",14,cyan));string location="";location=SelectLocation(body,id=>location=id);
        var count=Entry("Quantity","1",true);body.AddView(count);
        body.AddView(Button("Add equipment here",async()=>{
            var n=Quantity(count);await armory.Change(s=>{LocalInventory.Adjust(s,location,item,n);LocalInventory.Log(s,$"Added {n} × {CatalogPresentation.ItemName(item)} at {s.StorageLocations[location].Name}.");});
            inventoryLocation=location;Navigate("Inventory");
        },true));
        AddLocationForm(body,()=>AddToInventory(item,returnTo));
    }
    void ManageStoredItem(StoredItem entry)
    {
        Detail(CatalogPresentation.ItemName(entry.Item)??"Stored equipment",$"{entry.Quantity} at {armory.State.StorageLocations[entry.LocationId].Name}",Draw);
        var count=Entry("Quantity","1",true);body.AddView(count);
        body.AddView(Button("Remove this quantity from tracking",async()=>{
            var n=Quantity(count);if(!await Confirm("Remove tracked equipment?",$"Remove {n} × {CatalogPresentation.ItemName(entry.Item)} from {armory.State.StorageLocations[entry.LocationId].Name}?"))return;
            await armory.Change(s=>{LocalInventory.Adjust(s,entry.LocationId,entry.Item,-n);LocalInventory.Log(s,$"Removed {n} × {CatalogPresentation.ItemName(entry.Item)} from {s.StorageLocations[entry.LocationId].Name}.");});Draw();
        }));
        body.AddView(Label("Move to another location",18));string destination="";destination=SelectLocation(body,id=>destination=id,entry.LocationId);
        if(destination.Length>0)body.AddView(Button("Move selected quantity",async()=>{var n=Quantity(count);await armory.Change(s=>LocalInventory.Move(s,entry.LocationId,destination,entry.Item,n));inventoryLocation=destination;Draw();},true));
        else body.AddView(Label("Add another location to transfer equipment.",13,muted));
        AddLocationForm(body,()=>ManageStoredItem(entry));
    }
    void ApplyBuildScreen(Vehicle stock)
    {
        Detail("Apply build & store old parts",CatalogPresentation.VehicleName(stock)??"Vehicle",()=>VehicleScreen(stock,true));
        body.AddView(Label("Choose the location where you are fitting the new equipment. Available local parts are consumed first. Replaced equipment is deposited here. This records your manual in-game changes in JUVIS.",14,muted));
        if(armory.State.StorageLocations.Count==0){AddLocationForm(body,()=>ApplyBuildScreen(stock));return;}
        string location="";var previewPanel=new LinearLayout(this){Orientation=Orientation.Vertical};
        void Preview()
        {
            previewPanel.RemoveAllViews();
            try {
                var plan=LocalInventory.Preview(armory.State,stock,armory.Catalog.Items,location);
                foreach(var c in plan.Changes)previewPanel.AddView(Label($"{LoadoutPresentation.ContextName(c.Port)}\n{CatalogPresentation.ItemName(c.Previous??new())??"Empty / unidentified"} → {CatalogPresentation.ItemName(c.NewItem)}",14));
                if(plan.RemovedChildren.Count>0)previewPanel.AddView(Label($"Also stores {plan.RemovedChildren.Count} nested component(s). Child slots on replacement mounts remain unavailable until their layout can be verified.",14,cyan));
                foreach(var (id,n) in plan.Missing)previewPanel.AddView(Label($"Need {n} × {CatalogPresentation.ItemName(plan.Changes.First(c=>c.NewItem.Id==id).NewItem)} from outside this inventory.",13,cyan));
                var supplied=new CheckBox(this){Text="I supplied the missing new parts separately",Checked=false};supplied.SetTextColor(ink);supplied.SetMinHeight(Dp(48));
                if(plan.Missing.Count>0)previewPanel.AddView(supplied);
                previewPanel.AddView(Button("Apply build at this location",async()=>{
                    if(plan.Missing.Count>0&&!supplied.Checked)throw new InvalidOperationException("Add the missing parts to inventory or confirm that you supplied them separately.");
                    var name=armory.State.StorageLocations[location].Name;
                    if(!await Confirm("Record this upgrade?",$"Apply {plan.Changes.Count} component change(s) to {CatalogPresentation.VehicleName(stock)} and store the removed parts at {name}?"))return;
                    await armory.Change(s=>LocalInventory.Apply(s,stock,armory.Catalog.Items,location,supplied.Checked));
                    inventoryLocation=location;VehicleScreen(stock);status.Text="Build applied · replaced equipment stored at "+name;
                },true));
            }catch(Exception ex){previewPanel.AddView(Label(ex.Message,15,cyan));}
        }
        location=SelectLocation(body,id=>{location=id;Preview();});body.AddView(previewPanel);Preview();
        AddLocationForm(body,()=>ApplyBuildScreen(stock));
    }
}
