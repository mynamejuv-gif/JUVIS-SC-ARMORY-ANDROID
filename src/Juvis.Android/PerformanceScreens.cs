using Android.Graphics;
using Android.Views;
using Android.Widget;
using Juvis.Core;
namespace Juvis.AndroidApp;

public partial class MainActivity
{
    void PerformanceCard(Item item)
    {
        if(!item.Type.Equals("WeaponGun",StringComparison.OrdinalIgnoreCase))return;
        var p=item.Performance;var card=Card();card.AddView(Label("WEAPON PERFORMANCE",14,cyan));
        card.AddView(Label($"{ApiParser.First(p.DamageType,"Damage type unavailable")} · Size {item.Size?.ToString()??"?"}",15));
        foreach(var (label,value,unit) in PerformanceFields(p))card.AddView(Label($"{label}: {WeaponPerformance.Value(value,unit)}",14));
        card.AddView(Label("Ammo / energy: "+p.AmmoEnergy,14));
        card.AddView(Label("Power demand: "+WeaponPerformance.Value(p.PowerDemand," source units"),14));
        card.AddView(Label($"Source: {ApiParser.First(item.Source,"Unavailable")}\nData version: {ApiParser.First(item.Version,"Unknown")}",12,cyan));
        card.AddView(Label("Sustained DPS uses the source's 60-second reference model. Ship power, heat, firing mode and capacitor allocation affect actual results. Maximum projectile range is not a verified effective combat range.",12,muted));
        if(!p.HasData)card.AddView(Label("Refresh Wiki details to load performance. Missing values are not estimated.",13,muted));
        body.AddView(card);
    }
    static (string Name,decimal? Value,string Unit)[] PerformanceFields(WeaponPerformance p) => [
        ("Burst DPS",p.BurstDps,""),("Sustained DPS (60 s)",p.SustainedDps,""),("Alpha / shot",p.Alpha,""),
        ("Effective range",p.EffectiveRange," m"),("Maximum range",p.MaximumRange," m"),("Projectile velocity",p.Velocity," m/s"),("Fire rate",p.Rpm," rpm")];
    void ComparisonTable(Vehicle vehicle,Port port,List<(Item Item,FitResult Result)> candidates)
    {
        var current=LoadoutPresentation.InstalledForComparison(port,armory.Catalog.Items);
        var weapon=port.Types.Any(t=>t.Type.Equals("WeaponGun",StringComparison.OrdinalIgnoreCase));
        var selected=candidates.Where(c=>c.Result.Fit==Fit.Direct&&c.Item.Type=="WeaponGun").Take(3).Select(c=>c.Item.Id).ToHashSet();
        var chartPanel=new LinearLayout(this){Orientation=Orientation.Vertical};
        var tablePanel=new LinearLayout(this){Orientation=Orientation.Vertical};int tablePage=0;
        body.AddView(Label("Swipe horizontally for all columns. — means data unavailable. Only confirmed matches can be added to a build.",13,muted));
        body.AddView(tablePanel);
        if(weapon){body.AddView(Label("Select up to five confirmed alternatives for the charts. The fitted weapon stays included.",13,muted));body.AddView(chartPanel);}
        void Charts()
        {
            chartPanel.RemoveAllViews();if(!weapon)return;
            var rows=candidates.Where(c=>selected.Contains(c.Item.Id)&&c.Item.Type=="WeaponGun"&&c.Result.Fit==Fit.Direct).Select(c=>(c.Item,Name:CatalogPresentation.ItemName(c.Item)!)).ToList();
            if(current!=null&&current.Version==port.Version)rows.Insert(0,(current,(CatalogPresentation.ItemName(current)??"Fitted weapon")+" (current)"));
            foreach(var range in new[]{false,true})
            {
                var card=Card();card.AddView(Label(range?"RANGE COMPARISON":"DPS COMPARISON",16,cyan));
                var max=rows.SelectMany(x=>range?new[]{x.Item.Performance.EffectiveRange,x.Item.Performance.MaximumRange}:new[]{x.Item.Performance.BurstDps,x.Item.Performance.SustainedDps}).Max()??1;
                max=Math.Max(1,max);
                foreach(var (item,name) in rows)
                {
                    card.AddView(Label(name,14));var p=item.Performance;
                    var metrics=range?new[]{("Effective",p.EffectiveRange),("Maximum",p.MaximumRange)}:new[]{("Burst",p.BurstDps),("Sustained (60 s)",p.SustainedDps)};
                    for(int n=0;n<metrics.Length;n++)
                    {
                        var (label,value)=metrics[n];card.AddView(Label(label+": "+WeaponPerformance.Value(value,range?" m":" DPS"),12,muted));
                        card.AddView(new PerformanceBar(this,value,max,n==0?cyan:Color.ParseColor("#8293FF")),new LinearLayout.LayoutParams(-1,Dp(12)));
                    }
                }
                card.AddView(Label($"Scale 0–{max:0.##} {(range?"m":"DPS")}. Missing values have no bar.",12,muted));chartPanel.AddView(card);
            }
        }
        void Table()
        {
            tablePanel.RemoveAllViews();
            var pages=Math.Max(1,(candidates.Count+19)/20);tablePage=Math.Clamp(tablePage,0,pages-1);
            tablePanel.AddView(Label($"{candidates.Count} alternatives · page {tablePage+1} / {pages}",12,muted));
            var horizontal=new HorizontalScrollView(this);var grid=new LinearLayout(this){Orientation=Orientation.Vertical};horizontal.AddView(grid);tablePanel.AddView(horizontal);
            var headings=new List<(string,int)>{("CURRENT / CANDIDATE",220),("SIZE",65),("TYPE / FIT",155)};
            if(weapon)headings.AddRange(new[]{("BURST DPS",105),("SUSTAINED DPS\n60 s model",125),("ALPHA / SHOT",105),("EFFECTIVE RANGE",125),("MAXIMUM RANGE",125),("VELOCITY",115),("FIRE RATE",105),("AMMO / ENERGY",200)});
            headings.Add(("SOURCE / VERSION",190));headings.Add(("ACTIONS",180));
            var header=Row();header.SetBackgroundColor(Color.ParseColor("#164050"));
            foreach(var (text,width) in headings)Cell(header,Label(text,12,cyan),width);grid.AddView(header);
            void AddRow(Item item,FitResult fit,bool fitted)
            {
                var row=Row();row.SetBackgroundColor(fitted?Color.ParseColor("#12323F"):panel);
                row.LayoutParameters=new LinearLayout.LayoutParams(-2,-2){BottomMargin=Dp(2)};
                var name=new LinearLayout(this){Orientation=Orientation.Vertical};name.AddView(Label(fitted?"CURRENT":"CANDIDATE",10,cyan));name.AddView(Label(CatalogPresentation.ItemName(item)??"Name unavailable",15));
                if(!fitted&&weapon&&item.Type=="WeaponGun"&&fit.Fit==Fit.Direct)
                {
                    var check=new CheckBox(this){Text="Chart",Checked=selected.Contains(item.Id)};check.SetTextColor(cyan);check.SetMinHeight(Dp(48));
                    check.CheckedChange+=(_,e)=>{if(e.IsChecked&&selected.Count>=5&&!selected.Contains(item.Id)){check.Checked=false;status.Text="Choose up to five alternatives for charts.";return;}if(e.IsChecked)selected.Add(item.Id);else selected.Remove(item.Id);Charts();};name.AddView(check);
                }
                Cell(row,name,220);Cell(row,Label(item.Size?.ToString()??"—"),65);
                var p=item.Performance;Cell(row,Label(ApiParser.First(p.DamageType,item.Type)+"\n"+(fitted?"Fitted":fit.Fit==Fit.Direct?"✓ Published mount fit":"△ Check required")+(!fitted&&fit.Fit!=Fit.Direct?"\n"+fit.Reason:""),12),155);
                if(weapon)
                {
                    var widths=new[]{105,125,105,125,125,115,105};int n=0;
                    foreach(var (_,value,unit) in PerformanceFields(p))Cell(row,Label(value==null?"—":WeaponPerformance.Value(value,unit),13),widths[n++]);
                    Cell(row,Label(item.Type=="WeaponGun"?p.AmmoEnergy:"—",12),200);
                }
                Cell(row,Label(ApiParser.First(item.Source,"Unknown")+"\n"+ApiParser.First(item.Version,"Patch unknown"),12,muted),190);
                var actions=new LinearLayout(this){Orientation=Orientation.Vertical};
                actions.AddView(Button("Details",()=>{ItemScreen(item,()=>CompatibleScreen(vehicle,port));return Task.CompletedTask;}));
                if(!fitted&&fit.Fit==Fit.Direct&&!LoadoutPresentation.ParentChanged(port,armory.State.Builds.GetValueOrDefault(vehicle.Id)??[]))
                    actions.AddView(Button("+ Proposed build",()=>SaveCandidate(vehicle,port,item),true));
                actions.AddView(Button("✦ Gemini",()=>AskGemini(GeminiPrompt.Item(item))));Cell(row,actions,180);grid.AddView(row);
            }
            if(current!=null)AddRow(current.Version==port.Version?current:current with {Performance=new()},new(Fit.Direct,"Current equipment"),true);
            foreach(var (item,result) in candidates.Skip(tablePage*20).Take(20))AddRow(item,result,false);
            if(tablePage>0)tablePanel.AddView(Button("Previous candidates",()=>{tablePage--;Table();return Task.CompletedTask;}));
            if(tablePage+1<pages)tablePanel.AddView(Button("Next candidates",()=>{tablePage++;Table();return Task.CompletedTask;}));
        }
        Table();Charts();
    }
    void Cell(LinearLayout row,View view,int width)
    {
        view.SetPadding(Dp(8),Dp(8),Dp(8),Dp(8));row.AddView(view,new LinearLayout.LayoutParams(Dp(width),-2));
    }
    async Task SaveCandidate(Vehicle vehicle,Port port,Item item)
    {
        if(LoadoutPresentation.Visibility(port)!=PortVisibility.Upgradeable||Compatibility.Check(port,item).Fit!=Fit.Direct)throw new InvalidOperationException("Compatibility needs rechecking.");
        await armory.Change(s=>{
            if(LoadoutPresentation.ParentChanged(port,s.Builds.GetValueOrDefault(vehicle.Id)??[]))throw new InvalidOperationException("Remove the proposed parent mount change first.");
            if(!s.Builds.TryGetValue(vehicle.Id,out var build))s.Builds[vehicle.Id]=build=[];
            build.RemoveAll(e=>e.PortId==port.Id||e.PortId.StartsWith(port.Id+"/",StringComparison.Ordinal));
            build.Add(new(port.Id,item.Id,CatalogPresentation.ItemName(item)!,item.Version));
        });VehicleScreen(vehicle,true);
    }
}
sealed class PerformanceBar(global::Android.Content.Context context,decimal? value,decimal max,Color color):View(context)
{
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);using var paint=new Paint();paint.Color=Color.ParseColor("#06131B");canvas.DrawRect(0,0,Width,Height,paint);
        if(value is >=0){paint.Color=color;canvas.DrawRect(0,0,(float)Math.Clamp(value.Value/Math.Max(1,max),0,1)*Width,Height,paint);}
    }
}
