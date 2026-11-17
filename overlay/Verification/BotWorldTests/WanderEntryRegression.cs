using System.Text.Json;

internal static class WanderEntryRegression
{
    internal static async Task Run(string root)
    {
        string fixture=Path.Combine("I:/BnsmModernWork/Verification","wander-entry-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);checks.Add(message);}
        await using(var runtime=new LocalRuntime(root,Path.Combine(fixture,"saves"),0,0,0,false)) {
            var player=runtime.Store.GetOrCreate("wander-entry-test");player.Level=99;player.ClearHardStage=0;player.Gold=12345;player.CashPoint=98765;
            foreach(uint deck in new uint[]{13,21,22,23,24})player.Decks[deck]=player.Deck.ToList();runtime.Store.Save();
            string possessions=JsonSerializer.Serialize(new{player.Gold,player.CashPoint,player.Inventory,player.CardIds,player.CardStates,player.ClearHardStage});
            using var peer=new Peer(root);await peer.Login(runtime,player.Account);
            foreach(int difficulty in new[]{1,2,3}) {
                player.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};player.ClearHardStage=0;
                var enter=await peer.Request<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)difficulty});
                Check(enter.wanderBattleRecord.continueFlag&&(int)enter.wanderBattleRecord.difficulty==difficulty&&enter.wanderBattleRecord.spotId==1,"Level-eligible player may view difficulty "+difficulty+" before hard-adventure progress");
                Check(JsonSerializer.Serialize(new{player.Gold,player.CashPoint,player.Inventory,player.CardIds,player.CardStates,player.ClearHardStage})==possessions&&player.Wander.Resets==0&&player.Wander.Points==0,"Viewing map "+difficulty+" neither changes possessions nor consumes a reset or unlocks progress");
                var resumed=await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
                Check(resumed.wanderBattleRecord.continueFlag&&(int)resumed.wanderBattleRecord.difficulty==difficulty,"A fresh native record can resume the viewed map "+difficulty);
                var blocked=await peer.Request<FailPacket>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1},success:false);
                Check(blocked.errorCode==(uint)ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE&&player.ActiveStage==null&&player.Wander.Spot==1,"Hard-adventure requirement still blocks locked battle nodes for "+difficulty);
                await peer.Request<FailPacket>(GameServerCommand.GS_CLEAR_WANDER_BATTLE_MAP_REQUEST,new GsClearWanderBattleMapRequest(),success:false);
                Check(player.Wander.Points==0&&player.Gold==12345,"Locked map "+difficulty+" cannot be swept for rewards");
                await peer.Request<FailPacket>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)(difficulty==1?2:1)},success:false);
                Check(player.Wander.Difficulty==difficulty,"Viewing another difficulty cannot silently replace an active journey "+difficulty);
            }
            var catalog=runtime.Catalog;
            foreach(int difficulty in new[]{1,2,3}) {
                player.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};
                int required=difficulty==1?catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_WANDER_BATTLE):(int)catalog.Numbers[difficulty==2?BscTable.NUMBER_TYPE.WANDER_OPEN_LEVEL_HARD:BscTable.NUMBER_TYPE.WANDER_OPEN_LEVEL_HERO];
                player.Level=(uint)Math.Max(1,required-1);
                var denied=await peer.Request<FailPacket>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)difficulty},success:false);
                Check(denied.errorCode==(uint)ServerErrorCode.ERROR_INVALID_USER_LEVEL&&player.Wander.Difficulty==0,"Difficulty "+difficulty+" still rejects a player below its original unlock level "+required);
            }
            player.Level=99;player.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};
            player.ClearHardStage=(uint)new RaidCatalog(root).Wander[1].First().OpenId;
            await peer.Request<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=BscTable.WANDER_BATTLE_DIFFICULTY.WANDER_BATTLE_DIFFICULTY_NORMAL});
            var start=await peer.Request<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1});
            Check(start.startTimeStamp!=0&&player.ActiveStage!=null,"Clearing the original required adventure stage still allows a real first-node battle");
            await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            Check(player.ActiveStage==null&&player.Wander.Spot==1,"Leaving the test battle retains journey progress without fabricating a victory");
            runtime.Store.Save();
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/wander-entry-regression.json",JsonSerializer.Serialize(new{passed=true,checks,fixture,realSaveChanged=false,phoneUiOperated=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("WANDER_ENTRY_REGRESSION_PASSED "+checks.Count);
    }
}
