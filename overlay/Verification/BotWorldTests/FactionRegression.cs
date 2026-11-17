using System.IO.Compression;
using System.Text.Json;

internal static class FactionRegression
{
    internal static void Prepare(LocalRuntime runtime)
    {
        var p=runtime.Store.GetOrCreate("local-player");var c=runtime.Catalog;p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=1000000;p.CashPoint=1000000;
        p.ClearStage=0;p.ClearHardStage=0;
        p.CardIds=c.UsableHeroes.Where(x=>x.Rank==6&&!LocalHongCard.IsCard(x.Id)).GroupBy(x=>x.EvolveFirstAncestor).Select(x=>(uint)x.First().Id).ToList();p.CardStates.Clear();
        for(ulong sequence=1;sequence<=(ulong)p.CardIds.Count;sequence++)p.CardStates[sequence]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=5,ActiveSkills=[30,30],PassiveSkills=[30,30,30,30,30]};
        p.Deck=[1,2,3,4,5];
        for(uint index=1;index<=6;index++)p.Decks[25+index]=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).Where(x=>ProtocolRouter.FactionCardAllowed(c.Cards.Single(row=>row.Id==x.id),index)).Take(5).Select(x=>x.seq).ToList();
        p.Faction.Day=(ProtocolRouter.Now+8*3600)/86400;
        runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder,bool desktopPages=false,bool stagePreview=false)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);Prepare(runtime);
        foreach(var bot in runtime.Router.Bots.All())runtime.Router.Bots.Configure(bot.Uid,"casual",bot.Style);
        if(desktopPages) {
            var p=runtime.Store.All().Single();p.AttendanceDays=1;p.AttendanceLastDay=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceLastTime=ProtocolRouter.Now;
            p.ShortTermDays=1;p.ShortTermLastDay=p.AttendanceLastDay;p.AttendanceVipClaimedDay=p.AttendanceLastDay;
            var gear=new LocalBotBuilder(runtime.Catalog).Build(999002,"UI fixture","legend","balanced",42,ProtocolRouter.Now).Profile;
            p.EquipmentItems.AddRange(gear.EquipmentItems);p.NextEquipmentSequence=gear.NextEquipmentSequence;
            p.Inventory[120101003]=9;p.Inventory[120101004]=12;p.Inventory[260000001]=100;
            p.ClearStage=runtime.Catalog.Stages.Keys.Max();p.Decks[8]=p.Deck.ToList();p.Decks[50]=p.Deck.ToList();
            foreach(var bot in runtime.Router.Bots.All().Take(3))p.Friends.Links[bot.Uid]=new(){Joined=ProtocolRouter.Now};
            runtime.Store.Save();
        }
        if(stagePreview) {
            // This isolated fixture exercises every resource faction regardless
            // of today's weekday. Production retains ordinary weekly opening.
            var rules=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime.Router)!;
            var allOpen=rules.Operations.First(x=>x.Type=="RESOURCE_STAGE_ALL_OPEN");
            allOpen.Start=DateTimeOffset.FromUnixTimeSeconds((long)ProtocolRouter.Now).UtcDateTime.AddHours(8).AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss");allOpen.End=DateTimeOffset.FromUnixTimeSeconds((long)ProtocolRouter.Now).UtcDateTime.AddHours(8).AddDays(1).ToString("yyyy-MM-dd HH:mm:ss");
            var p=runtime.Store.All().Single();p.Stamina=1000;p.StaminaStamp=ProtocolRouter.Now;p.ExchangeDay=(ProtocolRouter.Now+8*3600)/86400;
            p.StageRecords[48]=new(){Stars=3};p.StageRecords[49]=new(){Stars=3};p.StageRecords[13]=new(){Stars=3};
            p.ClearHardStage=runtime.Catalog.HardStages.Keys.Max();p.HardStageRecords[13]=new(){Stars=3};
            p.HardBattleCounts[13]=3;runtime.Store.Save();
            p.Decks[21]=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).GroupBy(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.id).Faction).Select(x=>x.First().seq).Concat(p.Deck).Distinct().Take(5).ToList();
            // Real, legal mixed-role legend builds keep a native victory smoke
            // independent of the arbitrary ordering of all six-star heroes.
            foreach(var row in new BoardResourceCatalog(root).Resources) {
                uint faction=row.EnterType==BscTable.CARD_FACTION.FACTION_ALL?0u:(uint)row.EnterType;
                uint index=faction==1?1u:faction==2?3u:5u;
                var bot=new LocalBotBuilder(runtime.Catalog).Build(999010+(uint)row.Id,"Resource fixture","legend","burst",42,ProtocolRouter.Now,factionDeck:index).Profile;
                var equipment=new Dictionary<ulong,ulong>();
                foreach(var item in bot.EquipmentItems) {
                    ulong old=item.Sequence;item.Sequence=p.NextEquipmentSequence++;equipment[old]=item.Sequence;p.EquipmentItems.Add(item);
                }
                var deck=new List<ulong>();
                foreach(ulong old in bot.Deck) {
                    p.CardIds.Add(bot.CardIds[(int)old-1]);ulong sequence=(ulong)p.CardIds.Count;var state=bot.CardStates[old];
                    if(state.WeaponSequence!=0)state.WeaponSequence=equipment[state.WeaponSequence];
                    state.AccessorySequences=state.AccessorySequences.Select(x=>x==0?0:equipment[x]).ToList();
                    p.CardStates[sequence]=state;deck.Add(sequence);
                }
                foreach(var item in bot.Inventory)p.Inventory[item.Key]=p.Inventory.GetValueOrDefault(item.Key)+item.Value;
                p.Decks[(uint)row.UseDeck]=deck;
            }
            runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,gamePort=runtime.GamePort,saveDirectory=Path.Combine(folder,"saves")}));
        await Task.Delay(TimeSpan.FromMinutes(20));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","faction-protocol-"+Guid.NewGuid().ToString("N"));var checks=new List<string>();
        void Check(bool condition,string text){if(!condition)throw new InvalidDataException(text);checks.Add(text);}
        uint expected=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            Prepare(runtime);var p=runtime.Store.All().Single();var builder=new LocalBotBuilder(runtime.Catalog);
            foreach(uint index in Enumerable.Range(1,6).Select(x=>(uint)x))foreach(string strength in LocalBotWorld.Strengths) {
                var bot=builder.Build(999001,"人机验证",strength,"balanced",42,ProtocolRouter.Now,factionDeck:index);builder.Validate(bot);
                Check(bot.Profile.CardIds.Count==5&&bot.Profile.CardIds.All(id=>ProtocolRouter.FactionCardAllowed(runtime.Catalog.Cards.Single(x=>x.Id==id),index)),"Five legal faction cards and equipment for deck "+index+" / "+strength);
            }
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            var entrance=await peer.Request<GsGetPvpBattleEntranceInfoOk>(GameServerCommand.GS_GET_PVP_ENTRANCE_INFO_REQUEST,new GsGetPvpBattleEntranceInfoRequest());
            Check(entrance.factionArenaBattleDeckIndex is >=1 and <=6&&entrance.factionArenaBattleBuyBonusCountInfo!=null,"Native entrance info supplies a valid faction deck and ticket object");
            var lobby=await peer.Request<GsEnterFactionArenaBattleOk>(GameServerCommand.GS_ENTER_FACTION_ARENA_BATTLE_REQUEST,new GsEnterFactionArenaBattleRequest());
            Check(lobby.playInfo.battleCount==0&&p.ClearStage==0&&p.ClearHardStage==0,"Faction lobby opens without main-story progress");
            var ranks=await peer.Request<GsGetFactionArenaBattleRankInfoOk>(GameServerCommand.GS_GET_FACTION_ARENA_BATTLE_RANK_INFO_REQUEST,new GsGetFactionArenaBattleRankInfoRequest{requestCount=10});
            Check(ranks.rankInfoList.Count==0,"Fresh local ranking does not fabricate wins for bot residents");
            var match=await peer.Request<GsGetFactionArenaBattleEnemyInfoOk>(GameServerCommand.GS_GET_FACTION_ARENA_BATTLE_ENEMY_INFO_REQUEST,new GsGetFactionArenaBattleEnemyInfoRequest{deckIndex=entrance.factionArenaBattleDeckIndex});var enemy=match.enemyInfo;
            Check(enemy.cardInfoList.Count==5&&enemy.cardInfoList.All(x=>x.cardEquipInfo.Count==6&&x.skillLevel.Count==7),"Real configured bot opponent carries all equipment, gems and seven skill levels");
            Check(runtime.Router.Bots.Find(enemy.rankInfo.userInfo.uid)!=null&&enemy.rankInfo.userInfo.commonInfo.nick.Contains("人机"),"Match identifies a persistent bot instead of a hardcoded local-player identity");
            Check(p.Faction.Used==0,"Opening an opponent preview alone does not consume an entry");
            var end=new GsUpdateFactionArenaBattleResultRequest{targetUid=enemy.rankInfo.userInfo.uid,battleResult=0,battleEndSyncData=new(),battleHistory=new()};end.useCardSeq.AddRange(p.Faction.Cards);
            await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end,success:false);
            Check(p.Faction.Battles==0&&p.Faction.Points==0,"A claimed win without a matching replay is rejected atomically");
            var replay=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_FACTION_PVP,winTeamIndex=0};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=p.Faction.Opponent});
            uint slot=0;foreach(ulong sequence in p.Faction.Cards)replay.battleCardInfo.Add(new(){teamIndex=0,uid=p.Uid,cardId=p.CardIds[(int)sequence-1],cardSlotIndex=slot++});
            slot=0;foreach(var card in enemy.cardInfoList)replay.battleCardInfo.Add(new(){teamIndex=1,uid=p.Faction.Opponent,cardId=card.cardId,cardSlotIndex=slot++});
            replay.battleProcessInfo.Add(new());byte[] packed=WireCodec.Serialize(replay);byte[] compressed;
            using(var output=new MemoryStream()){using(var zip=new ZLibStream(output,CompressionLevel.Fastest,true))zip.Write(packed);compressed=output.ToArray();}
            int half=compressed.Length/2;
            var chunk1=new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_FACTION_PVP,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)half,replayData=compressed[..half],firstSendDataFlag=true};
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,chunk1);
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,chunk1,retry:true);
            var chunk2=new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_FACTION_PVP,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)(compressed.Length-half),replayData=compressed[half..]};
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,chunk2);
            Check(p.Faction.ReplayWinner==0,"Chunked native compressed replay uploads once despite a retried chunk");
            end.targetUid++;await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end,success:false);end.targetUid--;
            Check(p.Faction.Battles==0&&p.Faction.Opponent==end.targetUid,"Wrong target cannot claim a pending opponent's result");
            end.battleResult=1;await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end,success:false);end.battleResult=0;
            end.useCardSeq.AddRange(Enumerable.Repeat(0ul,15-end.useCardSeq.Count));end.battleHistory=null;end.battleEndSyncData=null;
            var won=await peer.Request<GsUpdateFactionArenaBattleResultOk>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end);
            Check(won.playInfo.winCount==1&&won.playInfo.battleCount==1&&p.Faction.Used==1&&p.Faction.Points>0,"Validated win settles points, daily use and real match statistics");
            string settled=JsonSerializer.Serialize(p.Faction);await peer.Request<GsUpdateFactionArenaBattleResultOk>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end,retry:true);
            Check(JsonSerializer.Serialize(p.Faction)==settled,"Result retry cannot duplicate points, tickets or grade rewards");
            await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_FACTION_ARENA_BATTLE_RESULT_REQUEST,end,success:false);
            Check(p.Faction.Battles==1,"Fresh request sequence cannot settle the same battle twice");
            Check(p.Missions.Counters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_ENTER_FACTION_PVP)==1&&p.Missions.Counters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_WIN_FACTION_PVP)==1,"Actual faction participation and win count once toward missions and activities");
            expected=p.Faction.Points;runtime.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false))Check(restarted.Store.All().Single().Faction.Points==expected,"Faction points and statistics survive a fresh server instance");
        File.WriteAllText("I:/BnsmModernWork/Verification/faction-protocol-test.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,realSaveChanged=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("FACTION_PROTOCOL_PASSED "+checks.Count);
    }
}
