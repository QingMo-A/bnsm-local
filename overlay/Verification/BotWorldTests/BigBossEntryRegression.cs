using System.Text.Json;
using System.Security.Cryptography;

internal static class BigBossEntryRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","big-boss-entry-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        var mixed=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).GroupBy(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.id).Faction).Select(x=>x.First().seq).Concat(p.Deck).Distinct().Take(5).ToList();
        p.Decks[21]=mixed;runtime.Store.Save();var catalog=new RaidCatalog(root);
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        var list=await peer.Request<GsGetBigBossListOk>(GameServerCommand.GS_GET_BIG_BOSS_LIST_REQUEST,new GsGetBigBossListRequest());
        Check(list.bigBossIDList.Contains(2635101)&&list.bigBossIDList.Contains(2637101),"Both reported extreme bosses are offered at the native unlock level");
        var targets=catalog.Big.Where(x=>x.BossGrade>0).OrderByDescending(x=>x.Id==2635101||x.Id==2637101).ToArray();
        foreach(var boss in targets) {
            // Reset only this isolated fixture's quotas between independent cases.
            p.Raids.Used[4]=0;p.Raids.Latest[4]=0;p.Raids.BossHp.Remove((uint)boss.Id);
            var begin=new GsBigBossBattleStartRequest{bossID=(uint)boss.Id};
            var started=await peer.Request<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,begin);
            Check(started.bossID==boss.Id&&p.Raids.BattleCards.SequenceEqual(mixed),"Native mixed-faction roster enters boss "+boss.Id);
            uint used=p.Raids.Used[4];await peer.Request<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,begin,retry:true);
            Check(p.Raids.Used[4]==used,"Start retry consumes one entry: "+boss.Id);
            uint full=catalog.Hp(boss,(uint)boss.Level);
            var end=new GsBigBossBattleEndRequest{bossID=(uint)boss.Id,timeStamp=started.timeStamp,damageAmount=1,battleEndSyncData=new()};
            var result=await peer.Request<GsBigBossBattleEndOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_END_REQUEST,end);
            Check(result.bossHp==full-1&&p.ActiveStage==null,"Native settlement preserves remaining HP: "+boss.Id);
        }
        p.Raids.Used[4]=0;p.Raids.Latest[4]=0;
        foreach(var deck in new[]{new List<ulong>(),new List<ulong>{mixed[0],mixed[0]},new List<ulong>{ulong.MaxValue}}) {
            p.Decks[21]=deck;uint used=p.Raids.Used[4];
            await peer.Request<FailPacket>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=2635101},success:false);
            Check(p.Raids.Used[4]==used&&p.ActiveStage==null,"Invalid roster is rejected without charging: "+string.Join(',',deck));
        }
        string serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/big-boss-entry-latest.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,productionSaveUsed=false,serverCoreSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("BIG_BOSS_ENTRY_PASSED "+checks.Count);
    }
}
