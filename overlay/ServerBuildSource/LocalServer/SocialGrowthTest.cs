using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;

internal static class SocialGrowthTest
{
    internal static async Task Run(string root)
    {
        string production=Path.Combine(root,"LocalServer/saves/players.json");
        byte[]? original=File.Exists(production) ? SHA256.HashData(File.ReadAllBytes(production)) : null;
        string save=Path.Combine(root,"verification/social-growth",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Assert(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        T Read<T>(byte[] data)=>(T)WireCodec.Deserialize(typeof(T),data)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);
        async Task<(ushort Command,byte[] Payload)> Send(TcpClient socket,ushort command,object message) {
            await socket.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(message),true));
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var frame=(await WireCodec.ReadFrame(socket.GetStream(),deadline.Token))!;
            Assert(frame.Length<=8192,"Original frame size respected for "+(GameServerCommand)command);
            var result=codec.Decode(frame,true);return (result.Command,result.Payload);
        }
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="social-growth-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var account=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true})).Payload);
        var key=new SessionKeyInfo {uid=account.uid,loginSessionKey=account.loginSessionKey};
        using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);
        async Task<byte[]> Request(GameServerCommand command,object message,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;
            var response=await Send(game,(ushort)command,message);
            string expected=command.ToString().Replace("_REQUEST",success ? "_OK":"_FAIL");
            if(((GameServerCommand)response.Command).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(response.Payload).message);
            return response.Payload;
        }
        var p=server.Store.Find(account.uid)!;var c=server.Catalog;
        async Task<GsGetUserGameInfoOk> Info()=>Read<GsGetUserGameInfoOk>(await Request(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest {keyInfo=key}));
        async Task<List<PKChatMessage>> Chat(uint last=0,uint before=0)=>Read<GsFetchPublicChatMessageOk>(await Request(GameServerCommand.GS_FETCH_PUBLIC_CHAT_MESSAGE_REQUEST,new GsFetchPublicChatMessageRequest {keyInfo=key,lastMsgSeq=last,beforeMsgSeq=before})).msgList;
        var initial=await Info();
        var entry=Read<GsCheckGameEntryOk>(await Request(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest {keyInfo=key}));
        Assert(entry.userCheckGameEntryData?.attendanceInfo!=null,"Entry refresh includes the nested attendance object required by the original startup callback");
        Assert(initial.namecardInfo.commonInfo.nick==p.Nickname && initial.skillPoint==c.SkillPointMax(p) && initial.skillPointStamp>0,"Profile and original skill capacity/timestamp are present on login");
        var first=await Chat();Assert(first.Count>0 && first.All(x=>x.senderUid>=900000 && x.senderCommonInfo.nick.Length>0 && c.UsableHeroes.Any(a=>a.Id==x.leaderCardInfo.cardID)),"World channel contains local actors with valid original avatars");
        Assert(first.Select(x=>x.chatSeq).SequenceEqual(first.Select(x=>x.chatSeq).OrderDescending()),"World messages arrive newest first as expected by the APK");
        uint last=first[0].chatSeq;Assert((await Chat(last)).Count==0,"Unchanged incremental poll does not repeat old messages");
        var chat=new GsSendPublicChatMessageRequest {keyInfo=key,msg="你好，有人一起聊聊吗",lastMsgSeq=last};
        var posted=Read<GsSendPublicChatMessageOk>(await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat));
        Assert(posted.msgList.Count==1 && posted.msgList[0].senderUid==p.Uid && posted.msgList[0].senderCommonInfo.nick==p.Nickname,"Player's own message appears under the correct profile and UID");
        var repeated=Read<GsSendPublicChatMessageOk>(await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat,retry:true));
        Assert(repeated.msgList.Count==1 && repeated.msgList[0].chatSeq==posted.msgList[0].chatSeq,"Resending the same chat request cannot append a duplicate");
        chat.msg="same sequence different text";await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat,false,true);
        chat.msg="[ff0000]markup";await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat,false);
        chat.msg=new string('a',101);await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat,false);
        chat.msg=" ";await Request(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,chat,false);
        Assert((await Chat(last)).Count==1,"Invalid chat requests leave history intact");
        Assert((await Chat(0,posted.msgList[0].chatSeq)).All(x=>x.chatSeq<posted.msgList[0].chatSeq),"History pagination respects the before-sequence boundary");
        var restoredChat=new AmbientWorldChat(root,save,c);Assert(restoredChat.Fetch(last,0).Single().message=="你好，有人一起聊聊吗","World history survives restart");
        ulong clock=2000000000;var ambient=new AmbientWorldChat(root,Path.Combine(save,"clock-test"),c,()=>clock);
        uint cursor=ambient.Fetch(0,0).Max(x=>x.chatSeq);clock+=24*3600;
        Assert(ambient.Fetch(cursor,0).Count==1,"Returning after a long absence emits one ambient line, not an offline backlog");
        cursor=ambient.Fetch(0,0).Max(x=>x.chatSeq);Assert(ambient.Fetch(cursor,0).Count==0,"Polling repeatedly cannot accelerate NPC speech");
        clock+=15;Assert(ambient.Fetch(cursor,0).Count<=1,"Conversation replies are bounded and delayed");
        for(int i=0;i<215;i++) {clock+=4;ambient.Send("fixture-"+i,"test "+i,new PKUserNameCardInfo {uid=1001,commonInfo=new PKUserCommonInfo {nick="test",level=20}},new PKUserCardInfo {cardID=20101,cardLevel=1});}
        using(var history=JsonDocument.Parse(File.ReadAllText(Path.Combine(save,"clock-test/world-chat.json"))))
            Assert(history.RootElement.GetProperty("Messages").GetArrayLength()==200,"World history retention is bounded to 200 messages");
        Assert(ambient.Fetch(0,0).Count==10,"Chat replies are limited to ten messages per original protocol frame");

        // All fixture balances and materials below belong to this independent test profile.
        p.Level=20;p.Exp=(uint)c.PlayerStates.Single(x=>x.Level==20).Exp;p.Gold=500000;p.CashPoint=10000;
        p.CardIds[0]=20103;p.CardStates[1]=new() {Level=10,Exp=(uint)c.CardLevels.Single(x=>x.Level==10).Rank1};
        p.SkillPoint=10;p.SkillPointStamp=ProtocolRouter.Now;server.Store.Save();
        var skill=new GsCardSkillLevelUpRequest {keyInfo=key,targetCardSeq=1};skill.levelUpInfo.Add(new() {skillType=CardSkillType.CARD_SKILL_ACTIVE_ONE,levelupValue=2});
        var upgraded=Read<GsCardSkillLevelUpOk>(await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill));
        Assert(upgraded.cardInfo.activeSkillLevel[0]==3 && upgraded.currentCoinInfo.gold==498990 && upgraded.remainSkillPoint==8,"Skill upgrade consumes original 500+510 gold and two skill points");
        Assert(upgraded.cardInfo.nonActiveSkillLevel.Count==5 && upgraded.cardInfo.activeSkillLevel.Count==2,"Original active/passive slot counts remain consistent through card replies");
        await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,retry:true);
        Assert(p.Gold==498990 && p.SkillPoint==8 && p.CardStates[1].ActiveSkills[0]==3,"Skill request retransmission cannot charge or upgrade twice");
        skill.levelUpInfo[0].levelupValue=1;await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false,true);
        Assert(p.CardStates[1].ActiveSkills[0]==3,"Reused sequence with changed skill content is rejected");
        skill.levelUpInfo.Clear();skill.levelUpInfo.Add(new() {skillType=CardSkillType.CARD_SKILL_NON_ACTIVE_ONE,levelupValue=1});
        var passive=Read<GsCardSkillLevelUpOk>(await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill));
        Assert(passive.cardInfo.nonActiveSkillLevel[0]==2 && passive.currentCoinInfo.gold==498740,"Passive skill uses its own original cost table and packet slot");
        uint gold=p.Gold,points=p.SkillPoint!.Value;
        skill.levelUpInfo.Add(new() {skillType=CardSkillType.CARD_SKILL_ACTIVE_TWO,levelupValue=1});await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        Assert(p.Gold==gold && p.SkillPoint==points && p.CardStates[1].PassiveSkills[0]==2,"A locked skill rejects the whole batch without partially charging valid skills");
        skill.levelUpInfo.Clear();skill.levelUpInfo.Add(new() {skillType=CardSkillType.CARD_SKILL_ACTIVE_ONE,levelupValue=99});await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        skill.levelUpInfo[0].levelupValue=1;skill.levelUpInfo.Add(new() {skillType=CardSkillType.CARD_SKILL_ACTIVE_ONE,levelupValue=1});await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        Assert(p.Gold==gold && p.SkillPoint==points,"Over-limit and duplicate skill slots cannot consume resources");
        skill.levelUpInfo.RemoveAt(1);p.SkillPoint=0;p.SkillPointStamp=ProtocolRouter.Now;await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        p.SkillPoint=5;p.Gold=0;await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        Assert(p.CardStates[1].ActiveSkills[0]==3,"Insufficient points and gold cannot change skill levels");
        p.Gold=500000;p.SkillPoint=1;p.SkillPointStamp=ProtocolRouter.Now-601;var regen=await Info();
        Assert(regen.skillPoint==3 && regen.skillPointStamp>=ProtocolRouter.Now-2,"Skill points recover once per original 300 seconds and retain the partial interval");
        p.SkillPoint=9;p.SkillPointStamp=ProtocolRouter.Now-3600;Assert((await Info()).skillPoint==c.SkillPointMax(p),"Natural recovery stops at the original player/VIP cap");
        p.VipExp=0;p.SkillPoint=1;p.SkillPointStamp=ProtocolRouter.Now;await Request(GameServerCommand.GS_USER_SKILL_POINT_CHARGE_REQUEST,new GsUserSkillPointChargeRequest {keyInfo=key},false);
        Assert(p.CashPoint==10000,"VIP-zero refill is refused without charging diamonds");
        p.VipExp=(uint)c.Vip.First(v=>v.VipLv==1).LvUpNeedVipExp;p.SkillPointChargeCount=0;
        var refill=Read<GsUserSkillPointChargeOk>(await Request(GameServerCommand.GS_USER_SKILL_POINT_CHARGE_REQUEST,new GsUserSkillPointChargeRequest {keyInfo=key}));
        uint cash=p.CashPoint;Assert(refill.nowSkillPoint==c.SkillPointMax(p) && refill.exchangeToSkillPointCount==1 && cash<10000,"VIP refill consumes the original diamond price and updates the daily counter");
        await Request(GameServerCommand.GS_USER_SKILL_POINT_CHARGE_REQUEST,new GsUserSkillPointChargeRequest {keyInfo=key},retry:true);
        Assert(p.CashPoint==cash && p.SkillPointChargeCount==1,"Refill retransmission cannot deduct diamonds twice");
        p.SkillPoint=1;await Request(GameServerCommand.GS_USER_SKILL_POINT_CHARGE_REQUEST,new GsUserSkillPointChargeRequest {keyInfo=key},false);
        Assert(p.CashPoint==cash,"The daily VIP refill limit is enforced");
        var potion=c.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_SKILL_POINT && x.RecoveryValue>0);
        p.Inventory[(uint)potion.Id]=2;p.SkillPoint=0;p.SkillPointStamp=ProtocolRouter.Now;
        var recover=new GsUseRecoveryItemRequest {keyInfo=key,itemUseType=UseRecoveryItemType.RECOVERY_SKILL_POINT,itemID=(uint)potion.Id,itemCount=1};
        var recovered=Read<GsUseRecoveryItemOk>(await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST,recover));
        Assert(recovered.skillPoint>0 && recovered.skillPoint<=c.SkillPointMax(p) && p.Inventory[(uint)potion.Id]==1,"Original skill recovery potion consumes an owned item and returns the updated skill pool");
        await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST,recover,retry:true);
        Assert(p.Inventory[(uint)potion.Id]==1,"Skill recovery potion retransmission cannot consume a second item");

        var card=c.Cards.First(x=>x.Id==p.CardIds[0]);var mats=c.CardEnchants.First(x=>x.EnchantType==card.EnchantType).MaterialDataList[0];
        p.Inventory.Clear();p.CardStates[1].Enchant=0;p.Gold=500000;
        await Request(GameServerCommand.GS_ENCHANT_CARD_REQUEST,new GsCardEnchantRequest {keyInfo=key,targetCardSeq=1},false);
        Assert(p.Gold==500000 && p.CardStates[1].Enchant==0,"Missing enchant materials reject the request atomically");
        for(int i=0;i<mats.ItemId.Count;i++)p.Inventory[(uint)mats.ItemId[i]]=mats.Count[i];
        var enchant=Read<GsCardEnchantOk>(await Request(GameServerCommand.GS_ENCHANT_CARD_REQUEST,new GsCardEnchantRequest {keyInfo=key,targetCardSeq=1}));
        Assert(enchant.enchantCardInfo.enchantGrade==1 && enchant.currentCoinInfo.gold==498400 && enchant.itemList.All(x=>x.itemCount<0),"Enchant consumes the original materials/1600 gold and persists grade one");
        await Request(GameServerCommand.GS_ENCHANT_CARD_REQUEST,new GsCardEnchantRequest {keyInfo=key,targetCardSeq=1},retry:true);
        Assert(p.Gold==498400 && p.CardStates[1].Enchant==1,"Enchant retransmission cannot advance the grade twice");
        var secondMats=c.CardEnchants.First(x=>x.EnchantType==card.EnchantType).MaterialDataList[1];
        p.Inventory.Clear();gold=p.Gold;
        for(int i=0;i<secondMats.ItemId.Count;i++) {
            int needed=secondMats.Count[i];var recipe=c.Recipes.FirstOrDefault(r=>r.TargetItemId==secondMats.ItemId[i] && r.TargetItemLevel<=1 && r.TargetItemQuantity>0 && r.MeterialItemId.Count>0 && r.MeterialItemQuantity.Count==r.MeterialItemId.Count && (r.Cost==0 || r.CostType==BscTable.MONEY_TYPE.MONEY_TYPE_GOLD));
            if(recipe==null)p.Inventory[(uint)secondMats.ItemId[i]]=needed;
            else {
                int times=(needed+recipe.TargetItemQuantity-1)/recipe.TargetItemQuantity;
                for(int m=0;m<recipe.MeterialItemId.Count;m++)p.Inventory[(uint)recipe.MeterialItemId[m]]=p.Inventory.GetValueOrDefault((uint)recipe.MeterialItemId[m])+times*recipe.MeterialItemQuantity[m];
            }
        }
        var crafted=Read<GsCardEnchantOk>(await Request(GameServerCommand.GS_ENCHANT_CARD_REQUEST,new GsCardEnchantRequest {keyInfo=key,targetCardSeq=1}));
        Assert(crafted.enchantCardInfo.enchantGrade==2 && p.Gold<gold && crafted.itemList.All(x=>c.Items.Any(i=>i.Id==x.itemID)),"Upgrade accepts owned raw materials through original recipes and returns actual inventory deltas");
        p.CardStates[1].Enchant=1; // Reset only this independent fixture for the following evolution preservation check.
        p.Inventory.Clear();gold=p.Gold;uint oldId=p.CardIds[0];
        await Request(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,new GsCardEvolutionRequest {keyInfo=key,targetCardSeq=1},false);
        Assert(p.Gold==gold && p.CardIds[0]==oldId,"Missing evolution fragments do not change card identity or balance");
        int fragments=c.CardRanks.Single(x=>x.Rank==card.Rank).NextEVP;
        p.Inventory[(uint)card.PieceId]=fragments-2;p.Inventory[220001004]=2;
        for(int i=0;i<card.RankUpMaterialItemId.Count;i++)p.Inventory[(uint)card.RankUpMaterialItemId[i]]=card.RankUpMaterialItemCount[i];
        var evolution=Read<GsCardEvolutionOk>(await Request(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,new GsCardEvolutionRequest {keyInfo=key,targetCardSeq=1}));
        Assert(evolution.evolutionCardInfo.cardID==card.RankUpCardId && evolution.evolutionCardInfo.cardSeq==1 && evolution.evolutionCardInfo.activeSkillLevel[0]==3 && evolution.evolutionCardInfo.enchantGrade==1,"Evolution follows the original next-card ID while preserving sequence, level, skills and enchant");
        Assert(p.Inventory.GetValueOrDefault((uint)card.PieceId)==0 && p.Inventory.GetValueOrDefault(220001004u)==0 && p.CollectedCardIds.Contains(oldId) && p.CollectedCardIds.Contains(p.CardIds[0]),"Evolution consumes own fragments before almighty fragments and retains both collection records");
        uint evolved=p.CardIds[0];gold=p.Gold;
        await Request(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,new GsCardEvolutionRequest {keyInfo=key,targetCardSeq=1},retry:true);
        Assert(p.CardIds[0]==evolved && p.Gold==gold,"Evolution retransmission cannot cause a second star upgrade");
        var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Assert(restored.CardStates[1].ActiveSkills[0]==3 && restored.CardStates[1].PassiveSkills[0]==2 && restored.CardStates[1].Enchant==1 && restored.CardIds[0]==evolved && restored.SkillPointChargeCount==1,"Card skills, enchant, evolution and daily skill counter survive save reload");
        Assert(restored.GrowthReceipts.Count>0,"Successful resource-changing operations retain durable retry receipts");
        p.Level=2;skill.levelUpInfo[0].levelupValue=1;await Request(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);
        Assert(p.CardStates[1].ActiveSkills[0]==3,"Skill upgrades enforce the original player-level unlock");
        if(original!=null)Assert(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(production))),"Production save stays untouched by the complete verification");
        File.WriteAllText(Path.Combine(root,"evidence/social-growth-test.json"),JsonSerializer.Serialize(new {completedAt=DateTimeOffset.Now,passed=checks.Count,checks,saveDirectory=save,scope="Encrypted original-protocol TCP tests plus deterministic ambient chat checks; isolated saves only."},new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"PASS: {checks.Count} social and growth checks.");
    }
}
