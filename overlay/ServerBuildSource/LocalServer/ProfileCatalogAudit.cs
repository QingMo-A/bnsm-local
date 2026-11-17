extern alias game;
using System.Text.Json;
using ProtoBuf;
internal static class ProfileCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string originalCdt=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets/CDT.bytes");
        byte[] packed=File.ReadAllBytes(originalCdt),cdt=packed[0]==0?packed[1..]:game::Crypto.GetDecrypt(packed[1..]);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"TitleDataTable","ExpShopSetDataTable","GoodsDataTable","MoneyTypeResource"}) {
            var row=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            byte[] data=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool matches=data.AsSpan().SequenceEqual(cdt.AsSpan(row.GetProperty("offset").GetInt32(),row.GetProperty("length").GetInt32()));
            if(!matches)throw new InvalidDataException("Profile source mismatch: "+name);
            sources.Add(new{name,matchesOriginalCdt=matches,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))});
        }
        var entry=manifest.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        using var textStream=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));
        var texts=Serializer.Deserialize<BscTable.LocalTextDataTable>(textStream);texts.MakeMap();
        File.WriteAllText(Path.Combine(root,"evidence/profile-original-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",openLevel=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_EXP),sources,
            text=texts.List.Where(x=>x.Text!=null&&(x.Text.Contains("边框")||x.Text.Contains("经验商店")||x.Text.Contains("更改名称"))).Select(x=>new{x.Id,x.Text}),
            titles=c.Titles.Select(t=>new{t.Id,t.TitleName,t.TitleLevel,t.UniqueType,t.Period,t.TitleIcon,t.ItemId,text=texts.GetItem(t.TitleName)?.Text}), expShop=c.ExpShop, goods=c.Goods.Where(g=>c.ExpShop.Any(s=>s.GoodsId==g.GoodsId)), rename=c.MoneyResources.Where(x=>x.MoneyEventType==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_CHANGE_NICKNAME_COST), titleItems=c.Items.Where(i=>c.Titles.Any(t=>t.ItemId==i.Id)||i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_TITLE), vipTitles=c.Vip.Select(v=>new{v.VipLv,v.VipTitleId,v.VipTitleIdType}), playerCap=ProgressionRules.PlayerMax(c), playerRows=c.PlayerStates.Where(x=>x.Level>=98&&x.Level<=100), minName=game::BSC_DEFINE.NICKNAME_MIN_LENGTH,maxName=game::BSC_DEFINE.NICKNAME_MAX_LENGTH
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
