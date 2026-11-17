using ProtoBuf;
using System.Text.Json;

/// <summary>Explicit local pricing for every original item; prices are exported to the player.</summary>
internal sealed class FullShopCatalog
{
    public const uint GoodsBase = 1_000_000_000;
    public const uint QueryMarker = 0xF17EC001;
    public const int PageSize = 48;
    public readonly Dictionary<uint,BscTable.GoodsData> Goods;
    public readonly Dictionary<uint,string> PriceSources = [];
    public FullShopCatalog(GameCatalog catalog)
    {
        Goods = [];
        var originalByItem = catalog.Goods.Where(x=>x.ProductIdType==BscTable.GOODS_TYPE.GOODS_TYPE_ITEM && x.Quantity>0).GroupBy(x=>x.ProductId).ToDictionary(x=>x.Key,x=>x.OrderBy(g=>g.GoodsId).ToArray());
        var originalIds=catalog.Goods.Select(x=>x.GoodsId).ToHashSet();
        foreach(var item in catalog.Items.Where(x=>x.Id>0&&x.Id!=LocalStoryChapter.MaknaeFragmentId&&LocalCardRegistry.FindFragment(x.Id)==null&&!LocalCardRegistry.IsEquipment(x.Id)&&x.Id!=LocalStoryChapter.WeaponBlankId&&!LocalHelperCostumeDefinition.IsItem(x.Id)).OrderBy(x=>x.ItemType).ThenBy(x=>x.Id)) {
            var original = originalByItem.GetValueOrDefault(item.Id, []);
            long sale=item.Sell>0 && item.SellMoneyType==BscTable.ITEM_SELL_MONEY_TYPE.SELL_MONEY_TYPE_GOLD ? Math.Max(0,item.Value) : 0;
            long local=Math.Max(100,1000+750L*Math.Max(0,(int)item.Grade)+500L*Math.Max(item.Rank,item.Step)+5L*Math.Max(0,item.GetExp));
            local=Math.Max(local,sale*2+1);
            int gold=(int)Math.Min(int.MaxValue,original.Where(x=>x.Gold>0).Select(x=>(long)Math.Ceiling((double)x.Gold/x.Quantity)).DefaultIfEmpty(local).First());
            int cash=original.Where(x=>x.Cash>0).Select(x=>(int)Math.Ceiling((double)x.Cash/x.Quantity)).DefaultIfEmpty(Math.Max(1,(int)Math.Ceiling(gold/3000d))).First();
            uint id=checked(GoodsBase+(uint)item.Id);
            if(id>int.MaxValue || originalIds.Contains((int)id))throw new InvalidDataException("Local goods ID collision");
            Goods[id]=new BscTable.GoodsData { GoodsId=(int)id,ProductIdType=BscTable.GOODS_TYPE.GOODS_TYPE_ITEM,ProductId=item.Id,
                ProductIdAlias="LocalItem_"+item.Id,Quantity=1,Gold=gold,Cash=cash,NameAlias=item.NameAlias??"",Image=item.Icon??"",PcLevelMin=1,PcLevelMax=999 };
            PriceSources[id]=original.Any(x=>x.Gold>0||x.Cash>0)?"original unit price where available; other currency uses local fallback":"local fallback";
        }
        foreach (var preview in LocalCardRegistry.MarketPreviews().Concat(LocalStoryChapter.MaknaeMarketPreviews()))
        {
            uint id = (uint)preview.GoodsId;
            if (originalIds.Contains(preview.GoodsId) || !Goods.TryAdd(id, preview))
                throw new InvalidDataException($"Registered shop preview ID collision: {preview.NameAlias}/{id}");
            PriceSources[id] = "story acquisition";
        }
    }
    public void Export(string root)
    {
        var table=new BscTable.GoodsDataTable();table.List.AddRange(Goods.Values);
        string file=Path.Combine(root,"UnityRebuilt/ExportedProject/Assets/StreamingAssets/LocalShopGoods.bytes");
        using(var stream=File.Create(file))Serializer.Serialize(stream,table);
        File.WriteAllText(Path.Combine(root,"evidence/full-shop-catalog.json"),JsonSerializer.Serialize(new {
            itemCount=Goods.Count,pageSize=PageSize,goodsBase=GoodsBase,queryMarker=QueryMarker,
            prices="Use positive original goods prices per unit, otherwise gold=max(100,1000+750*grade+500*max(rank,step)+5*exp,2*goldSaleValue+1); cash=ceil(gold/3000). No real payments.",
            sourceSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
            allItemIds=Goods.Values.Select(x=>x.ProductId),sample=Goods.Take(12).Select(x=>new{x.Key,x.Value.ProductId,x.Value.Gold,x.Value.Cash,source=PriceSources[x.Key]})},new JsonSerializerOptions {WriteIndented=true}));
    }
}
