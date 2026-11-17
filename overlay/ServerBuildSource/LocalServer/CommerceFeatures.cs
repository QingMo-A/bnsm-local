using System.Security.Cryptography;
using System.Text;

internal sealed partial class ProtocolRouter
{
    private Reply? HandleCommerceWithReceipt(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,
        Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_BUY_CASH_PRODUCT_REQUEST" or "GS_CASH_PRODUCT_DELIVERY_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.CommerceReceipts.TryGetValue(key,out var receipt))
            return receipt.Fingerprint==fingerprint ? new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name)
                : fail("payment request sequence reused with different data",ServerErrorCode.ERROR_INVALID_BUY_PRODUCT_INFO);
        var result=HandleCommerce(name,request,p,ok,fail);
        if(result.Name.EndsWith("_OK")) {
            if(p.CommerceReceipts.Count>=128)p.CommerceReceipts.Remove(p.CommerceReceipts.Keys.First());
            p.CommerceReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        return result;
    }
    private LocalChargeOrder? BuildOrder(uint id)
    {
        var regular=catalog.BuyCashProducts.FirstOrDefault(x=>x.Id==id && x.BuyType==BscTable.BUY_CASH_DATA_TYPE.BUY_CASH && x.CNY>0 && x.Value>0);
        var order=new LocalChargeOrder {Token="LOCAL-"+Guid.NewGuid().ToString("N"),ProductId=id};
        if(regular!=null) {
            if(regular.RewardCash<0 || regular.RewardGold<0 || regular.CardNum<0 || regular.ItemNum<0)return null;
            order.Cash=checked((uint)(regular.Value+regular.RewardCash));order.Gold=(uint)regular.RewardGold;order.VipExperience=(uint)regular.Value;
            if(regular.ItemNum>0)order.Items[(uint)regular.RewardItemId]=regular.ItemNum;
            if(regular.CardNum>0)order.Cards.AddRange(Enumerable.Repeat((uint)regular.RewardCardId,regular.CardNum));
        } else {
            var package=catalog.CashProducts.FirstOrDefault(x=>x.Id==id && x.SubType==BscTable.CASH_PRODUCT_SUB_TYPE.CASH_PRODUCT_SUB_TYPE_DELIVERY && x.Cost>0 && x.Price>0);
            if(package==null || package.ItemId.Count==0 || package.ItemId.Count!=package.Quantity.Count || package.ItemId.Count!=package.ItemIdType.Count)return null;
            order.VipExperience=(uint)package.Price;
            for(int i=0;i<package.ItemId.Count;i++) {
                int amount=package.Quantity[i];if(amount<=0)return null;
                switch(package.ItemIdType[i]) {
                    case BscTable.REWARD_TYPE.REWARD_TYPE_ITEM:
                        uint item=(uint)package.ItemId[i];order.Items[item]=checked(order.Items.GetValueOrDefault(item)+amount);break;
                    case BscTable.REWARD_TYPE.REWARD_TYPE_CASH:order.Cash=checked(order.Cash+(uint)amount);break;
                    case BscTable.REWARD_TYPE.REWARD_TYPE_GOLD:order.Gold=checked(order.Gold+(uint)amount);break;
                    case BscTable.REWARD_TYPE.REWARD_TYPE_CARD:order.Cards.AddRange(Enumerable.Repeat((uint)package.ItemId[i],amount));break;
                    default:return null;
                }
            }
        }
        if(order.Items.Any(x=>x.Key==0 || x.Key==LocalStoryChapter.MaknaeFragmentId || (LocalCardRegistry.FindFragment((int)x.Key)!=null||LocalCardRegistry.IsEquipment((int)x.Key)) || !catalog.Items.Any(i=>i.Id==x.Key)) || order.Cards.Count>300 || order.Cards.Any(id=>LocalCardRegistry.FindExtensionForCard((int)id)!=null||!catalog.UsableHeroes.Any(c=>c.Id==id&&!LocalStoryChapter.IsMaknaeFamily(c.EvolveFirstAncestor))))return null;
        return order;
    }
    private Reply HandleCommerce(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_BUY_PRODUCT_INFO);
        switch(name) {
            case "GS_BUY_CASH_PRODUCT_REQUEST":
                uint id=((GsBuyCashProductRequest)request!).cashProductId;
                if(p.PendingCharge!=null && p.PendingCharge.ProductId!=id)return Invalid("deliver the pending local order first");
                if(p.PendingCharge==null) {
                    var order=BuildOrder(id);if(order==null)return Invalid("this original product is unsupported or has incomplete rewards");
                    if(p.CardIds.Count(x=>x!=0)+order.Cards.Count>300)return fail("card storage is full",ServerErrorCode.ERROR_INVALID_CARD);
                    p.PendingCharge=order;
                }
                return ok(new GsBuyCashProductOk {tokenId=p.PendingCharge.Token,urlParams="local-simulation"});
            case "GS_CASH_PRODUCT_DELIVERY_REQUEST":
                var delivery=new GsCashProductDeliveryOk();
                if(p.PendingCharge!=null) {
                    var order=p.PendingCharge;
                    ulong vip=order.VipExperience ?? order.Cash;
                    if((ulong)p.CashPoint+order.Cash>uint.MaxValue || (ulong)p.Gold+order.Gold>uint.MaxValue || (ulong)p.VipExp+vip>uint.MaxValue ||
                        order.Items.Any(x=>x.Value<=0 || (long)p.Inventory.GetValueOrDefault(x.Key)+x.Value>int.MaxValue) || p.CardIds.Count(x=>x!=0)+order.Cards.Count>300)
                        return Invalid("reward capacity reached; order remains pending");
                    p.Ranking.ChargedTotal+=vip;p.Ranking.ChargedToday+=vip;
                    p.CashPoint+=order.Cash;p.Gold+=order.Gold;p.VipExp+=(uint)vip;
                    foreach(var item in order.Items) {p.Inventory[item.Key]=p.Inventory.GetValueOrDefault(item.Key)+item.Value;delivery.itemList.Add(ItemInfo(item.Key,item.Value));}
                    p.CardIds.AddRange(order.Cards);p.CollectedCardIds.UnionWith(order.Cards);
                    p.PendingCharge=null;delivery.deliveryCount=1;
                }
                delivery.coinInfo=Coins(p);return ok(delivery);
            default:throw new InvalidOperationException(name);
        }
    }
}
