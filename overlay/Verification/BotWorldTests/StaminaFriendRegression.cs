using System.Reflection;
using System.Text.Json;

internal static class StaminaFriendRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","stamina-friend-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        // Isolated clock-independent protocol fixture; production table bytes stay untouched.
        var missions=(MissionCatalog)typeof(ProtocolRouter).GetProperty("Missions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.Router)!;
        var meal=missions.Rows[33];meal.CheckStartTime=meal.CheckEndTime=meal.CheckOpenTime=meal.CheckCloseTime=0;
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        p.Stamina=runtime.Catalog.StaminaMax(p.Level)+25;
        uint before=p.Stamina;var reward=await peer.Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=33});
        Check(reward.rewardStamina>0&&p.Stamina==before+reward.rewardStamina&&reward.stamina==p.Stamina,"Timed meal claim grants exact stamina above recovery cap");
        before=p.Stamina;await peer.Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=33},retry:true);
        Check(p.Stamina==before,"Rapid retry replays its receipt without a second stamina grant");
        await peer.Request<FailPacket>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=33},success:false);
        Check(p.Stamina==before,"A different sequence cannot claim the daily meal twice");
        p.Missions.DailyClaimed.Remove(33);p.Stamina=uint.MaxValue;runtime.Store.Save();
        await peer.Request<FailPacket>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=33},success:false);
        Check(p.Stamina==uint.MaxValue&&!p.Missions.DailyClaimed.ContainsKey(33),"Real storage overflow cannot consume or partially grant a meal");p.Stamina=100;
        var bot=runtime.Router.Bots.All().First();
        var chat=(AmbientWorldChat)typeof(ProtocolRouter).GetField("worldChat",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.Router)!;
        var chatSettings=(AmbientWorldChat.Settings)typeof(AmbientWorldChat).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(chat)!;
        chatSettings.Enabled=false;
        await peer.Request<FailPacket>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=bot.Uid,friendlyBattle=true},success:false);
        Check(!p.Friends.Links.ContainsKey(bot.Uid),"A non-friend cannot use the friendly challenge route");
        await peer.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=bot.Uid});
        Check(p.Friends.Links.ContainsKey(bot.Uid),"The original add-friend request accepts a configured bot resident");
        var search=await peer.Request<GsFindFriendOk>(GameServerCommand.GS_FIND_FRIEND_REQUEST,
            new GsFindFriendRequest{findNick=bot.Uid.ToString()});
        Check(search.count==1&&search.findFriendList[0].uid==bot.Uid,"Exact UID finds the resident even with ambient chat disabled");
        var send=new GsHaveDrinkRequest{friendUid=bot.Uid};
        await peer.Request<GsHaveDrinkOk>(GameServerCommand.GS_HAVE_DRINK_REQUEST,send);
        await peer.Request<GsHaveDrinkOk>(GameServerCommand.GS_HAVE_DRINK_REQUEST,send,retry:true);
        var gift=await peer.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,
            new GsReceiveHaveDrinkRequest{friendUid=bot.Uid});
        Check(gift.addStamina>0&&p.Friends.RefundCount==1&&p.Friends.ReceivedToday.Contains(bot.Uid),
            "Resident gift and return gift stay available without ambient chat and retry counts once");
        foreach(string strength in new[]{"casual","legend"}) {
            runtime.Router.Bots.Configure(bot.Uid,strength,"balanced");bot=runtime.Router.Bots.Find(bot.Uid)!;
            string botBefore=JsonSerializer.Serialize(bot.Profile);uint gold=p.Gold,stamina=p.Stamina;
            var reply=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=bot.Uid,friendlyBattle=true});
            Check(reply.battleCardInfoList.Count==5&&reply.battleCardInfoList.All(x=>x.uid==bot.Uid),strength+" challenge uses the selected bot's five cards");
            foreach(var card in reply.battleCardInfoList) {
                var state=bot.Profile.CardStates[card.cardSeq];
                Check(card.cardId==bot.Profile.CardIds[(int)card.cardSeq-1]&&card.cardLevel==state.Level&&card.enchantGrade==state.Enchant,"Card identity, level and enchant match the saved bot loadout");
                Check(card.cardSkillLevel.SequenceEqual(state.ActiveSkills.Concat(state.PassiveSkills))&&card.cardEquipInfo.Count==6,"Seven skill levels and all equipment/gem slots reach the battle protocol");
                foreach(ulong gear in new[]{state.WeaponSequence}.Concat(state.AccessorySequences)) {
                    var item=bot.Profile.EquipmentItems.Single(x=>x.Sequence==gear);
                    Check(card.cardEquipInfo.Any(x=>x.itemID==item.ItemId&&x.empowerEffect.SequenceEqual(item.EmpowerValues)),"Real equipment and refinement values are preserved");
                }
            }
            Check(JsonSerializer.Serialize(bot.Profile)==botBefore&&p.Gold==gold&&p.Stamina==stamina,"Practice preview cannot change opponent equipment or charge the player");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/stamina-friend-test.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("STAMINA_FRIEND_PASSED "+checks.Count);
    }
}
