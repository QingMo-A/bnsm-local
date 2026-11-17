using System.Security.Cryptography;
using System.Text.Json;

internal static class HelperCostumeRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","helper-costume-protocol-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool pass,string message){if(!pass)throw new InvalidDataException(message);checks.Add(message);Console.WriteLine("PASS "+message);}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("helper-costume-test");p.Level=99;p.Gold=1000000;
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.Inventory,p.HelperCostumes,p.CraftingReceipts,p.ActivityReceipts,p.Activities});
            GsHelperCostumeCraftRequest Craft(int level,int jump=1)=>new(){baseItemId=(uint)(LocalHelperCostumeDefinition.FirstItemId+level),targetItemId=(uint)(LocalHelperCostumeDefinition.FirstItemId+level+jump)};
            async Task Reject(GsHelperCostumeCraftRequest req,string label){string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_HELPER_COSTUME_CRAFT_REQUEST,req,success:false);Check(State()==before,label+" leaves all state unchanged");}
            foreach(var item in c.Items.Where(x=>LocalHelperCostumeDefinition.IsItem(x.Id))) {
                var native=c.ItemsById[LocalHelperCostumeDefinition.TemplateId(item.Id)];
                Check(item.EnchantValue==native.EnchantValue&&item.AttributeValueId.SequenceEqual(native.AttributeValueId)&&item.AttributeValueIdType.SequenceEqual(native.AttributeValueIdType)&&item.Material1Value.SequenceEqual(native.Material1Value),"Native attributes and recipe quantities at +"+item.EnchantValue);
                Check(c.Helpers.All(h=>h.FindModel(item.Id)?.ModelFile==LocalHelperCostumeDefinition.ModelFile(Array.IndexOf(LocalHelperCostumeDefinition.Helpers,(int)h.HelperType))),"All three helpers resolve shared item +"+item.EnchantValue);
            }
            Check(c.Items.Count(x=>LocalHelperCostumeDefinition.IsItem(x.Id))==11&&c.ItemsById[1900030011].Next1Id==0,"Exactly eleven unique IDs and a +10 terminal row");
            var defs=await peer.Request<GsGetDebugPromotionInfoOk>(GameServerCommand.GS_GET_DEBUG_PROMOTION_DATA_REQUEST,new GsGetDebugPromotionInfoRequest());
            var activity=defs.promotionDataList.Single(x=>x.Id==900011);Check(activity.PromotionInfoList.Count==3&&activity.PromotionInfoList.All(x=>x.ItemId[0]==1900030001),"Activity rewards grant only the shared base garment, never upgraded IDs");
            GsReceivePromotionRewardRequest Claim(uint id)=>new(){eventId=900011,dataId=id,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION};
            var claim=Claim(1);string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,claim,success:false);Check(State()==before,"Incomplete activity cannot grant a garment");
            p.Activities.Total["MCT_CLEAR_PVE"]=100;
            foreach(uint goal in new uint[]{1,2,3}) {
                claim=Claim(goal);await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,claim);
                before=State();byte[] bytes=peer.LastPayload.ToArray();await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,claim,retry:true);Check(State()==before&&bytes.SequenceEqual(peer.LastPayload),"Activity receipt prevents duplicate grant "+goal);
            }
            Check(p.Inventory[1900030001]==36&&p.Inventory[280000011]==8&&p.Inventory[280000012]==16&&p.Inventory[280000013]==50,"Activity supplies exact native copies and cloth for one complete +10 path");
            foreach(uint helper in new uint[]{1,2,3})await peer.Request<GsUpdateHelperCostumeOk>(GameServerCommand.GS_UPDATE_HELPER_COSTUME_REQUEST,new GsUpdateHelperCostumeRequest{helperId=helper,itemId=1900030001});
            Check(p.HelperCostumes.Values.All(x=>x==1900030001)&&p.Inventory[1900030001]==36,"One shared stack dresses all three helpers without triple consumption");
            await Reject(Craft(0,2),"Skipped enchant target");p.Level=7;await Reject(Craft(0),"Original level-eight unlock");p.Level=99;
            p.Inventory.Remove(280000011);await Reject(Craft(0),"Missing fabric after valid garment");p.Inventory[280000011]=8;
            for(int level=0;level<10;level++) {
                var source=c.ItemsById[1900030001+level];var req=Craft(level);uint gold=p.Gold;
                if(level==1){p.Gold=0;await Reject(req,"Insufficient coins");p.Gold=gold;}
                var result=await peer.Request<GsHelperCostumeCraftOk>(GameServerCommand.GS_HELPER_COSTUME_CRAFT_REQUEST,req);
                Check(p.HelperCostumes.Values.All(x=>x==req.targetItemId)&&result.helperInfoList.All(x=>x.costumeId==req.targetItemId)&&result.userItemList.First().itemID==req.targetItemId,"Authoritative worn state and signed deltas at +"+(level+1));
                before=State();byte[] bytes=peer.LastPayload.ToArray();await peer.Request<GsHelperCostumeCraftOk>(GameServerCommand.GS_HELPER_COSTUME_CRAFT_REQUEST,req,retry:true);Check(State()==before&&bytes.SequenceEqual(peer.LastPayload),"Enchant retry never charges twice at +"+(level+1));
            }
            Check(p.Gold==745000&&p.Inventory.GetValueOrDefault(1900030011u)==1&&!p.Inventory.Keys.Any(id=>id>=1900030001&&id<1900030011),"Complete native +10 path consumes 36 base copies and 255000 gold exactly");
            await Reject(Craft(10),"Beyond +10");
            // A lower garment worn on another helper is itself consumed as a
            // material; no helper may retain a dangling zero-count outfit ID.
            p.Inventory[1900030001]=1;p.Inventory[1900030002]=1;p.Inventory[280000011]=5;p.HelperCostumes[1]=1900030002;p.HelperCostumes[2]=1900030001;
            await peer.Request<GsHelperCostumeCraftOk>(GameServerCommand.GS_HELPER_COSTUME_CRAFT_REQUEST,Craft(1));
            Check(p.HelperCostumes[1]==1900030003&&p.HelperCostumes[2]==1900030003,"Consumed last base copy repairs all affected equipped references");
            // Existing original clothing now uses the same native endpoint.
            p.Inventory[220003004]=2;p.Inventory[280000011]=3;
            await peer.Request<GsHelperCostumeCraftOk>(GameServerCommand.GS_HELPER_COSTUME_CRAFT_REQUEST,new GsHelperCostumeCraftRequest{baseItemId=220003004,targetItemId=220003104});
            Check(p.Inventory.GetValueOrDefault(220003104u)==1&&p.Inventory.GetValueOrDefault(220003004u)==0,"Original garment recipe remains unchanged and craftable");
            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var p=runtime.Store.All().Single();Check(p.HelperCostumes[1]==1900030003&&p.HelperCostumes[3]==1900030011&&p.Activities.PermanentClaims.Count(x=>x.StartsWith("900011/"))==3&&p.CraftingReceipts.Count>=12,"Worn levels, inventory, claims and receipts survive server restart");
        }
        string report=typeof(LocalRuntime).Assembly.GetName().Name=="Bnsm.EmbeddedServer"?"helper-costume-portable-latest.json":"helper-costume-protocol-latest.json";
        File.WriteAllText(Path.Combine("I:/BnsmModernWork/Verification",report),JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=Hash(typeof(LocalRuntime).Assembly.Location),activityConfigSha256=Hash(Path.Combine(root,"LocalServer/data/activities.json"))},new JsonSerializerOptions{WriteIndented=true}));
    }
}
