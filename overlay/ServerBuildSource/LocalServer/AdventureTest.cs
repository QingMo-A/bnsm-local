using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

internal static class AdventureTest
{
    public static void WriteFixture(string root,string output)
    {
        if(!Path.GetFullPath(output).StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Dedicated verification output required");
        var c=new GameCatalog(root);
        // Leave a next-level row for this APK's ExpMax getter; it always reads level+1.
        uint playerLevel=(uint)Math.Min(99,c.PlayerStates.Max(x=>x.Level)-1);
        BscTable.CARD_CLASS[] classes=[BscTable.CARD_CLASS.CLASS_BLADE_MASTER,BscTable.CARD_CLASS.CLASS_DESTROYER,BscTable.CARD_CLASS.CLASS_FORCE_MASTER,BscTable.CARD_CLASS.CLASS_SUMMONER,BscTable.CARD_CLASS.CLASS_GUN_MASTER];
        var team=classes.Select(job=>c.UsableHeroes.Where(x=>x.Class==job&&x.Talent==BscTable.CARD_TALENT.CARD_TALENT_SS&&x.Rank<=8).OrderByDescending(x=>x.Rank).ThenBy(x=>x.Id).First()).ToList();
        var p=new LocalPlayer {Uid=100001,Account="local-player",Level=playerLevel,Exp=(uint)c.PlayerStates.Single(x=>x.Level==playerLevel).Exp,
            Gold=10000000,CashPoint=100000,Stamina=3000,StaminaStamp=ProtocolRouter.Now,SkillPoint=10,SkillPointStamp=ProtocolRouter.Now,
            ClearStage=29,ClearHardStage=29,ClearLegendStage=12,CardIds=team.Select(x=>(uint)x.Id).ToList(),Deck=[1,2,3,4,5]};
        for(int i=0;i<p.CardIds.Count;i++) {
            var card=team[i];uint level=Math.Min(playerLevel,(uint)c.CardRanks.Single(x=>x.Rank==card.Rank).MaxLevel);
            p.CardStates[(ulong)i+1]=new(){Level=level,Exp=(uint)c.CardLevels.Single(x=>x.Level==level).Rank1,Enchant=5,
                ActiveSkills=[20,20],PassiveSkills=[20,20,20,20,20]};
        }
        var ownedPieces=c.Cards.Where(x=>p.CardIds.Contains((uint)x.Id)).Select(x=>x.PieceId).ToHashSet();
        var recipe=c.Pieces.First(x=>c.UsableHeroes.Any(y=>y.Id==x.NextId)&&!ownedPieces.Contains(x.ItemId));
        p.Inventory[(uint)recipe.ItemId]=recipe.Complete;
        for(uint id=13;id<=29;id++){p.StageRecords[id]=new(){Stars=3};p.HardStageRecords[id]=new(){Stars=3};}
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"players.json"),JsonSerializer.Serialize(new[]{p},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"adventure-fixture.json"),JsonSerializer.Serialize(new {pieceId=recipe.ItemId,cardId=recipe.NextId,cost=recipe.Complete}));
    }

    private sealed class Peer : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec;
        public SessionKeyInfo Key=new();
        public Peer(string root) {codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        public async Task Login(LocalRuntime server,string account="adventure-test")
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
            var started=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload);
            Key=new(){uid=started.uid,loginSessionKey=started.loginSessionKey};
            await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));
            using var timeout=new CancellationTokenSource(12000);
            var frame=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);
            return (frame.Command,frame.Payload);
        }
        public async Task<byte[]> Request(GameServerCommand cmd,object request,bool success=true,bool advance=true)
        {
            if(advance)Key.sequenceNo++;
            var response=await Send(socket,(ushort)cmd,request);
            string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)response.Command).ToString()!=expected)throw new Exception($"Expected {expected}; got {(GameServerCommand)response.Command}: "+Read<FailPacket>(response.Payload).message);
            return response.Payload;
        }
        public Task<byte[]> Combine(uint[] ids,bool success=true,bool advance=true) {var r=new GsCombinePieceRequest{keyInfo=Key};r.itemIdList.AddRange(ids);return Request(GameServerCommand.GS_COMBINE_PIECE_REQUEST,r,success,advance);}
        public Task<byte[]> Claim(uint field,uint reward,BscTable.GAME_DIFFICULTY difficulty,bool success=true,bool advance=true)=>Request(GameServerCommand.GS_ACHIEVE_REWARD_REQUEST,new GsAchieveRewardRequest{keyInfo=Key,fieldID=field,rewardID=reward,difficulty=difficulty},success,advance);
        public async Task<GsGetUserGameInfoOk> Info()=>Read<GsGetUserGameInfoOk>(await Request(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest{keyInfo=Key}));
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    public static async Task Run(string root)
    {
        // Guard both the legacy file and the actual version-independent user profiles.
        var protectedFiles=new List<string>{Path.Combine(root,"LocalServer/saves/players.json")};
        if(Directory.Exists(SaveWorkspace.DefaultRoot))protectedFiles.AddRange(Directory.GetFiles(SaveWorkspace.DefaultRoot,"players.json",System.IO.SearchOption.AllDirectories));
        var hashes=protectedFiles.Where(File.Exists).ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        string save=Path.Combine(root,"verification/adventure",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        string State(LocalPlayer p)=>JsonSerializer.Serialize(new {p.CardIds,p.Inventory,p.Gold,p.CashPoint,p.NormalFieldRewardClaims,p.HardFieldRewardClaims,p.LegendFieldRewardClaims,p.EquipmentItems});
        uint recipeId=0,cardId=0;ulong sequence=0;
        const BscTable.GAME_DIFFICULTY Normal=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,Hard=BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,Legend=BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND;
        await using(var server=new LocalRuntime(root,save,0,0,0)) {
            using var peer=new Peer(root);await peer.Login(server);
            var c=server.Catalog;var p=server.Store.Find(peer.Key.uid)!;
            Check(c.Pieces.Count==120&&c.Pieces.All(x=>x.Complete==2),"All 120 same-version fragment recipes specify two pieces");
            var recipes=c.Pieces.Where(x=>c.UsableHeroes.Any(y=>y.Id==x.NextId)).ToList();
            var recipe=recipes.First(x=>!p.CardIds.Contains((uint)x.NextId));recipeId=(uint)recipe.ItemId;cardId=(uint)recipe.NextId;
            Check(c.Cards.Single(x=>x.Id==cardId).Rank==1,"Fragment summon resolves the exact original one-star NextId");
            async Task Bad(uint[] ids,string label) {string before=State(p);await peer.Combine(ids,false);Check(before==State(p),label);}
            await Bad([],"Empty summon is rejected without mutation");
            await Bad([uint.MaxValue],"Unknown piece id cannot create a card");
            p.Inventory[recipeId]=recipe.Complete-1;await Bad([recipeId],"Insufficient fragments do not consume items");
            p.Inventory[recipeId]=recipe.Complete;await Bad([recipeId,recipeId],"Duplicate fragment ids cannot double summon");
            var second=recipes.First(x=>x.ItemId!=recipeId&&!p.CardIds.Contains((uint)x.NextId));
            await Bad([recipeId,(uint)second.ItemId],"Mixed batch validates every recipe before consuming any fragments");
            var starters=p.CardIds.ToList();p.CardIds=Enumerable.Repeat(starters[0],300).ToList();
            await Bad([recipeId],"Full card bag preserves the fragments");p.CardIds=starters;
            uint gold=p.Gold,cash=p.CashPoint;int beforeCards=p.CardIds.Count;
            byte[] first=await peer.Combine([recipeId]);var summoned=Read<GsCombinePieceOk>(first);sequence=summoned.cardInfoList.Single().cardSeq;
            Check(summoned.cardInfoList[0].cardID==cardId&&summoned.cardInfoList[0].cardLevel==1,"Summon returns a typed level-one card with matching original identity");
            Check(summoned.itemInfoList.Single().itemCount==-recipe.Complete&&!p.Inventory.ContainsKey(recipeId),"Native item response is the exact negative fragment delta");
            Check(p.CardIds.Count==beforeCards+1&&p.CardIds[(int)sequence-1]==cardId&&p.Gold==gold&&p.CashPoint==cash,"Summon adds one stable sequence without inventing a gold or diamond charge");
            Check(p.CollectedCardIds.Contains(cardId),"Summon updates the collection history");
            string after=State(p);byte[] retry=await peer.Combine([recipeId],advance:false);
            Check(first.SequenceEqual(retry)&&after==State(p),"Identical encrypted request retry replays without another summon or charge");
            await peer.Combine([(uint)second.ItemId],false,false);Check(after==State(p),"Same packet sequence with a changed payload is rejected");
            p.Inventory[recipeId]=100;await Bad([recipeId],"Owned character cannot turn two pieces into duplicate conversion rewards");
            var higher=c.UsableHeroes.FirstOrDefault(x=>x.PieceId==recipeId&&x.Rank>1);
            if(higher!=null){p.CardIds[(int)sequence-1]=(uint)higher.Id;await Bad([recipeId],"Owned evolved character also keeps its fragments for rank up");p.CardIds[(int)sequence-1]=cardId;}
            p.Inventory[(uint)second.ItemId]=second.Complete;
            var third=recipes.First(x=>x.ItemId!=recipeId&&x.ItemId!=second.ItemId&&!p.CardIds.Contains((uint)x.NextId));p.Inventory[(uint)third.ItemId]=third.Complete+3;
            var batch=Read<GsCombinePieceOk>(await peer.Combine([(uint)second.ItemId,(uint)third.ItemId]));
            Check(batch.cardInfoList.Count==2&&batch.itemInfoList.Count==2&&p.Inventory[(uint)third.ItemId]==3,"Batch summon creates exactly one card per recipe and keeps excess fragments");
            using(var everyPiece=new Peer(root)) {
                await everyPiece.Login(server,"all-fragment-recipes");var fixture=server.Store.Find(everyPiece.Key.uid)!;
                foreach(var row in recipes) {
                    fixture.CardIds=[(uint)c.InitialHeroes.First(x=>x.PieceId!=row.ItemId).Id];fixture.CardStates.Clear();fixture.Inventory.Clear();
                    fixture.Inventory[(uint)row.ItemId]=row.Complete+1;
                    var made=Read<GsCombinePieceOk>(await everyPiece.Combine([(uint)row.ItemId]));
                    Check(made.cardInfoList.Single().cardID==row.NextId&&made.cardInfoList[0].cardLevel==1&&fixture.Inventory[(uint)row.ItemId]==1,$"Fragment {row.ItemId} summons its original initial card {row.NextId}");
                }
                foreach(var row in c.Pieces.Where(x=>!recipes.Contains(x))) {
                    fixture.Inventory[(uint)row.ItemId]=row.Complete;
                    string prior=State(fixture);await everyPiece.Combine([(uint)row.ItemId],false);
                    Check(prior==State(fixture),$"Unavailable or placeholder recipe {row.ItemId} is rejected without consuming fragments");
                }
            }

            // Actual locked and earned states, independent of client-provided totals.
            p.Level=30;p.Exp=(uint)c.PlayerStates.Single(x=>x.Level==30).Exp;p.ClearStage=15;
            async Task BadClaim(uint f,uint r,BscTable.GAME_DIFFICULTY d,string label){string before=State(p);await peer.Claim(f,r,d,false);Check(before==State(p),label);}
            await BadClaim(3,1,Normal,"A reward cannot be claimed with no earned stars");
            p.StageRecords[13]=new(){Stars=3};p.StageRecords[14]=new(){Stars=3};
            await BadClaim(3,2,Normal,"Six stars cannot claim the twelve-star reward");
            await BadClaim(3,1,Hard,"Normal stars do not unlock or fund a hard reward");
            await BadClaim(3,1,BscTable.GAME_DIFFICULTY.DIFFICULTY_MAX,"The protocol's sentinel difficulty is rejected");
            await BadClaim(3,0,Normal,"Reward ordinals start at one");
            await BadClaim(3,4,Normal,"Missing reward ordinal is rejected");
            await BadClaim(uint.MaxValue,1,Normal,"Unknown reward field is rejected");
            p.Inventory[210002001]=int.MaxValue;
            await BadClaim(3,1,Normal,"Overflowing reward inventory leaves claim status and all rewards unchanged");p.Inventory.Remove(210002001);
            byte[] claim=await peer.Claim(3,1,Normal);var six=Read<GsAchieveRewardOk>(claim);
            Check(six.fieldID==3&&six.rewardID==1&&six.addItemInfoList.Any(x=>x.itemID==210002001&&x.itemCount==1000),"Six-star reward uses original card experience and equipment quantities");
            Check(six.addItemInfoList.Any(x=>x.itemID==100073058&&x.itemCount==1)&&p.EquipmentItems.Any(x=>x.ItemId==100073058),"Equipment reward has a real owned instance and unique inventory sequence");
            var info=await peer.Info();Check(info.normalAchieveRewardInfo[6]==2&&info.hardAchieveRewardInfo[6]==0&&info.legendAchieveRewardInfo[6]==0,"Game info uses field*2 and bit 1<<rewardId for all three difficulty arrays");
            // Retries must keep the original sequence; Info above advanced it.
            await BadClaim(3,1,Normal,"Claiming an already received reward in a new request is rejected");
            p.StageRecords[15]=new(){Stars=3};p.StageRecords[16]=new(){Stars=3};
            byte[] twelve=await peer.Claim(3,2,Normal);after=State(p);
            byte[] twelveRetry=await peer.Claim(3,2,Normal,advance:false);
            Check(twelve.SequenceEqual(twelveRetry)&&after==State(p),"Reward retry returns the same payload without granting twice");
            for(uint id=13;id<=18;id++)p.StageRecords[id]=new(){Stars=3};p.ClearStage=29;
            uint priorGold=p.Gold;var eighteen=Read<GsAchieveRewardOk>(await peer.Claim(3,3,Normal));
            Check(eighteen.addGold==30000&&p.Gold==priorGold+30000,"Eighteen-star normal reward grants the original 30000 gold");
            Check((await peer.Info()).normalAchieveRewardInfo[6]==14,"All three claimed normal gifts remain set together");
            p.ClearHardStage=18;for(uint id=13;id<=18;id++)p.HardStageRecords[id]=new(){Stars=3};
            uint beforeCash=p.CashPoint;var hard=Read<GsAchieveRewardOk>(await peer.Claim(3,1,Hard));
            Check(hard.addCashPoint==100&&p.CashPoint==beforeCash+100,"Hard six-star reward grants its original 100 diamonds");
            await BadClaim(3,1,Legend,"Legend reward does not reuse hard stars");
            p.LegendStageRecords[14]=new(){Stars=3};p.ClearLegendStage=14;
            var legend=Read<GsAchieveRewardOk>(await peer.Claim(3,1,Legend));
            Check(legend.addGold==50000,"Legend first gift requires three stars and grants the original 50000 gold");
            info=await peer.Info();Check(info.normalAchieveRewardInfo[6]==14&&info.hardAchieveRewardInfo[6]==2&&info.legendAchieveRewardInfo[6]==2,"Difficulty claim states do not overwrite each other");

            var dungeon=c.Dungeons[Hard].Values.First();uint dungeonId=(uint)dungeon.Id;
            Check(c.WaveIds(Hard,dungeonId,true).Count>0,"Original hidden dungeon has recovered enemy wave variants");
            async Task<byte[]> DungeonStart(uint flag=1,bool success=true)=>await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{keyInfo=peer.Key,difficulty=Hard,stageid=dungeonId,dungeonFlag=flag},success);
            uint staminaBefore=p.Stamina;
            await DungeonStart(success:false);Check(p.ActiveStage==null&&p.Stamina==staminaBefore,"Hidden dungeon respects original OpenStageId before charging stamina");
            p.ClearHardStage=(uint)dungeon.OpenStageId;p.ClearStage=179;p.Stamina=3000;
            var entered=Read<GsNormalBattleStartOk>(await DungeonStart());
            Check(p.ActiveDungeon&&p.Stamina==3000-dungeon.ConsumeStamina&&entered.waveProbIdList.Count>0,"Hidden dungeon enters with its original stamina and enemy waves");
            var ending=new GsNormalBattleEndConfirmRequest{keyInfo=peer.Key,difficulty=Hard,stageId=dungeonId,dungeonFlag=0,timeStamp=entered.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS};
            await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,ending,false);
            Check(p.ActiveStage==dungeonId,"A hidden battle cannot settle using a normal-stage flag");
            ending.dungeonFlag=1;var ended=Read<GsNormalBattleEndConfirmOk>(await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,ending));
            Check(ended.clearStageID==dungeon.OpenStageId&&p.ClearHardStage==dungeon.OpenStageId&&!p.ActiveDungeon,"Hidden battle does not overwrite campaign progress with its separate id");
            Check((await peer.Info()).hardDungeonAchieveInfo[dungeonId*2]==3,"Hidden three-star clear is persisted and returned in the original dungeon array");
            var countInfo=Read<GsGetHardModeBattleCountOk>(await peer.Request(GameServerCommand.GS_GET_HARDMODE_BATTLE_COUNT_REQUEST,new GsGetHardModeBattleCountRequest{keyInfo=peer.Key}));
            Check(countInfo.dungeonBattleCount.Any(x=>x.stageid==dungeonId&&x.battleCount==1),"Hidden dungeon daily entries use their own protocol list");
            for(int attempt=1;attempt<dungeon.DailyLimit;attempt++) {
                var again=Read<GsNormalBattleStartOk>(await DungeonStart());ending.timeStamp=again.timeStamp;
                await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,ending);
            }
            uint depleted=p.Stamina;await DungeonStart(success:false);
            Check(p.ActiveStage==null&&p.Stamina==depleted&&p.HardDungeonBattleCounts[dungeonId]==dungeon.DailyLimit,"Hidden dungeon enforces its original daily limit without another stamina charge");
            var vip=c.Vip.First(x=>x.HardDungeonMaxResetCount>0);p.VipExp=(uint)vip.LvUpNeedVipExp;p.CashPoint=10000;
            uint resetCost=(uint)c.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_HARD_DUNGEON,1)!.Value;
            var reset=Read<GsResetHardModeBattleVIPEntranceCountOk>(await peer.Request(GameServerCommand.GS_RESET_HARDMODE_BATTLE_VIP_ENTRANCE_COUNT_REQUEST,new GsResetHardModeBattleVIPEntranceCountRequest{keyInfo=peer.Key,stageId=dungeonId,difficulty=Hard,dungeonFlag=true}));
            Check(reset.resetCount==1&&p.CashPoint==10000-resetCost&&p.HardDungeonBattleCounts[dungeonId]==0,"Hidden entry reset uses its original VIP quota and separate diamond cost");
            p.ExchangeDay--;await peer.Info();Check(p.HardDungeonBattleCounts.Count==0&&p.HardDungeonResetCounts.Count==0,"Hidden daily entries and reset quotas reset at UTC+8 midnight");

            // Audit every original reward row that the supported campaign can reach.
            var audit=server.Store.GetOrCreate("all-reward-rows");audit.Level=(uint)c.PlayerStates.Max(x=>x.Level);audit.ClearStage=179;audit.ClearHardStage=179;audit.ClearLegendStage=179;
            using var rowsPeer=new Peer(root);await rowsPeer.Login(server,"all-reward-rows");
            foreach(var d in new[]{Normal,Hard,Legend}) {
                var records=d==Normal?audit.StageRecords:d==Hard?audit.HardStageRecords:audit.LegendStageRecords;
                foreach(uint id in c.StagesFor(d).Keys)records[id]=new(){Stars=3};
                if(d==Hard)foreach(uint id in c.Dungeons[d].Keys)audit.HardDungeonStageRecords[id]=new(){Stars=3};
                foreach(var field in c.FieldRewards[d].Where(x=>StageUnlock.FieldOpen(c,audit,d,x.FieldId)))
                    for(uint r=1;r<=field.List.Count;r++) {
                        var got=Read<GsAchieveRewardOk>(await rowsPeer.Claim((uint)field.FieldId,r,d));
                        Check(got.fieldID==field.FieldId&&got.rewardID==r,$"Original {d} field {field.FieldId} gift {r} is grantable");
                    }
            }
            var disk=new PlayerStore(save,c).Find(p.Uid)!;
            Check(disk.CardIds[(int)sequence-1]==cardId&&disk.Inventory[recipeId]==100,"Summoned card and subsequent remaining fragments survive a disk reload");
            Check(disk.NormalFieldRewardClaims[3]==14&&disk.HardFieldRewardClaims[3]==2&&disk.LegendFieldRewardClaims[3]==2,"All reward masks survive a disk reload");
        }
        await using(var server=new LocalRuntime(root,save,0,0,0)) {
            using var peer=new Peer(root);await peer.Login(server);
            var info=await peer.Info();var p=server.Store.Find(peer.Key.uid)!;
            Check(info.legendAchieveRewardInfo[6]==2&&info.normalAchieveRewardInfo[6]==14,"New server and login restore claimed gift UI states");
            string before=State(p);await peer.Claim(3,1,Normal,false);await peer.Combine([recipeId],false);
            Check(before==State(p),"New login cannot reclaim gifts or summon an already owned character");
        }
        Check(hashes.All(x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key)))==x.Value),"Legacy and all real shared player profiles remain byte-for-byte unchanged");
        File.WriteAllText(Path.Combine(root,"evidence/adventure-protocol-test.json"),JsonSerializer.Serialize(new {passed=true,count=checks.Count,checks,productionSavesTouched=false},new JsonSerializerOptions{WriteIndented=true}));
        System.Console.WriteLine($"PASS: {checks.Count} fragment summon and field reward checks.");
    }
}
