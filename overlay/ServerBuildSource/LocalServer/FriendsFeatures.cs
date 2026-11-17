using System.Security.Cryptography;
using System.Text;

internal sealed partial class ProtocolRouter
{
    private uint FriendLimit(LocalPlayer p)=>(uint)catalog.PlayerStates.Single(x=>x.Level==p.Level).MaxFriend;
    private uint FriendSendLeft(LocalPlayer p)=>(uint)Math.Max(0,catalog.VipFor(p.VipExp).MaxSendMeat-p.Friends.SentToday.Count);
    // Local receive rule: one gift per contact, at most MaxFriend gifts per day.
    private uint FriendReceiveLeft(LocalPlayer p)=>(uint)Math.Max(0,(int)FriendLimit(p)-p.Friends.ReceivedToday.Count);
    private uint FriendNumber(BscTable.NUMBER_TYPE key)=>(uint)Math.Max(0,catalog.Numbers[key]);
    private AmbientWorldChat.Actor? FriendActor(uint uid)=>worldChat.LocalActors.FirstOrDefault(x=>x.Uid==uid);
    private bool IsFriendCompanion(uint uid)=>Bots.Find(uid)!=null||FriendActor(uid)!=null;
    private PKThumbnailUserData? FriendProfile(uint uid)
    {
        var player=store.Find(uid)??Bots.Find(uid)?.Profile;if(player!=null)return GuildThumbnail(player);
        var a=FriendActor(uid);
        return a==null?null:new(){uid=a.Uid,nick=a.Name+"·本地",level=a.Level,account="local-npc-"+a.Uid,avatarCardId=a.CardId,logoutTime=Now,leaderCardInfo=new(){cardID=a.CardId,cardLevel=Math.Min(a.Level,99),cardSeq=1}};
    }
    private IEnumerable<PKThumbnailUserData> FriendProfiles()=>store.All().Select(GuildThumbnail).Concat(Bots.All().Select(x=>GuildThumbnail(x.Profile)));
    private void RefreshFriends(LocalPlayer p)
    {
        var f=p.Friends;ulong day=(Now+8*3600)/86400;
        if(f.Day!=day){f.Day=day;f.SentToday.Clear();f.ReceivedToday.Clear();}
        foreach(var pair in f.Links)if(IsFriendCompanion(pair.Key)&&!f.ReceivedToday.Contains(pair.Key)&&pair.Value.GiftDay!=day){pair.Value.GiftDay=day;pair.Value.ReceivedAt=Now;}
    }
    private PKFriendData FriendData(LocalPlayer p,uint uid)
    {
        var t=FriendProfile(uid)!;var f=p.Friends;var link=f.Links.GetValueOrDefault(uid);
        return new(){uid=uid,nick=t.nick,level=t.level,account=t.account,vipGrade=t.vipGrade,titleId=t.titleId,avatarCardId=t.avatarCardId,leaderCardInfo=t.leaderCardInfo,logoutTime=t.logoutTime,registTime=link?.Joined??Now,
            haveDrinkSend=link!=null&&!f.SentToday.Contains(uid)?1u:0u,haveDrinkReceive=link?.GiftDay==f.Day&&!f.ReceivedToday.Contains(uid)?1u:0u,haveDrinkSendTime=link?.SentAt??0,haveDrinkReceiveTime=link?.ReceivedAt??0};
    }
    private void MakeFriends(LocalPlayer p,uint uid)
    {
        p.Friends.Links[uid]=new(){Joined=Now};p.Friends.Incoming.Remove(uid);p.Friends.Outgoing.Remove(uid);
        var q=store.Find(uid);if(q!=null){q.Friends.Links[p.Uid]=new(){Joined=Now};q.Friends.Incoming.Remove(p.Uid);q.Friends.Outgoing.Remove(p.Uid);}RefreshFriends(p);
    }
    private Reply? HandleFriends(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(request is GsGetArenaBattleEnemyDeckInfoRequest duel&&duel.friendlyBattle) {
            if(duel.targetUid==p.Uid||!p.Friends.Links.ContainsKey(duel.targetUid))return fail("friendly duel requires a current friend",ServerErrorCode.ERROR_NOT_FRIEND);
            var opponent=store.Find(duel.targetUid)??Bots.Find(duel.targetUid)?.Profile;
            if(opponent==null)return fail("friend has no battle loadout",ServerErrorCode.ERROR_INVALID_ENEMY_DECK_CARD_INFO);
            var slots=DeckSlots(opponent,(uint)BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_ARENA).Take(5).ToArray();
            if(!CompetitiveRoster(opponent,slots,5,out _))slots=opponent.Deck.Take(5).ToArray();
            if(!CompetitiveRoster(opponent,slots,5,out _))
                return fail("friend has no valid battle cards",ServerErrorCode.ERROR_INVALID_ENEMY_DECK_CARD_INFO);
            var result=new GsGetArenaBattleEnemyDeckInfoOk{formationInfo=new(){id=1,level=1}};
            for(int i=0;i<slots.Length;i++)if(slots[i]!=0)result.battleCardInfoList.Add(RankingCard(opponent,slots[i],(uint)i));
            var formation=opponent.FormationLoadouts.GetValueOrDefault(BscTable.GAME_MODE.MODE_FRIEND_PVP)?.Defense.FirstOrDefault(x=>x>0)??0;
            if(formation>0&&opponent.FormationLevels.TryGetValue(formation,out var level))result.formationInfo=new(){id=formation,level=level};
            return ok(result);
        }
        if(name is not ("GS_RECOMMEND_FRIEND_REQUEST" or "GS_FIND_FRIEND_REQUEST" or "GS_GET_FRIEND_LIST_REQUEST" or "GS_GET_INVITE_FRIEND_LIST_REQUEST" or "GS_INVITE_FRIEND_REQUEST" or "GS_ACCEPT_FRIEND_REQUEST" or "GS_DENY_FRIEND_REQUEST" or "GS_DELETE_FRIEND_REQUEST" or "GS_HAVE_DRINK_REQUEST" or "GS_RECEIVE_HAVE_DRINK_REQUEST" or "GS_GET_ANOTHER_USER_GAME_INFO_REQUEST" or "GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST" or "GS_FETCH_PRIVATE_CHAT_MESSAGE_REQUEST" or "GS_GET_PLATFORM_FRIEND_LIST_REQUEST" or "GS_LBS_FRIEND_REQUEST"))return null;
        RefreshFriends(p);
        bool read=name is "GS_RECOMMEND_FRIEND_REQUEST" or "GS_FIND_FRIEND_REQUEST" or "GS_GET_FRIEND_LIST_REQUEST" or "GS_GET_INVITE_FRIEND_LIST_REQUEST" or "GS_GET_ANOTHER_USER_GAME_INFO_REQUEST" or "GS_GET_PLATFORM_FRIEND_LIST_REQUEST" or "GS_LBS_FRIEND_REQUEST";
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}"))),fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(!read&&p.Friends.Receipts.TryGetValue(key,out var previous))return previous.Fingerprint==fingerprint?new(previous.Command,previous.TypeName==""?null:WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(previous.TypeName,true),Convert.FromBase64String(previous.Payload)),previous.Name):fail("friend sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var reply=FriendOperation(name,request,p,ok,fail);
        if(reply.Name.EndsWith("_OK")){
            if(!read){if(p.Friends.Receipts.Count>=256)p.Friends.Receipts.Remove(p.Friends.Receipts.Keys.First());p.Friends.Receipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message?.GetType().FullName??"",Payload=reply.Message==null?"":Convert.ToBase64String(WireCodec.Serialize(reply.Message))};}store.Save();
        }return reply;
    }
    private Reply FriendOperation(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_FRIEND_STATE)=>fail(why,code);
        var f=p.Friends;
        if(name=="GS_RECOMMEND_FRIEND_REQUEST"){
            var r=new GsRecommendFriendOk();r.recommendList.AddRange(FriendProfiles().Where(x=>x.uid!=p.Uid&&!f.Links.ContainsKey(x.uid)&&!f.Outgoing.Contains(x.uid)&&!f.Incoming.Contains(x.uid)).OrderByDescending(x=>x.logoutTime).ThenBy(x=>x.uid).Take(20));r.count=(uint)r.recommendList.Count;return ok(r);
        }
        if(request is GsFindFriendRequest search){
            string text=(search.findNick??"").Trim();if(text.Length is <1 or >32||text.Any(char.IsControl))return Bad("invalid search",ServerErrorCode.ERROR_INVALID_ID);
            var r=new GsFindFriendOk();
            bool numeric=uint.TryParse(text,out uint uid);
            var matches=FriendProfiles().Where(x=>x.uid!=p.Uid&&
                    (numeric&&x.uid==uid||x.nick.Contains(text,StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x=>numeric&&x.uid==uid)
                .ThenByDescending(x=>x.nick.Equals(text,StringComparison.OrdinalIgnoreCase)||
                    x.nick.Equals(text+"·人机",StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x=>x.logoutTime).ThenBy(x=>x.uid);
            r.findFriendList.AddRange(matches.Take(20));r.count=(uint)r.findFriendList.Count;return ok(r);
        }
        if(request is GsGetFriendListRequest get){
            if(get.targetUid!=0&&get.targetUid!=p.Uid)return Bad("cannot read another account's private friend list",ServerErrorCode.ERROR_INVALID_ID);
            if(!Enum.IsDefined(get.friendStatus))return Bad("invalid friend status");
            IEnumerable<uint> ids=get.friendStatus switch{FriendStatus.FRIEND_STATUS_FRIEND_DONE or FriendStatus.FRIEND_STATUS_CHANGE_INFO=>f.Links.Keys,FriendStatus.FRIEND_STATUS_BE_INVITED=>f.Incoming,FriendStatus.FRIEND_STATUS_INVITING=>f.Outgoing,FriendStatus.FRIEND_STATUS_ALL=>f.Links.Keys.Concat(f.Incoming).Concat(f.Outgoing).Distinct(),_=>[]};
            var r=new GsGetFriendListOk{remainHaveDrinkCount=FriendSendLeft(p),remainReceiveDrinkCount=FriendReceiveLeft(p),refundDrinkCount=f.RefundCount};r.friendList.AddRange(ids.Where(x=>FriendProfile(x)!=null).Order().Select(x=>FriendData(p,x)));r.currentCount=r.totalCount=(uint)r.friendList.Count;return ok(r);
        }
        if(name=="GS_GET_INVITE_FRIEND_LIST_REQUEST"){
            var r=new GsGetInviteFriendListOk();r.inviteFriendList.AddRange(f.Outgoing.Where(x=>FriendProfile(x)!=null).Order());r.currentCount=r.totalCount=(uint)r.inviteFriendList.Count;return ok(r);
        }
        if(name=="GS_GET_PLATFORM_FRIEND_LIST_REQUEST")return ok(new GsGetPlatformFriendListOk{remainHaveDrinkCount=FriendSendLeft(p),remainReceiveDrinkCount=FriendReceiveLeft(p),refundDrinkCount=f.RefundCount});
        if(name=="GS_LBS_FRIEND_REQUEST")return ok(new GsLBSFriendListOk());
        if(request is GsInviteFriendRequest invite){
            uint id=invite.inviteUid;var q=store.Find(id);
            if(id==p.Uid||FriendProfile(id)==null)return Bad("friend not found",ServerErrorCode.ERROR_INVALID_FRIEND_INFO);
            if(f.Links.ContainsKey(id))return Bad("already friends",ServerErrorCode.ERROR_EXIST_FRIEND);
            if(f.Outgoing.Contains(id))return Bad("already requested",ServerErrorCode.ERROR_ALREADY_INVITING_FRIEND_STATUS);
            if(f.Links.Count>=FriendLimit(p))return Bad("own friend list full",ServerErrorCode.ERROR_OVER_MAX_FRIEND_COUNT_MINE);
            if(q!=null&&q.Friends.Links.Count>=FriendLimit(q))return Bad("target friend list full",ServerErrorCode.ERROR_OVER_MAX_FRIEND_COUNT_FRIEND);
            if(f.Outgoing.Count>=FriendLimit(p)||q!=null&&q.Friends.Incoming.Count>=FriendLimit(q))return Bad("too many pending requests",ServerErrorCode.ERROR_OVER_MAX_INVITING_FRIEND_COUNT);
            if(f.Incoming.Contains(id))return Bad("accept the incoming request",ServerErrorCode.ERROR_ALREADY_INVITED_FRIEND_STATUS);
            if(q==null)MakeFriends(p,id);else{f.Outgoing.Add(id);q.Friends.Incoming.Add(p.Uid);}return ok(null);
        }
        if(request is GsAcceptFriendRequest accept){
            uint id=accept.acceptUid;var q=store.Find(id);
            if(!f.Incoming.Contains(id)||q==null||!q.Friends.Outgoing.Contains(p.Uid))return Bad("request no longer exists");
            if(f.Links.Count>=FriendLimit(p))return Bad("own friend list full",ServerErrorCode.ERROR_OVER_MAX_FRIEND_COUNT_MINE);
            if(q.Friends.Links.Count>=FriendLimit(q))return Bad("target friend list full",ServerErrorCode.ERROR_OVER_MAX_FRIEND_COUNT_FRIEND);
            MakeFriends(p,id);return ok(new GsAcceptFriendOk{friendData=FriendData(p,id),rewardItem=new()});
        }
        if(request is GsDenyFriendRequest deny){if(!f.Incoming.Remove(deny.denyUid))return Bad("request no longer exists");store.Find(deny.denyUid)?.Friends.Outgoing.Remove(p.Uid);return ok(null);}
        if(request is GsDeleteFriendRequest delete){uint id=delete.deleteUid;if(!f.Links.Remove(id))return Bad("not friends",ServerErrorCode.ERROR_NOT_FRIEND);store.Find(id)?.Friends.Links.Remove(p.Uid);return ok(null);}
        if(request is GsHaveDrinkRequest send){
            uint id=send.friendUid;var q=store.Find(id);
            if(!f.Links.TryGetValue(id,out var link))return Bad("not friends",ServerErrorCode.ERROR_NOT_FRIEND);
            if(q==null&&!IsFriendCompanion(id)||id==p.Uid)return Bad("invalid recipient");
            if(FriendSendLeft(p)==0||f.SentToday.Contains(id))return Bad("daily gift limit");
            if(q!=null){RefreshFriends(q);if(!q.Friends.Links.TryGetValue(p.Uid,out var back))return Bad("friendship no longer exists");back.GiftDay=f.Day;back.ReceivedAt=Now;}
            f.SentToday.Add(id);link.SentAt=Now;f.RefundCount=Math.Min(FriendNumber(BscTable.NUMBER_TYPE.FRIEND_STAMINA_REFUND_MAX_COUNT),f.RefundCount+1);
            return ok(new GsHaveDrinkOk{currentCoinInfo=Coins(p),remainDrinkCount=FriendSendLeft(p),refundDrinkCount=f.RefundCount});
        }
        if(request is GsReceiveHaveDrinkRequest receive){
            uint id=receive.friendUid;uint gain;
            if(id==0){if(f.RefundCount==0)return Bad("no gift refunds");gain=checked(f.RefundCount*FriendNumber(BscTable.NUMBER_TYPE.FRIEND_STAMINA_REFUND));}
            else{if(!f.Links.TryGetValue(id,out var link)||link.GiftDay!=f.Day||f.ReceivedToday.Contains(id))return Bad("no pending gift");if(FriendReceiveLeft(p)==0)return Bad("daily receive limit");gain=FriendNumber(BscTable.NUMBER_TYPE.FRIEND_STAMINA);}
            if((ulong)p.Stamina+gain>999)return Bad("stamina capacity",ServerErrorCode.ERROR_OVER_MAX_STAMINA);
            p.Stamina+=gain;if(id==0)f.RefundCount=0;else f.ReceivedToday.Add(id);
            return ok(new GsReceiveHaveDrinkOk{stamina=p.Stamina,addStamina=gain,staminaStamp=p.StaminaStamp,remainReceiveDrinkCount=FriendReceiveLeft(p),refundDrinkCount=f.RefundCount});
        }
        if(request is GsGetAnotherUserGameInfoRequest profile){
            var t=FriendProfile(profile.targetUid);if(t==null)return Bad("profile not found",ServerErrorCode.ERROR_INVALID_FRIEND_INFO);var q=store.Find(t.uid)??Bots.Find(t.uid)?.Profile;
            var n=q!=null?NameCard(q):new PKUserNameCardInfo{uid=t.uid,introMsg="本地伙伴：离线模拟互动",helperID=(BscTable.HELPER_TYPE)1,commonInfo=new(){nick=t.nick,level=t.level,account=t.account},leaderCardInfo=t.leaderCardInfo,avatarCardId=t.avatarCardId};
            n.commonInfo.nick=t.nick;
            var r=new GsGetAnotherUserGameInfoOk{targetUid=t.uid,nameCardInfo=n,helperCostumeId=n.helperCostumeId,townExhibitionDeckInfo=new(){deckID=(BscTable.USER_DECK_INDEX_TYPE)1,cardSlot1=t.leaderCardInfo.cardSeq}};
            if(q!=null)
            {
                var deck=q.Deck.Take(5).Concat(Enumerable.Repeat(0UL,5)).Take(5).ToArray();
                r.townExhibitionDeckInfo.cardSlot1=deck[0];r.townExhibitionDeckInfo.cardSlot2=deck[1];r.townExhibitionDeckInfo.cardSlot3=deck[2];r.townExhibitionDeckInfo.cardSlot4=deck[3];r.townExhibitionDeckInfo.cardSlot5=deck[4];
                foreach(ulong seq in deck.Where(x=>x>0&&Owns(q,x)))
                {var s=CardState(q,seq);r.townExhibitionCardInfoList.Add(new(){cardSeq=(uint)seq,cardId=q.CardIds[(int)seq-1],cardLevel=s.Level,cardSkinId=s.Skin,cardEnchantGrade=s.Enchant});}
            }
            else {var l=t.leaderCardInfo;r.townExhibitionCardInfoList.Add(new(){cardSeq=(uint)l.cardSeq,cardId=l.cardID,cardLevel=l.cardLevel,cardSkinId=l.cardSkinId});}
            return ok(r);
        }
        PKChatMessage[] TakeInbox(){var rows=f.Inbox.Select(x=>(PKChatMessage)WireCodec.Deserialize(typeof(PKChatMessage),Convert.FromBase64String(x))!).Take(15).ToArray();f.Inbox.RemoveRange(0,rows.Length);return rows;}
        if(name=="GS_FETCH_PRIVATE_CHAT_MESSAGE_REQUEST"){
            var r=new GsFetchPrivateChatMessageOk();r.msgList.AddRange(TakeInbox());r.totalDataCount=r.dataCount=(uint)r.msgList.Count;return ok(r);
        }
        if(request is GsSendPrivateChatMessageRequest chat){
            string text=(chat.msg??"").Trim();uint id=chat.recieverUid;var t=FriendProfile(id);if(t==null||!f.Links.ContainsKey(id))return Bad("private chat requires a friend",ServerErrorCode.ERROR_NOT_FRIEND);
            if(text.Length is <1 or >100||text.Any(char.IsControl)||text.IndexOfAny(['[',']'])>=0)return Bad("chat accepts 1..100 plain text characters",ServerErrorCode.ERROR_INVALID_ID);if(f.LastChatTime+3>Now)return Bad("wait before sending again");
            void Queue(LocalFriends destination,PKChatMessage msg){destination.Inbox.Add(Convert.ToBase64String(WireCodec.Serialize(msg)));while(destination.Inbox.Count>100)destination.Inbox.RemoveAt(0);}
            var n=NameCard(p);var target=store.Find(id);
            if(target!=null)Queue(target.Friends,new(){type=ChatType.CHAT_TYPE_PRIVATE,senderUid=p.Uid,senderCommonInfo=n.commonInfo,leaderCardInfo=n.leaderCardInfo,receiverUid=id,receiverNick=t.nick,receiverLevel=t.level,message=text,timeStamp=Now});
            else Queue(f,new(){type=ChatType.CHAT_TYPE_PRIVATE,senderUid=id,senderCommonInfo=new(){nick=t.nick,level=t.level,account=t.account},leaderCardInfo=t.leaderCardInfo,receiverUid=p.Uid,receiverNick=p.Nickname,receiverLevel=p.Level,message="（本地伙伴）"+(text.Contains("你好")?"你好，今天也一起慢慢养卡吧。":text.Contains("卡")?"别忘了留些金币升级常用卡的技能。":"收到啦。我先去闯关，回头见。"),timeStamp=Now});
            f.LastChatTime=Now;var r=new GsSendPrivateChatMessageOk();r.msgList.AddRange(TakeInbox());r.totalDataCount=r.dataCount=(uint)r.msgList.Count;return ok(r);
        }
        return Bad("unsupported friends action");
    }
}
