from pathlib import Path
r=Path('I:/BnsmModernWork');p=r/'ServerBuildSource/LocalServer/CompetitiveCastleFeatures.cs';s=p.read_text(encoding='utf-8-sig').replace('storeGoodsId=(uint)row.GoodsId[i]','storeGoodsId=(uint)(CastleOffer(p,row,i)?.GoodsId??0)');p.write_text(s,encoding='utf-8-sig')
p=r/'ServerBuildSource/LocalServer/CompetitiveCastleSupport.cs';s=p.read_text(encoding='utf-8-sig');i=s.index('    private void CompetitiveProgress');s=s[:i]+'''    private BscTable.GoodsData? CastleOffer(LocalPlayer p,BscTable.CastleSpotInfo spot,int slot) {
        var original=catalog.Goods.FirstOrDefault(x=>x.GoodsId==spot.GoodsId[slot]);if(original!=null)return original;
        // The shipped castle map references retired goods groups absent from CDT.
        // Use a deterministic eligible original offer with its original price and
        // quantity; publish the concrete ID to the client, never the missing group.
        var money=spot.MoneyType[slot];var pool=catalog.Goods.Where(x=>x.ProductIdType==BscTable.GOODS_TYPE.GOODS_TYPE_ITEM&&x.Quantity>0&&catalog.ItemsById.ContainsKey(x.ProductId)&&x.PcLevelMin<=p.Level&&x.PcLevelMax>=p.Level&&(money==BscTable.MONEY_TYPE.MONEY_TYPE_GOLD?x.Gold>0:money==BscTable.MONEY_TYPE.MONEY_TYPE_CASH?x.Cash>0:x.ArenaP>0)).OrderBy(x=>x.GoodsId).ToArray();
        return pool.Length==0?null:pool[(spot.SpotId*31+spot.GoodsId[slot])%pool.Length];
    }
'''+s[i:];s=s.replace('row.GoodsId[index]!=buy.goodsId','CastleOffer(p,row,index)?.GoodsId!=buy.goodsId');p.write_text(s,encoding='utf-8-sig')
p=r/'Verification/BotWorldTests/CompetitiveRegression.cs';s=p.read_text(encoding='utf-8-sig').replace('uint id=(uint)tile.GoodsId[slot],beforeGold=p.Gold;','uint id=conquered.castleMapSpotInfo.storeInfoList[slot].storeGoodsId,beforeGold=p.Gold;');p.write_text(s,encoding='utf-8-sig')
