using System.Net.Sockets;
using System.Text.Json;

internal static class RaidTest
{
    private sealed class Peer(string root) : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();public byte[] Last=[];
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server)
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),(await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload)!;
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false)
        {
            if(!retry)Key.sequenceNo++;body.GetType().GetField("keyInfo")?.SetValue(body,Key);
            var result=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)result.Command).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)result.Command+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),result.Payload)!).message);
            Last=result.Payload;return(T)WireCodec.Deserialize(typeof(T),Last)!;
        }
        public Task<LocalEquipmentReply> Batch(LocalEquipmentRequest body)=>Request<LocalEquipmentReply>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,body);
        public void Dispose()=>socket.Dispose();
    }

    internal static void Prepare(LocalRuntime r)
    {
        var p=r.Store.GetOrCreate("local-player");var c=r.Catalog;UpgradeTowerTest.Initialize(p,c);
        p.ClearHardStage=179;p.CashPoint=900000;p.VipExp=0;p.WorldBossPoint=1000;
        foreach(uint deck in new uint[]{13,21,22,23,24})p.Decks[deck]=p.Deck.ToList();
        for(ulong i=1;i<=5;i++){p.CardStates[i].Enchant=10;p.CardStates[i].ActiveSkills=[99,99];p.CardStates[i].PassiveSkills=[99,99,99,99,99];}
        p.Raids.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now);p.Wander.Day=p.Raids.Day;r.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);if(r.Store.All().Count==0){Prepare(r);r.Store.GetOrCreate("local-player").ClearHardStage=18;r.Store.Save();}await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        var rc=new RaidCatalog(root);File.WriteAllText(Path.Combine(output,"proof.json"),JsonSerializer.Serialize(new{hp=rc.Private.Concat(rc.World).Concat(rc.Big.Where(x=>x.BossGrade>0)).Select(x=>new{id=x.Id,level=x.Level==0?99:x.Level,hp=rc.Hp(x,(uint)(x.Level==0?99:x.Level))})}));
        await Task.Delay(TimeSpan.FromMinutes(30));
    }
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        string save=Path.Combine(@"I:\BnsmMobileWork\verification","raid-protocol-"+Guid.NewGuid().ToString("N"));
        uint persistedHp=0;int receiptCount=0;
        await using(var r=new LocalRuntime(root,save,0,0,0,false)){
            Prepare(r);using var peer=new Peer(root);await peer.Login(r);var p=r.Store.Find(peer.Key.uid)!;var c=r.Catalog;var rc=new RaidCatalog(root);
            async Task<T> Send<T>(GameServerCommand cmd,object body,bool retry=false)=>await peer.Request<T>(cmd,body,true,retry);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.WorldBossPoint,p.Inventory,p.CardIds,p.EquipmentItems,p.Raids,p.Wander,p.ActiveStage});
            async Task Bad(GameServerCommand cmd,object body,string text){string before=State();await peer.Request<FailPacket>(cmd,body,false);Check(State()==before,text+" rejects without mutation");}
            var entry=await Send<GsCheckGameEntryOk>(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest());Check(entry.userCheckGameEntryData.RaidBossID.Count==17&&entry.userCheckGameEntryData.privateRaidBattleCount==3,"Recovered hard-adventure encounters and original daily private tickets");
            var world=await Send<GsGetWorldBossInfoOk>(GameServerCommand.GS_GET_WORLD_BOSS_INFO_REQUEST,new GsGetWorldBossInfoRequest());Check(world.worldBossBattleCount==2&&world.comboBossBattleCount==2&&world.worldBossId!=world.comboBossId,"Regular and combo daily bosses are distinct with original VIP limits");
            var list=await Send<GsGetBigBossListOk>(GameServerCommand.GS_GET_BIG_BOSS_LIST_REQUEST,new GsGetBigBossListRequest());Check(list.bigBossIDList.Count==18&&list.bigBossFullHPList.Count==18&&list.bigBossHPList.All(x=>x>0),"All original big bosses expose aligned IDs, HP and recall flags");
            await Bad(GameServerCommand.GS_RAID_BATTLE_START_REQUEST,new GsRaidBattleStartRequest{raidBossID=1},"Unknown private boss");
            uint privateId=entry.userCheckGameEntryData.RaidBossID[0];var start=new GsRaidBattleStartRequest{raidBossID=privateId,battleType=PrivateRaidBattleType.PRIAVET_RAID_BATTLE_ON_MAIN_MENU};
            var a=await Send<GsRaidBattleStartOk>(GameServerCommand.GS_RAID_BATTLE_START_REQUEST,start);string snapshot=State();await Send<GsRaidBattleStartOk>(GameServerCommand.GS_RAID_BATTLE_START_REQUEST,start,true);Check(State()==snapshot,"Private start retry consumes one entry only");
            await Bad(GameServerCommand.GS_RAID_BATTLE_START_REQUEST,start,"Overlapping private battle");
            var end=new GsRaidBattleEndRequest{raidBossID=privateId,battleType=start.battleType,timeStamp=a.timeStamp,damageAmount=rc.Hp(rc.Private.Single(x=>x.Id==privateId),14),battleEndSyncData=new()};
            end.timeStamp++;await Bad(GameServerCommand.GS_RAID_BATTLE_END_REQUEST,end,"Forged private timestamp");end.timeStamp--;
            var done=await Send<GsRaidBattleEndOk>(GameServerCommand.GS_RAID_BATTLE_END_REQUEST,end);Check(done.earnedCoinInfo.worldBossPoint==100&&p.WorldBossPoint==1100&&done.deltaItem.Count>0&&p.ActiveStage==null,"Private kill grants recovered essence and drop-group items");
            snapshot=State();await Send<GsRaidBattleEndOk>(GameServerCommand.GS_RAID_BATTLE_END_REQUEST,end,true);Check(State()==snapshot,"Private settlement retry replays exact drop result");
            await Bad(GameServerCommand.GS_RAID_BATTLE_END_REQUEST,end,"Already settled private battle");
            foreach(bool combo in new[]{false,true}){
                uint id=combo?world.comboBossId:world.worldBossId;var ws=new GsStartWorldBossBattleRequest{isCombo=combo,bossId=id};var started=await Send<GsStartWorldBossBattleOk>(GameServerCommand.GS_START_WORLD_BOSS_BATTLE_REQUEST,ws);
                var we=new GsEndWorldBossBattleRequest{isCombo=combo,bossId=id,timeStamp=started.timeStamp,damageAmount=(uint)rc.WorldRewards.Single(x=>x.PCLevel==99).RequireDamage*2,battleEndSyncData=new()};
                var result=await Send<GsEndWorldBossBattleOk>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,we);Check(result.earnedCoinInfo.worldBossPoint==50,"World boss points use original UI threshold formula, combo="+combo);

                snapshot=State();await Send<GsEndWorldBossBattleOk>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,we,true);Check(State()==snapshot,"World settlement retry cannot duplicate essence, combo="+combo);
                var ranking=await Send<GsGetWorldBossTopRankerOk>(GameServerCommand.GS_GET_WORLD_BOSS_TOP_RANKER_REQUEST,new GsGetWorldBossTopRankerRequest{bossId=id});Check(ranking.topRankerList.Count==1&&ranking.userRankData.cardInfo.Count==20&&ranking.topRankerList[0].cardInfo.Count==20&&ranking.userRankData.reward==0,"Local personal ranking safely supplies all 20 native detail slots, combo="+combo);
                var immortal=await Send<GsStartWorldBossBattleOk>(GameServerCommand.GS_START_WORLD_BOSS_BATTLE_REQUEST,ws);var rewardRule=rc.WorldRewards.Single(x=>x.PCLevel==99);uint capDamage=(uint)(rewardRule.RequireDamage*((rewardRule.BossPointLimit-rewardRule.BossPointDiff+rewardRule.BossPointRatio-1)/rewardRule.BossPointRatio));
                Check(capDamage>rc.Hp(rc.World.Single(x=>x.Id==id),99),"Original reward ceiling can require damage above immortal boss HP, combo="+combo);
                var capResult=await Send<GsEndWorldBossBattleOk>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,new GsEndWorldBossBattleRequest{bossId=id,isCombo=combo,timeStamp=immortal.timeStamp,damageAmount=capDamage,battleEndSyncData=new()});Check(capResult.earnedCoinInfo.worldBossPoint==rewardRule.BossPointLimit,"Immortal world boss cumulative damage reaches the exact original point limit, combo="+combo);

            }
            uint big=list.bigBossIDList[0],full=list.bigBossFullHPList[0];var bs=new GsBigBossBattleStartRequest{bossID=big};var bigStart=await Send<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,bs);
            var be=new GsBigBossBattleEndRequest{bossID=big,timeStamp=bigStart.timeStamp,damageAmount=full/5,battleEndSyncData=new()};var bigDone=await Send<GsBigBossBattleEndOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_END_REQUEST,be);
            Check(bigDone.bossHp==full-full/5&&bigDone.battleReward.Count==2&&bigDone.battleReward.Sum(x=>x.earnedItemList.Sum(i=>i.itemCount))==300,"Big boss partial HP and two crossed original reward thresholds");
            bigStart=await Send<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,bs);be.timeStamp=bigStart.timeStamp;be.damageAmount=full;bigDone=await Send<GsBigBossBattleEndOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_END_REQUEST,be);
            Check(bigDone.bossHp==0&&bigDone.battleReward.Count==8&&bigDone.battleReward[0].sequence==3,"Big boss follow-up pays only new thresholds");
            snapshot=State();await Send<GsBigBossBattleEndOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_END_REQUEST,be,true);Check(State()==snapshot,"Big boss retry preserves HP and reward inventory");
            await Bad(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,bs,"Defeated big boss");
            p.VipExp=(uint)c.Vip.Max(x=>x.LvUpNeedVipExp);uint cash=p.CashPoint;await Send<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,new GsBuyBonusCountRequest{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)10,buyCount=1});Check(p.CashPoint==cash-10,"Original world ticket price");
            await Bad(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,new GsBuyBonusCountRequest{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)8,buyCount=999},"Oversized ticket purchase");
            cash=p.CashPoint;var legacyTicket=await Send<GsBuyWorldBossCountOk>(GameServerCommand.GS_BUY_WORLD_BOSS_PLAY_COUNT_REQUEST,new GsBuyWorldBossCountRequest{isCombo=true,buyCount=1});Check(p.CashPoint==cash-10&&legacyTicket.buyWorldBossBattleCount==1,"Legacy world ticket packet alias and original price remain compatible");
            await Bad(GameServerCommand.GS_BUY_PRIVATE_RAID_PLAY_COUNT_REQUEST,new GsBuyPrivateRaidCountRequest(),"Original zero VIP private-raid purchase limit");
            await Bad(GameServerCommand.GS_BUY_BIG_BOSS_PLAY_COUNT_REQUEST,new GsBuyBigBossCountRequest{buyCount=1},"Original zero VIP big-boss purchase limit");
            var bossCount=await Send<GsGetBigBossCountOk>(GameServerCommand.GS_GET_BIG_BOSS_PLAY_COUNT_REQUEST,new GsGetBigBossCountRequest());Check(bossCount.bigBossBattleCount==1,"Big ticket UI count synchronizes spent and bought entries");
            // Wander loop covers every original node, all difficulties, every buff,
            // reward/bonus node, and exact native enemy-alive obfuscation.
            foreach(int difficulty in new[]{1,2,3}){
                p.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};
                await Send<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)difficulty});
                await Bad(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)(difficulty==1?2:1)},"Mid-run difficulty switch "+difficulty);
                if(difficulty==1){
                    await Bad(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=2},"Jump to a future wander battle");
                    var first=await Send<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1});
                    var loss=new GsWanderBattleEndConfirmRequest{spotId=1,startTimeStamp=first.startTimeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_FAIL,enemyAliveCount=(uint)(first.startTimeStamp%1000)+1,battleEndSyncData=new()};loss.cardInfoList.Add(new(){cardSeq=1,death=true,hpRatio=0,spRatio=10});
                    await Send<GsWanderBattleEndConfirmOk>(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,loss);Check(p.Wander.Spot==1&&p.Wander.Points==0&&p.Wander.Cards[1].Dead,"Wander loss retains spot and persists actual fallen card without reward");
                    await Bad(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1},"Fallen wander card cannot fight again before reset");p.Wander.Cards.Clear();
                }
                if(difficulty==1&&c.VipFor(p.VipExp).WanderBattleEnterBonusCount>0){
                    var paid=await Send<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,new GsBuyBonusCountRequest{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)12,buyCount=1});Check(paid.changedValue==1&&p.Wander.PaidLeft==1,"Purchased wander reset response reports paid tickets without duplicating the free reset");
                }
                foreach(var spot in rc.Wander[difficulty]){
                    if(spot.Type==BscTable.WanderSpotDataType.Type_Battle){
                        var st=await Send<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=(uint)spot.Id,buffId=p.Wander.Buff});
                        var en=new GsWanderBattleEndConfirmRequest{spotId=(uint)spot.Id,buffId=p.Wander.Buff,startTimeStamp=st.startTimeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(st.startTimeStamp%1000*(uint)spot.Id),battleEndSyncData=new()};
                        en.cardInfoList.Add(new(){cardSeq=1,hpRatio=.75f,spRatio=10});await Send<GsWanderBattleEndConfirmOk>(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,en);
                    }else if(spot.Type==BscTable.WanderSpotDataType.Type_Buff){
                        var bf=await Send<GsShuffleWanderBattleBuffOk>(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_BUFF_REQUEST,new GsShuffleWanderBattleBuffRequest{spotId=(uint)spot.Id});
                        if(difficulty==1&&spot.Id==5){uint oldGold=p.Gold,oldCash=p.CashPoint;var set=rc.BuffSets.Single(x=>x.Id==spot.BuffId);var fee=c.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_WANDER_BATTLE_BUFF_SHUFFLE,1,true);uint cost=(uint)((float)(fee.Value*set.BaseCost)/100f);bf=await Send<GsShuffleWanderBattleBuffOk>(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_BUFF_REQUEST,new GsShuffleWanderBattleBuffRequest{spotId=5});Check(p.Gold==oldGold-cost&&p.CashPoint==oldCash,"Buff reroll charges original gold formula and preserves diamonds");}
                        await Send<GsConfirmWanderBattleBuffOk>(GameServerCommand.GS_CONFIRM_WANDER_BATTLE_BUFF_REQUEST,new GsConfirmWanderBattleBuffRequest{spotId=(uint)spot.Id,buffId=bf.wanderBattleRecord.buffId});
                    }else {
                        var rw=new GsShuffleWanderBattleRewardRequest{spotId=(uint)spot.Id};await Send<GsShuffleWanderBattleRewardOk>(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_REWARD_REQUEST,rw);snapshot=State();await Send<GsShuffleWanderBattleRewardOk>(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_REWARD_REQUEST,rw,true);Check(State()==snapshot,"Reward node retry grants once "+difficulty+"/"+spot.Id);
                        if(difficulty==1&&spot.Id==3)await Bad(GameServerCommand.GS_CLEAR_WANDER_BATTLE_MAP_REQUEST,new GsClearWanderBattleMapRequest(),"Sweep with an unconfirmed reward cannot pay the same node again");
                        await Send<GsShuffleWanderBattleRewardOk>(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_REWARD_REQUEST,rw);
                        await Bad(GameServerCommand.GS_SHUFFLE_WANDER_BATTLE_REWARD_REQUEST,rw,"Third reward roll "+difficulty+"/"+spot.Id);
                        await Send<GsConfirmWanderBattleRewardOk>(GameServerCommand.GS_CONFIRM_WANDER_BATTLE_REWARD_REQUEST,new GsConfirmWanderBattleRewardRequest{spotId=(uint)spot.Id});
                    }
                }
                Check(p.Wander.Complete&&p.Wander.Points==rc.Wander[difficulty].Sum(x=>x.WanderPoint)&&p.Wander.BestSpots[difficulty-1]==rc.Wander[difficulty].Count,"Every original node completes and highest points persist, difficulty="+difficulty);
                var reward=rc.WanderRewards.First();await Send<GsGetWanderBattlePointRewardOk>(GameServerCommand.GS_GET_WANDER_BATTLE_POINT_REWARD_REQUEST,new GsGetWanderBattlePointRewardRequest{pointRewardId=(uint)reward.Id});
                await Bad(GameServerCommand.GS_GET_WANDER_BATTLE_POINT_REWARD_REQUEST,new GsGetWanderBattlePointRewardRequest{pointRewardId=(uint)reward.Id},"Already claimed point reward "+difficulty);
                await Send<GsResetWanderBattleRequestOk>(GameServerCommand.GS_RESET_WANDER_BATTLE_REQUEST,new GsResetWanderBattleRequest());
                Check(p.Wander.Difficulty==0&&p.Wander.Cards.Count==0&&p.Wander.BestSpots[difficulty-1]==rc.Wander[difficulty].Count&&p.Wander.Claimed.Count==1,"Reset clears run, retains personal best and once-only claims "+difficulty);
                await Send<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)difficulty});
                var sweep=await Send<GsClearWanderBattleMapOk>(GameServerCommand.GS_CLEAR_WANDER_BATTLE_MAP_REQUEST,new GsClearWanderBattleMapRequest());Check(p.Wander.Complete&&sweep.earnedItemList.Count>0,"Sweep previously completed journey pays normal node rewards "+difficulty);
            }
            p.ClearHardStage=17;p.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};
            await Send<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)1});
            await Bad(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1},"Locked adventure nodes remain blocked after map entry");p.ClearHardStage=179;
            p.Raids.Day--;p.Raids.Used[1]=999;p.Wander.Day--;p.Wander.Resets=1;p.Wander.Difficulty=1;p.Wander.Spot=5;
            await Send<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            Check(p.Raids.Used.Count==0&&p.Wander.Resets==0&&p.Wander.Spot==5&&p.Wander.Difficulty==1,"04:00 refresh restores opportunities without losing active wander progress");
            p.Raids.BossHp[big]=12345;persistedHp=12345;receiptCount=p.RaidReceipts.Count;r.Store.Save();
        }
        await using(var r=new LocalRuntime(root,save,0,0,0,false)){
            using var peer=new Peer(root);await peer.Login(r);var p=r.Store.Find(peer.Key.uid)!;Check(p.Raids.BossHp.Values.Contains(persistedHp)&&p.RaidReceipts.Count==receiptCount&&p.WorldBossPoint>1000&&p.Wander.Spot==5,"Restart retains currency, boss HP, receipts and wander progress");
            var record=await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());Check(record.wanderBattleRecord.spotId==5,"Fresh native-compatible login receives saved wander record");
        }
        File.WriteAllText(Path.Combine(root,"evidence/raid-wander-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("RAID_WANDER_PASS "+checks.Count);
    }
}
