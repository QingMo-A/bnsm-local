using System.Globalization;
using System.Security.Cryptography;
using System.Text;

internal sealed class LocalTitle
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public ulong RegisteredAt {get;set;}
    public ulong ExpiresAt {get;set;}
    public bool IsNew {get;set;}=true;
}

internal sealed partial class ProtocolRouter
{
    private static bool TitleValid(LocalTitle t)=>t.ExpiresAt==0||t.ExpiresAt>Now;
    private void GrantTitle(LocalPlayer p,uint id)
    {
        if(!p.Titles.TryGetValue(id,out var t)||!TitleValid(t))p.Titles[id]=new(){RegisteredAt=Now};
    }
    private void RefreshTitles(LocalPlayer p)
    {
        GrantTitle(p,0);
        int vip=catalog.VipFor(p.VipExp).VipLv;
        foreach(var milestone in catalog.Vip.Where(x=>x.VipLv<=vip&&x.VipTitleId>0&&x.VipTitleIdType==BscTable.REWARD_TYPE.REWARD_TYPE_TITLE))
            if(catalog.Titles.Any(t=>t.Id==milestone.VipTitleId))GrantTitle(p,(uint)milestone.VipTitleId);
    }
    private uint SelectedTitle(LocalPlayer p)=>catalog.Titles.Any(x=>x.Id==p.SelectedTitle)&&p.Titles.TryGetValue(p.SelectedTitle,out var t)&&TitleValid(t)?p.SelectedTitle:0;
    internal static bool ValidNickname(string value)=>value.Length is >=2 and <=8&&value.All(c=>
        char.GetUnicodeCategory(c) is UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber||c is '·' or 'ᐧ' or '・' or '･');
    private Reply? HandleProfile(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_GET_TITLE_LIST_REQUEST" or "GS_SELECT_TITLE_REQUEST" or "GS_CHANGE_NICKNAME_REQUEST" or "GS_GET_RANDOM_NICKNAME_REQUEST" or "GS_GET_EXP_SHOP_BUY_LIST_REQUEST" or "GS_BUY_EXP_SHOP_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}"))),fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.ProfileReceipts.TryGetValue(key,out var receipt))return receipt.Fingerprint==fingerprint?new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name):fail("profile sequence reused with different data",ServerErrorCode.ERROR_INVALID_ID);
        Reply reply;
        switch(name) {
        case "GS_GET_TITLE_LIST_REQUEST": {
            RefreshTitles(p);var result=new GsGetTitleListOk();
            foreach(var title in catalog.Titles) {
                uint id=(uint)title.Id;
                if(p.Titles.TryGetValue(id,out var owned)&&TitleValid(owned)) {
                    result.titleList.Add(new(){titleId=id,isNewTitle=owned.IsNew,registTime=owned.RegisteredAt});owned.IsNew=false;
                } else if(title.ItemId>0&&p.Inventory.GetValueOrDefault((uint)title.ItemId)>0)
                    result.titleList.Add(new(){titleId=id,isNewTitle=true,registTime=Now});
            }
            reply=ok(result);break;
        }
        case "GS_SELECT_TITLE_REQUEST": {
            uint id=((GsSelectTitleRequest)request!).titleId;var row=catalog.Titles.FirstOrDefault(t=>t.Id==id);
            if(row==null)return fail("invalid original frame",ServerErrorCode.ERROR_INVALID_TITLE_ID);
            bool owned=p.Titles.TryGetValue(id,out var current)&&TitleValid(current);
            bool vip=id==0||catalog.Vip.Any(v=>v.VipLv<=catalog.VipFor(p.VipExp).VipLv&&v.VipTitleId==id&&v.VipTitleIdType==BscTable.REWARD_TYPE.REWARD_TYPE_TITLE);
            bool voucher=row.ItemId>0&&p.Inventory.GetValueOrDefault((uint)row.ItemId)>0;
            if(!owned&&!vip&&!voucher)return fail("frame is not unlocked",ServerErrorCode.ERROR_INVALID_TITLE_ID);
            if(!owned&&!vip) {uint item=(uint)row.ItemId;p.Inventory[item]--;if(p.Inventory[item]==0)p.Inventory.Remove(item);}
            RefreshTitles(p);GrantTitle(p,id);p.Titles[id].IsNew=false;p.SelectedTitle=id;
            reply=ok(new GsSelectTitleOk{nameCardInfo=NameCard(p)});break;
        }
        case "GS_CHANGE_NICKNAME_REQUEST": {
            string nick=((GsChangeNickNameRequest)request!).nickName??"";
            if(!ValidNickname(nick))return fail("nickname must match original 2..8-character rules",ServerErrorCode.ERROR_INVALID_NICKNAME);
            if(store.All().Any(x=>string.Equals(x.Nickname,nick,StringComparison.OrdinalIgnoreCase)))return fail("nickname already used by a local profile",ServerErrorCode.ERROR_EXIST_NICKNAME);
            var fee=catalog.MoneyResources.Single(x=>x.MoneyEventType==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_CHANGE_NICKNAME_COST);
            if(fee.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_CASH||fee.Value<0)return fail("invalid original nickname fee",ServerErrorCode.ERROR_INVALID_ID);
            if(p.CashPoint<fee.Value)return fail("not enough diamonds to rename",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)fee.Value;p.Nickname=nick;reply=ok(new GsChangeNickNameOk{newNickName=p.Nickname,coinInfo=Coins(p)});break;
        }
        case "GS_GET_RANDOM_NICKNAME_REQUEST": {
            string[] starts=["青竹","云间","听雨","剑客","墨羽","清风","星河","飞雪"];string nick;
            do{nick=starts[RandomNumberGenerator.GetInt32(starts.Length)]+RandomNumberGenerator.GetInt32(1000000).ToString("D6");}while(store.All().Any(x=>string.Equals(x.Nickname,nick,StringComparison.OrdinalIgnoreCase)));
            return ok(new GsGetRandomNickNameOk{randomNickname=nick});
        }
        case "GS_GET_EXP_SHOP_BUY_LIST_REQUEST":
        case "GS_BUY_EXP_SHOP_REQUEST": {
            if(p.Level<catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_EXP))return fail("experience shop opens at original level 99",ServerErrorCode.ERROR_INVALID_ID);
            ulong day=(Now+8*3600)/86400;bool fresh=p.ExpShopDay!=day;
            bool Visible(BscTable.ExpShopSetData s)=>s.PcLvMin<=p.Level&&(s.PcLvMax==0||p.Level<=s.PcLvMax);
            if(name=="GS_GET_EXP_SHOP_BUY_LIST_REQUEST") {
                if(fresh){p.ExpShopDay=day;p.ExpShopPurchases.Clear();}
                var result=new GsGetExpShopBuyListOk();result.buyDataList.AddRange(catalog.ExpShop.Where(Visible).Select(s=>new PKExpShopBuyData{slotId=(uint)s.Slot,buyCount=p.ExpShopPurchases.GetValueOrDefault((uint)s.Slot)}));reply=ok(result);break;
            }
            var r=(GsBuyExpShopRequest)request!;var slot=catalog.ExpShop.SingleOrDefault(s=>s.Slot==r.SlotId);var goods=slot==null?null:catalog.Goods.SingleOrDefault(g=>g.GoodsId==slot.GoodsId);
            uint used=fresh?0:p.ExpShopPurchases.GetValueOrDefault(r.SlotId);
            if(slot==null||!Visible(slot)||goods==null||slot.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_EXP||goods.Exp<=0||r.buyTryCount is 0 or >100||used+(ulong)r.buyTryCount>(uint)Math.Max(0,slot.BuyCount))return fail("invalid original experience-shop purchase or daily stock",ServerErrorCode.ERROR_INVALID_ID);
            if(p.ActiveStage!=null)return fail("finish the current battle before spending player EXP",ServerErrorCode.ERROR_INVALID_ID);
            ulong cost=(ulong)goods.Exp*r.buyTryCount;uint floor=ProgressionRules.PlayerFloor(catalog,p.Level);
            if(p.Exp<floor||cost>p.Exp-floor)return fail("only experience above the current level floor can be spent",ServerErrorCode.ERROR_INVALID_ID);
            var grant=new ShopGrant();BscTable.REWARD_TYPE type=goods.ProductIdType switch {BscTable.GOODS_TYPE.GOODS_TYPE_ITEM=>BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,BscTable.GOODS_TYPE.GOODS_TYPE_CARD=>BscTable.REWARD_TYPE.REWARD_TYPE_CARD,_=>BscTable.REWARD_TYPE.REWARD_NONE};
            long amount=(long)goods.Quantity*r.buyTryCount;
            if(amount<=0||amount>int.MaxValue||!AddShopReward(p,grant,type,goods.ProductId,(int)amount)||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("experience shop reward cannot fit inventory",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var bought=new GsBuyExpShopOk();ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,bought.deltaItemList,bought.deltaCardList);p.Exp-=(uint)cost;
            if(fresh){p.ExpShopDay=day;p.ExpShopPurchases.Clear();}p.ExpShopPurchases[r.SlotId]=used+r.buyTryCount;bought.nowCoinInfo=Coins(p);bought.pcExp=p.Exp;reply=ok(bought);break;
        }
        default:return null;
        }
        if(p.ProfileReceipts.Count>=256)p.ProfileReceipts.Remove(p.ProfileReceipts.Keys.First());
        p.ProfileReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};store.Save();return reply;
    }
}
