using System.Text.Json;

internal static class MissionRewardRegression
{
    internal static async Task ReportedFamilies(string root)
    {
        string fixture=Path.Combine("I:/BnsmModernWork/Verification","mission-families-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();var results=new List<object>();var labels=BuildExampleExporter.Labels(root);
        await using var runtime=new LocalRuntime(root,Path.Combine(fixture,"saves"),0,0,0,false);
        var c=runtime.Catalog;var missions=new MissionCatalog(root);var p=runtime.Store.GetOrCreate("reported-families-test");
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.ClearStage=c.Stages.Keys.Max();p.ClearHardStage=c.HardStages.Keys.Max();p.Stamina=10;
        p.CollectedCardIds.UnionWith(c.UsableHeroes.Select(x=>(uint)x.Id));
        foreach(var row in missions.Rows.Values)p.Missions.Claimed[(uint)row.MissionId]=ProtocolRouter.Now;
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        foreach(int family in new[]{20011,70061,30081})
        {
            var card=c.UsableHeroes.Where(x=>ProtocolRouter.HeroIdentity(x)==family).OrderByDescending(x=>x.Rank).First();
            p.CardIds=[(uint)card.Id];p.CardStates.Clear();p.CardStates[1]=new(){Level=70,Exp=ProgressionRules.CardFloor(c,70),Enchant=5,Protect=1,ActiveSkills=[8,9],PassiveSkills=[7,6,5,4,3]};p.Deck=[1,0,0,0,0];
            string state=JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck});
            var row=missions.Rows.Values.First(x=>x.RewardId.Select((id,i)=>(id,type:x.RewardIdType[i])).Any(r=>r.type==BscTable.REWARD_TYPE.REWARD_TYPE_ITEM&&r.id==card.PieceId));
            p.Missions.Claimed.Remove((uint)row.MissionId);int old=p.Inventory.GetValueOrDefault((uint)card.PieceId);
            var reply=await peer.Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=(uint)row.MissionId});
            string name=labels(card.NameAlias);int amount=row.RewardQuantity[row.RewardId.IndexOf(card.PieceId)];
            if(state!=JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck})||reply.rewardCardInfoList.Count!=0)throw new InvalidDataException(name+" changed");
            checks.Add(name+" keeps its evolved card, sequence, level, enchant and skills");
            if(reply.rewardItemInfoList.Single(x=>x.itemID==card.PieceId).itemCount!=amount||p.Inventory.GetValueOrDefault((uint)card.PieceId)!=old+amount)throw new InvalidDataException(name+" fragment delta mismatch");
            checks.Add(name+" receives exact additive fragments");results.Add(new{name,family,cardId=card.Id,missionId=row.MissionId,pieceId=card.PieceId,amount});
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/mission-reported-families.json",JsonSerializer.Serialize(new{passed=true,checks,results,fixture,originalPlayerSaveUsed=false},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        Console.WriteLine("REPORTED_FAMILIES_PASSED "+checks.Count);
    }
    internal static async Task Run(string root)
    {
        string fixture=Path.Combine("I:/BnsmModernWork/Verification","mission-rewards-"+Guid.NewGuid().ToString("N")),save=Path.Combine(fixture,"saves");
        Directory.CreateDirectory(fixture);var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);Console.WriteLine("PASS "+text);}
        // These clocks refresh on requests independently of reward settlement.
        string State(LocalPlayer p)=>JsonSerializer.Serialize(JsonSerializer.SerializeToElement(p).EnumerateObject().Where(x=>x.Name is not ("LastActivity" or "SkillPointStamp")).ToDictionary(x=>x.Name,x=>x.Value));
        var expectedTitles=new HashSet<uint>();uint playerUid=0;
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false))
        {
            var c=runtime.Catalog;var mc=new MissionCatalog(root);var p=runtime.Store.GetOrCreate("mission-reward-regression");playerUid=p.Uid;
            p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=100000;p.CashPoint=1000;p.Stamina=10;
            p.ClearStage=c.Stages.Keys.Max();p.ClearHardStage=c.HardStages.Keys.Max();p.ClearLegendStage=c.LegendStages.Keys.Max();
            p.CollectedCardIds.UnionWith(c.UsableHeroes.Select(x=>(uint)x.Id));
            foreach(var row in mc.Rows.Values){p.Missions.Claimed[(uint)row.MissionId]=ProtocolRouter.Now;p.Missions.EnteredDeckMissions.Add((uint)row.MissionId);p.StageRecords[(uint)row.Check1Id]=new(){Stars=3,PerfectTime=1};p.HardStageRecords[(uint)row.Check1Id]=new(){Stars=3,PerfectTime=1};p.LegendStageRecords[(uint)row.Check1Id]=new(){Stars=3,PerfectTime=1};p.HardDungeonStageRecords[(uint)row.Check1Id]=new(){Stars=3,PerfectTime=1};}
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            async Task<GsMissionRewardOk> Claim(uint id,bool retry=false)=>await peer.Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=id},retry);
            foreach(var row in mc.Rows.Values.Where(x=>x.RewardIdType.Contains(BscTable.REWARD_TYPE.REWARD_TYPE_TITLE)))
            {
                uint id=(uint)row.MissionId;p.Missions.Claimed.Remove(id);
                p.HardDungeonStageRecords[(uint)row.Check1Id]=new(){Stars=3,PerfectTime=1};
                var reply=await Claim(id);uint title=(uint)row.RewardId[row.RewardIdType.IndexOf(BscTable.REWARD_TYPE.REWARD_TYPE_TITLE)];expectedTitles.Add(title);
                Check(reply.rewardTitleId==title&&p.Titles.ContainsKey(title)&&p.Missions.Claimed.ContainsKey(id),"Original title mission "+id+" grants its native title and completes");
                byte[] payload=peer.LastPayload;string before=State(p);await Claim(id,true);
                if(State(p)!=before){File.WriteAllText(Path.Combine(fixture,"before.json"),before);File.WriteAllText(Path.Combine(fixture,"after.json"),State(p));Console.WriteLine("STATE_DIFF "+fixture);}
                Check(State(p)==before&&peer.LastPayload.SequenceEqual(payload),"Title mission "+id+" receipt replay is exact and does not regrant");
            }
            Check(expectedTitles.Count==3&&expectedTitles.Contains(3015),"Collector Master 17 and both other title achievements are supported");
            int duplicateRows=0;
            foreach(var row in mc.Rows.Values.Where(x=>x.RewardIdType.Contains(BscTable.REWARD_TYPE.REWARD_TYPE_CARD)))
            {
                var rewards=row.RewardId.Select((id,i)=>(id,type:row.RewardIdType[i],count:row.RewardQuantity[i])).Where(x=>x.type==BscTable.REWARD_TYPE.REWARD_TYPE_CARD).ToArray();
                var owned=rewards.Select(x=>c.UsableHeroes.Where(card=>ProtocolRouter.HeroIdentity(card)==ProtocolRouter.HeroIdentity(c.Cards.Single(k=>k.Id==x.id))).OrderByDescending(card=>card.Rank).First()).ToArray();
                p.CardIds=owned.Select(x=>(uint)x.Id).ToList();p.CardStates.Clear();p.Deck=[1,0,0,0,0];
                for(int i=0;i<owned.Length;i++)p.CardStates[(ulong)i+1]=new(){Level=90,Exp=ProgressionRules.CardFloor(c,90),Enchant=5,Protect=1,ActiveSkills=[9,8],PassiveSkills=[7,6,5,4,3]};
                string cards=JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck,p.Decks});var inventory=new Dictionary<uint,int>(p.Inventory);
                p.Missions.Claimed.Remove((uint)row.MissionId);
                var reply=await Claim((uint)row.MissionId);
                Check(reply.rewardCardInfoList.Count==0&&JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck,p.Decks})==cards,"Mission "+row.MissionId+" converts owned/evolved heroes without replacing cards or training");
                foreach(var reward in rewards)
                {
                    var card=c.Cards.Single(x=>x.Id==reward.id);int count=GachaRules.Load(root).PieceCount(card.Talent)*reward.count;
                    Check(reply.rewardItemInfoList.Single(x=>x.itemID==card.PieceId).itemCount==count&&p.Inventory.GetValueOrDefault((uint)card.PieceId)==inventory.GetValueOrDefault((uint)card.PieceId)+count,"Mission "+row.MissionId+" saves and returns exact matching fragments");
                }
                string before=State(p);byte[] payload=peer.LastPayload;await Claim((uint)row.MissionId,true);
                if(State(p)!=before){File.WriteAllText(Path.Combine(fixture,"before.json"),before);File.WriteAllText(Path.Combine(fixture,"after.json"),State(p));Console.WriteLine("STATE_DIFF "+fixture);}
                Check(State(p)==before&&peer.LastPayload.SequenceEqual(payload),"Mission "+row.MissionId+" retry cannot add fragments twice");duplicateRows++;
            }
            Check(duplicateRows>20,"All shipped card-reward missions were exercised through encrypted packets");
            int fragmentRows=0;
            foreach(var row in mc.Rows.Values.Where(x=>x.RewardId.Select((id,i)=>(id,type:x.RewardIdType[i])).Any(x=>x.type==BscTable.REWARD_TYPE.REWARD_TYPE_ITEM&&c.ItemsById.GetValueOrDefault(x.id)?.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE)))
            {
                p.Missions.Claimed.Remove((uint)row.MissionId);string cards=JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck});var before=new Dictionary<uint,int>(p.Inventory);
                var reply=await Claim((uint)row.MissionId);
                Check(reply.rewardCardInfoList.Count==0&&cards==JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.Deck}),"Fragment mission "+row.MissionId+" never replaces any owned card");
                foreach(var item in reply.rewardItemInfoList.Where(x=>c.ItemsById[(int)x.itemID].ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE))
                    Check(p.Inventory.GetValueOrDefault(item.itemID)==before.GetValueOrDefault(item.itemID)+item.itemCount,"Fragment mission "+row.MissionId+" inventory equals reward delta");
                fragmentRows++;
            }
            Check(fragmentRows>0,"Original fragment-reward missions are covered");
            var fresh=mc.Rows[1002];uint newId=(uint)fresh.RewardId[0];p.CardIds=[(uint)c.UsableHeroes.First(x=>ProtocolRouter.HeroIdentity(x)!=ProtocolRouter.HeroIdentity(c.Cards.Single(y=>y.Id==newId))).Id];
            p.CardStates.Clear();p.CardStates[1]=new(){Level=75,Exp=ProgressionRules.CardFloor(c,75),Enchant=3,Protect=1};p.Deck=[1,0,0,0,0];string old=JsonSerializer.Serialize(p.CardStates[1]);uint oldId=p.CardIds[0];p.Missions.Claimed.Remove(1002);
            var acquired=await Claim(1002);Check(acquired.rewardCardInfoList.Single().cardSeq==2&&p.CardIds.SequenceEqual(new[]{oldId,newId})&&JsonSerializer.Serialize(p.CardStates[1])==old,"Unowned mission hero appends a new sequence and preserves the existing trained hero");
            string snapshot=State(p);await peer.Request<FailPacket>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=1002},success:false);
            Check(State(p)==snapshot,"Fast second claim with a new sequence rejects without changing rewards or cards");
            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false))
        {
            var p=runtime.Store.Find(playerUid)!;
            Check(expectedTitles.All(p.Titles.ContainsKey)&&p.Missions.Claimed.ContainsKey(3016),"Earned titles and Collector Master 17 survive server restart");
            Check(p.CardIds.Count==2&&p.CardStates[1].Level==75&&p.CardStates[1].Enchant==3,"Mission card reward preserves original training after restart");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/mission-reward-regression.json",JsonSerializer.Serialize(new{passed=true,checks,fixture,originalPlayerSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("MISSION_REWARD_REGRESSION_PASSED "+checks.Count);
    }
    internal static void Audit(string root)
    {
        var catalog=new GameCatalog(root);var missions=new MissionCatalog(root);var text=BuildExampleExporter.Labels(root);
        var rows=missions.Rows.Values.Select(row=>new{row.MissionId,check=row.CheckType.ToString(),row.NeedCount,rewards=row.RewardId.Select((id,i)=>new{id,type=row.RewardIdType[i].ToString(),count=row.RewardQuantity[i],itemExists=catalog.ItemsById.ContainsKey(id),card=catalog.Cards.FirstOrDefault(x=>x.Id==id) is { } card?text(card.NameAlias):null})}).ToArray();
        File.WriteAllText("I:/BnsmModernWork/Verification/mission-reward-audit.json",JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var row in rows.Where(x=>x.check=="MCT_COLLECT_CARD_COUNT"&&x.NeedCount==55))Console.WriteLine(JsonSerializer.Serialize(row));
        Console.WriteLine("Reward types: "+string.Join(", ",rows.SelectMany(r=>r.rewards.Select(x=>x.type)).Distinct()));
        foreach(var row in rows.Where(r=>r.rewards.Any(x=>x.card!=null&&(x.card.Contains("程宏")||x.card.Contains("云山")||x.card.Contains("老小")))))Console.WriteLine(JsonSerializer.Serialize(row,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        foreach(var card in catalog.UsableHeroes.GroupBy(ProtocolRouter.HeroIdentity).Select(g=>g.OrderBy(c=>c.Rank).First()).Where(c=>text(c.NameAlias).Contains("洪门")||text(c.NameAlias).Contains("幻鬼")))Console.WriteLine("Original hero: "+text(card.NameAlias)+" / family="+ProtocolRouter.HeroIdentity(card));
        foreach(int family in new[]{20011,70061,30081}){
            var card=catalog.UsableHeroes.First(x=>ProtocolRouter.HeroIdentity(x)==family);
            Console.WriteLine("User reported family: "+text(card.NameAlias)+" piece="+card.PieceId+" missions="+string.Join(',',missions.Rows.Values.Where(r=>r.RewardId.Select((id,i)=>(id,type:r.RewardIdType[i])).Any(x=>x.type==BscTable.REWARD_TYPE.REWARD_TYPE_ITEM&&x.id==card.PieceId||x.type==BscTable.REWARD_TYPE.REWARD_TYPE_CARD&&catalog.Cards.Any(c=>c.Id==x.id&&ProtocolRouter.HeroIdentity(c)==family))).Select(r=>r.MissionId)));
        }
    }
}
