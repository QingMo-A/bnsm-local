extern alias game;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;

internal static class CraftingCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string dynamic=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets");
        byte[] Decode(string file) {var b=File.ReadAllBytes(Path.Combine(dynamic,file));return b[0]==0?b[1..]:game::Crypto.GetDecrypt(b[1..]);}
        byte[] blob=Decode("CDT.bytes");
        using var offsetStream=new MemoryStream(Decode("CDT_off.bytes"));
        var offsets=Serializer.Deserialize<BscTable.DataOffsetTable>(offsetStream).List;
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"EquipmentDataTable","GemDataTable","RecipeDataTable","ItemDataTable","ItemCardProcessData","ContentDataTable"}) {
            var m=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            int offset=m.GetProperty("offset").GetInt32(),length=m.GetProperty("length").GetInt32(),id=m.GetProperty("id").GetInt32();
            var bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool equal=Enumerable.Range(0,offsets.Count/4).Any(i=>offsets[i*4]==id&&offsets[i*4+1]==offset&&offsets[i*4+2]==length)&&bytes.AsSpan().SequenceEqual(blob.AsSpan(offset,length));
            if(!equal)throw new InvalidDataException("Original crafting table mismatch: "+name);
            sources.Add(new {name,offset,length,sha256=Convert.ToHexString(SHA256.HashData(bytes)),matchesOriginalCdt=equal});
        }
        var report=new {version="1.97.0.0",sources,
            contents=c.Contents.Where(x=>x.Alias.ToString().Contains("CRAFT")||x.Alias.ToString().Contains("AWAKE")).Select(x=>new {type=x.Alias.ToString(),value=c.ContentValue(x.Alias)}),
            processes=c.CardProcesses.List.Where(x=>x.ProcessType.ToString().Contains("CRAFT")||x.ProcessType.ToString().Contains("AWAK")),
            pathCounts=new {refinement=c.Items.Count(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Next1Id!=0),awakening=c.Items.Count(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.AwakningNextId!=0),gem=c.Items.Count(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Next1Id!=0),ingredientRecipes=c.Recipes.Count(x=>c.Items.Any(i=>i.Id==x.TargetItemId&&i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT))},
            equipmentExamples=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.AwakningNextId!=0).GroupBy(x=>x.EquipmentType).SelectMany(g=>g.Take(2)).Select(x=>new {x.Id,x.NameAlias,x.Level,x.Step,x.Rank,type=x.EquipmentType.ToString(),x.Next1Id,x.AwakningNextId,x.Material1Id,x.Material1Value,x.AwakningMaterialId,x.AwakningMaterialValue}),
            gemExamples=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Next1Id!=0).GroupBy(x=>x.Step).Select(g=>g.First()).Select(x=>new {x.Id,x.NameAlias,x.Step,x.Next1Id,x.Material1Id,x.Material1Value}),
            recipeExamples=c.Recipes.Where(x=>c.Items.Any(i=>i.Id==x.TargetItemId&&i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT)).Take(8).Select(x=>new {x.TargetItemId,x.TargetItemQuantity,x.CostType,x.Cost,x.MeterialItemId,x.MeterialItemQuantity})};
        File.WriteAllText(Path.Combine(root,"evidence/crafting-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Crafting tables verified against original CDT: "+sources.Count);
    }
}
