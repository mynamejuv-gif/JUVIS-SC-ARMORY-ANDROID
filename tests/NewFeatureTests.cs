using System.Text.Json;
using Juvis.Core;

static class NewFeatureTests
{
    static void Check(bool value){if(!value)throw new Exception("New-feature assertion failed");}
    static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidOperationException or InvalidDataException){return;}throw new Exception("Expected rejection");}
    public static void Run(Action<string,Action> test)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Fixtures");
        using var gd=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"loadout-gladius.json")));
        using var wd=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"performance-weapon.json")));
        var gladius=ApiParser.WikiVehicle(gd.RootElement.Get("data"));var weapon=ApiParser.WikiItem(wd.RootElement.Get("data"));
        test("Six loadout groups and quantum drive under Avionics",()=>{var g=LoadoutPresentation.Group(gladius.Ports,false);Check(g.Keys.SequenceEqual(LoadoutPresentation.Categories));Check(g["Avionics"].Any(p=>p.Types.Any(t=>t.Type=="QuantumDrive")));});
        test("Normal hides fixed/internal/unknown; Advanced retains every nested port",()=>{var normal=LoadoutPresentation.Group(gladius.Ports,false).Values.SelectMany(x=>x).ToList();Check(normal.Count<gladius.Ports.Count);Check(normal.All(p=>LoadoutPresentation.Visibility(p)==PortVisibility.Upgradeable));Check(LoadoutPresentation.Group(gladius.Ports,true).Values.Sum(x=>x.Count)==gladius.Ports.Count);Check(normal.Count(p=>p.Installed?.Type=="WeaponGun")==3);});
        test("Nested guns have distinct readable parent context",()=>Check(gladius.Ports.Where(p=>p.Installed?.Type=="WeaponGun").Select(LoadoutPresentation.ContextName).Distinct().Count()==3));
        test("Live weapon performance uses nested source values",()=>{var p=weapon.Performance;Check(p.BurstDps==545.6m&&p.SustainedDps==278.9m&&p.Alpha==43.7m&&p.MaximumRange==1924&&p.Velocity==1480&&p.Rpm==750);});
        test("No maximum range relabelled effective and no invented sustained DPS",()=>{Check(weapon.Performance.EffectiveRange==null);using var j=JsonDocument.Parse("""{"vehicle_weapon":{"rpm":500,"damage_per_shot":20,"range":2000}}""");var p=WeaponPerformance.Parse(j.RootElement);Check(p.BurstDps==null&&p.SustainedDps==null&&p.EffectiveRange==null&&p.MaximumRange==2000);});
        test("Energy capacitor capacity stays separate from ballistic rounds",()=>{var p=weapon.Performance;Check(p.Energy&&p.AmmoCapacity==null&&p.CapacitorShots==75&&p.CapacitorRegen==15&&p.CostPerShot==48.5m);});
        test("Numeric invalid values never become damage statistics",()=>{using var j=JsonDocument.Parse("""{"vehicle_weapon":{"damage":{"burst":false,"sustained_60s":-5},"rpm":"NaN"}}""");var p=WeaponPerformance.Parse(j.RootElement);Check(p.BurstDps==null&&p.SustainedDps==null&&p.Rpm==null);});
        var old=new Item{Id="old",Name="Old Shield",Type="Shield",Size=1,Version="patch",RestrictionsKnown=true};
        var next=old with{Id="new",Name="New Shield"};
        var port=new Port("shield","shield",true,1,1,[new("Shield",[])],[],[],old,"patch");
        var ship=new Vehicle("ship","Test ship","",false,"","patch",[port]);
        UserState State(){var s=new UserState();s.StorageLocations["A"]=new("A","Area18");s.StorageLocations["B"]=new("B","Orison");s.Builds[ship.Id]=[new(port.Id,next.Id,next.Name,next.Version)];return s;}
        test("Location add is case-insensitive and inventory quantities stay separate",()=>{var s=State();Check(LocalInventory.AddLocation(s," area18 ")=="A");LocalInventory.Adjust(s,"A",old,3);LocalInventory.Adjust(s,"B",old,1);Check(s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==3&&s.Inventory[LocalInventory.Key("B",old.Id)].Quantity==1);});
        test("Move conserves total quantity and rejects overdraft",()=>{var s=State();LocalInventory.Adjust(s,"A",old,3);LocalInventory.Move(s,"A","B",old,2);Check(s.Inventory.Values.Sum(x=>x.Quantity)==3);var before=Backup.Export(s);Reject(()=>LocalInventory.Move(s,"A","B",old,2));Check(s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1);});
        test("Proposing a build does not alter inventory or tracked equipment",()=>{var s=State();var p=LocalInventory.Preview(s,ship,[next],"A");Check(p.Changes.Count==1&&s.Inventory.Count==0&&s.TrackedLoadouts.Count==0);});
        test("Apply consumes selected location stock and stores removed equipment there",()=>{var s=State();LocalInventory.Adjust(s,"A",next,1);LocalInventory.Adjust(s,"B",next,4);LocalInventory.Apply(s,ship,[next],"A",false);Check(s.Inventory[LocalInventory.Key("A",next.Id)].Quantity==0&&s.Inventory[LocalInventory.Key("B",next.Id)].Quantity==4&&s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1);Check(LocalInventory.EffectiveVehicle(s,ship).Ports[0].Installed?.Id==next.Id&&s.Builds[ship.Id].Count==0);});
        test("Missing parts require explicit external-supply confirmation",()=>{var s=State();Reject(()=>LocalInventory.Apply(s,ship,[next],"A",false));Check(s.Inventory.Count==0&&s.TrackedLoadouts.Count==0);LocalInventory.Apply(s,ship,[next],"A",true);Check(s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1);});
        test("Reapplying cannot duplicate removed equipment",()=>{var s=State();LocalInventory.Apply(s,ship,[next],"A",true);Reject(()=>LocalInventory.Apply(s,ship,[next],"A",true));s.Builds[ship.Id]=[new(port.Id,next.Id,next.Name,next.Version)];Reject(()=>LocalInventory.Apply(s,ship,[next],"A",true));Check(s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1);});
        test("Second upgrade removes tracked current item, not original stock again",()=>{var s=State();LocalInventory.Apply(s,ship,[next],"A",true);var third=next with{Id="third",Name="Third Shield"};s.Builds[ship.Id]=[new(port.Id,third.Id,third.Name,third.Version)];LocalInventory.Apply(s,ship,[third],"B",true);Check(s.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1&&s.Inventory[LocalInventory.Key("B",next.Id)].Quantity==1);});
        test("Unknown compatibility and missing location cannot apply",()=>{var s=State();Reject(()=>LocalInventory.Apply(s,ship,[next with{Version="other"}],"A",true));Reject(()=>LocalInventory.Apply(s,ship,[next],"missing",true));Check(s.Inventory.Count==0);});
        test("Replacing a mount stores child equipment and invalidates old child slots",()=>{var s=State();var child=port with{Id="shield/gun",Installed=weapon};var vessel=ship with{Ports=[port,child]};LocalInventory.Apply(s,vessel,[next],"A",true);Check(s.Inventory[LocalInventory.Key("A",weapon.Id)].Quantity==1);var effective=LocalInventory.EffectiveVehicle(s,vessel);Check(effective.Ports[1].Installed==null&&effective.Ports[1].Editable==false);});
        test("Inventory and tracked loadout backup round trip",()=>{var s=State();LocalInventory.Apply(s,ship,[next],"A",true);var restored=Backup.Parse(Backup.Export(s));Check(restored.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1&&restored.TrackedLoadouts[ship.Id][port.Id].Item?.Id==next.Id&&restored.InventoryHistory.Count==1);});
        test("Repeated backup merge replaces quantities, never doubles them",()=>{var s=State();LocalInventory.Adjust(s,"A",old,2);var merged=Backup.Merge(s,s);merged=Backup.Merge(merged,s);Check(merged.Inventory[LocalInventory.Key("A",old.Id)].Quantity==2);LocalInventory.Adjust(s,"A",old,-2);Check(Backup.Merge(merged,s).Inventory[LocalInventory.Key("A",old.Id)].Quantity==0);});
        test("Older backups preserve new inventory on merge",()=>{var s=State();LocalInventory.Apply(s,ship,[next],"A",true);var oldBackup=Backup.Parse("""{"Format":"juvis-android","SchemaVersion":1,"State":{"Gear":{},"Blueprints":[],"Vehicles":[],"CraftPlan":{},"Builds":{}}}""");var merged=Backup.Merge(s,oldBackup);Check(merged.Inventory.Count==s.Inventory.Count&&merged.TrackedLoadouts.Count==1);});
        test("Malformed quantities and dangling locations are rejected in imports",()=>{var s=State();s.Inventory["bad"]=new("missing",old,-1);Reject(()=>Backup.Parse(Backup.Export(s)));});
        test("Armory import and relaunch persist inventory and tracking atomically",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"juvis-inventory-test-"+Guid.NewGuid());var s=State();LocalInventory.Apply(s,ship,[next],"A",true);
            using var http=new HttpClient();var app=new Armory(new LocalStore(dir),new ApiClient(http));app.Initialize(()=>Task.FromResult(new Catalog())).GetAwaiter().GetResult();app.Import(s).GetAwaiter().GetResult();
            var reopened=new Armory(new LocalStore(dir),new ApiClient(http));reopened.Initialize(()=>Task.FromResult(new Catalog())).GetAwaiter().GetResult();Check(reopened.State.Inventory[LocalInventory.Key("A",old.Id)].Quantity==1&&reopened.State.TrackedLoadouts.Count==1);
        });
        test("Patch validity separates LIVE, PTU and stale data",()=>{
            Check(ZeroHero.Validity("4.10.1-LIVE.12660092")==PatchValidity.Live);
            Check(ZeroHero.Validity("4.10.2 PTU") == PatchValidity.Ptu);
            Check(ZeroHero.Validity("4.10.0-LIVE") == PatchValidity.Outdated && ZeroHero.Validity("") == PatchValidity.Outdated);
        });
        test("Active craft and contract requirements become priority keep",()=>{
            var ingredient=new Ingredient("pressurized-ice-game-id","Pressurized Ice",1.5m,"SCU");
            var blueprint=new Blueprint("recipe","Test recipe","4.10.1 LIVE",10,true,[ingredient],[],"");
            var s=new UserState { CraftPlan=new(){{"recipe",2}}, ZeroHero=new(){ContractNeeds=new(){{"recycled-material-composite",3}}} };
            var plan=ZeroHero.ResourcePlan(new Catalog{Blueprints=[blueprint]},s);
            var ice=plan.Single(x=>x.Name=="Pressurized Ice");var rmc=plan.Single(x=>x.Name=="Recycled Material Composite");
            Check(ice.Needed==3&&ice.Disposition==ResourceDisposition.PriorityKeep&&rmc.Needed==3&&rmc.Disposition==ResourceDisposition.PriorityKeep);
        });
        test("New wipe run clears only Zero Hero progress",()=>{
            var s=State();s.Gear["tool"]=new(true);s.Blueprints.Add("recipe");s.Vehicles.Add(ship.Id);
            s.ZeroHero.CompletedSteps.Add("stabilize");s.ZeroHero.ResourceInventory["ice"]=4;s.ZeroHero.ContractNeeds["ice"]=2;s.ZeroHero.RebuildQueue.Add(ship.Id);
            var run=s.ZeroHero.RunNumber;ZeroHero.NewRun(s,new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero));
            Check(s.ZeroHero.RunNumber==run+1&&s.ZeroHero.CompletedSteps.Count==0&&s.ZeroHero.ResourceInventory.Count==0&&s.ZeroHero.ContractNeeds.Count==0&&s.ZeroHero.RebuildQueue.Count==0);
            Check(s.Gear["tool"].Owned&&s.Blueprints.Contains("recipe")&&s.Vehicles.Contains(ship.Id)&&s.Builds[ship.Id].Count==1&&s.StorageLocations.Count==2);
        });
        test("Zero Hero state survives backup and merge",()=>{
            var s=State();s.ZeroHero.ResourceInventory["pressurized-ice"]=2.5m;s.ZeroHero.ResourceChoices["pressurized-ice"]=ResourceDisposition.PriorityKeep;s.ZeroHero.CompletedSteps.Add("stabilize");
            var restored=Backup.Parse(Backup.Export(s));Check(restored.ZeroHero.ResourceInventory["pressurized-ice"]==2.5m&&restored.ZeroHero.CompletedSteps.Contains("stabilize"));
            var merged=Backup.Merge(new UserState(),restored);Check(merged.ZeroHero.ResourceChoices["pressurized-ice"]==ResourceDisposition.PriorityKeep);
        });
        test("Older backup merge preserves current Zero Hero run",()=>{
            var current=new UserState();current.ZeroHero.RunNumber=4;current.ZeroHero.ResourceInventory["pressurized-ice"]=7;
            var old=Backup.Parse("""{"Format":"juvis-android","SchemaVersion":1,"State":{"Gear":{},"Blueprints":[],"Vehicles":[],"CraftPlan":{},"Builds":{}}}""");
            var merged=Backup.Merge(current,old);Check(merged.ZeroHero.RunNumber==4&&merged.ZeroHero.ResourceInventory["pressurized-ice"]==7);
        });
    }
}
