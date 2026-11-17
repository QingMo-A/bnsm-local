using System.IO.Compression;
using System.Text.Json;
using ProtoBuf;

internal sealed class LocalFactionState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public uint Used {get;set;}
    public uint Bought {get;set;}
    public uint PaidLeft {get;set;}
    public uint Points {get;set;}
    public uint Battles {get;set;}
    public uint Wins {get;set;}
    public uint Streak {get;set;}
    public uint LossStreak {get;set;}
    public uint MaxStreak {get;set;}
    public uint GradeReward {get;set;}
    public HashSet<int> GradeClaims {get;set;}=[];
    public uint Opponent {get;set;}
    public uint MatchDeck {get;set;}
    public ulong MatchedAt {get;set;}
    public byte[] Enemy {get;set;}=[];
    public List<ulong> Cards {get;set;}=[];
    public uint? ReplayWinner {get;set;}
    public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    private readonly Dictionary<uint,(uint Total,MemoryStream Data)> factionUploads=[];
    private List<BscTable.FactionPvpGradeData>? factionGrades;
    private List<BscTable.FactionPvpGradeData> FactionGrades {get {
        if(factionGrades==null){using var stream=File.OpenRead(Path.Combine(RootDirectory,"LocalServer/data/tables/FactionPVPGradeDataTable.pb"));factionGrades=Serializer.Deserialize<BscTable.FactionPvpGradeDataTable>(stream).List;}return factionGrades;
    }}
    internal static bool FactionCardAllowed(BscTable.CardData card,uint index)
    {
        int faction=index is 1 or 2?1:index is 3 or 4?2:0;
        return index is >=1 and <=6&&(int)card.Faction==faction&&(int)card.AttackType==(index%2==1?0:1);
    }
    internal static uint FactionDeckIndex(ulong now)=>(uint)(((now+8*3600)/86400/7)%6)+1;
    private void RefreshFaction(LocalPlayer p)
    {
        var f=p.Faction;ulong day=(Now+8*3600)/86400;
        if(f.Day!=day){f.Day=day;f.Used=0;f.Bought=0;}
        if(f.Opponent!=0&&(Now<f.MatchedAt||Now-f.MatchedAt>1800))ClearFactionMatch(p);
    }
    private void ClearFactionMatch(LocalPlayer p)
    {
        var f=p.Faction;f.Opponent=0;f.Enemy=[];f.Cards.Clear();f.ReplayWinner=null;
        if(factionUploads.Remove(p.Uid,out var upload))upload.Data.Dispose();
    }
    private PKFactionArenaBattleUserInfo FactionUser(LocalPlayer p)
    {
        var thumbnail=GuildThumbnail(p);var common=NameCard(p).commonInfo;if(p.Account.StartsWith("world-bot:",StringComparison.Ordinal))common.nick+=" · 人机";
        return new(){uid=p.Uid,serverId=1,commonInfo=common,
            leaderCardId=thumbnail.avatarCardId,leaderCardLevel=thumbnail.leaderCardInfo?.cardLevel??1,
            leaderCardEnchantGrade=thumbnail.leaderCardInfo?.enchantGrade??0};
    }
    private uint FactionRank(LocalPlayer p)=>p.Faction.Battles==0?0u:(uint)store.All().Where(x=>x.Faction.Battles>0).OrderByDescending(x=>x.Faction.Points).ThenBy(x=>x.Uid).Select((x,i)=>(x.Uid,Rank:(uint)i+1)).First(x=>x.Uid==p.Uid).Rank;
    private PKFactionArenaBattlePlayInfo FactionPlay(LocalPlayer p)=>new(){arenaPoint=p.Faction.Points,rank=FactionRank(p),battleCount=p.Faction.Battles,winCount=p.Faction.Wins,currentWinStreak=p.Faction.Streak,maxWinStreak=p.Faction.MaxStreak,gradeRewardId=p.Faction.GradeReward};
    private Reply? HandleFaction(string name,object? request,LocalPlayer p,SessionKeyInfo key,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool purchase=request is GsBuyBonusCountRequest b&&b.priceType==BscTable.VIP_PRICE_DISCOUNT_TYPE.VIP_PRICE_DISCOUNT_BUY_FACTION_PVP_ENTER_COUNT;
        bool upload=request is GsUploadReplayDataRequest u&&u.gameMode==BscTable.GAME_MODE.MODE_FACTION_PVP;
        if(!purchase&&!upload&&!name.Contains("FACTION_ARENA_BATTLE")&&name!="GS_GET_PVP_ENTRANCE_INFO_REQUEST")return null;
        RefreshFaction(p);
        return FeatureReceipt(p,p.Faction.Receipts,name,key,payload,()=>FactionOperation(name,request,p,ok,fail),fail,name is not ("GS_GET_FACTION_ARENA_BATTLE_RANK_INFO_REQUEST" or "GS_GET_PVP_ENTRANCE_INFO_REQUEST"));
    }
    private Reply FactionOperation(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var f=p.Faction;Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);
        if(name=="GS_GET_PVP_ENTRANCE_INFO_REQUEST")return ok(new GsGetPvpBattleEntranceInfoOk{factionArenaBattleEntranceCount=f.Used,factionArenaBattleBuyBonusCountInfo=new(){todayBuyBonusCount=f.Bought,remainPaidCount=f.PaidLeft},factionArenaBattleDeckIndex=FactionDeckIndex(Now)});
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_FACTION_PVP))return fail("faction battle requires its original player level, not main-story progress",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(name=="GS_ENTER_FACTION_ARENA_BATTLE_REQUEST") {
            // Returning to the lobby abandons an unfinished preview, without granting a result.
            ClearFactionMatch(p);return ok(new GsEnterFactionArenaBattleOk{playInfo=FactionPlay(p)});
        }
        if(request is GsGetFactionArenaBattleRankInfoRequest page) {
            if(page.requestCount>100||page.beginId>100000)return Bad("invalid local faction rank page");
            var result=new GsGetFactionArenaBattleRankInfoOk();
            result.rankInfoList.AddRange(store.All().Where(x=>x.Faction.Battles>0).OrderByDescending(x=>x.Faction.Points).ThenBy(x=>x.Uid).Skip((int)page.beginId).Take((int)page.requestCount).Select(x=>new PKFactionArenaBattleRankInfo{userInfo=FactionUser(x),rank=FactionRank(x),arenaPoint=x.Faction.Points}));return ok(result);
        }
        if(request is GsGetFactionArenaBattleEnemyInfoRequest match) {
            if(match.deckIndex!=FactionDeckIndex(Now))return Bad("refresh faction lobby before matching a different weekly deck");
            if(f.Opponent!=0&&f.MatchDeck==match.deckIndex&&f.Enemy.Length>0)return ok(new GsGetFactionArenaBattleEnemyInfoOk{enemyInfo=(PKFactionArenaBattleEnemyInfo)WireCodec.Deserialize(typeof(PKFactionArenaBattleEnemyInfo),f.Enemy)!});
            if(p.ActiveStage!=null)return Bad("finish the active battle before faction matching");
            var cards=DeckSlots(p,25+match.deckIndex).Where(x=>x!=0).ToList();
            if(cards.Count==0||cards.Count>5||cards.Distinct().Count()!=cards.Count||cards.Any(x=>!Owns(p,x)||InTraining(p,x)||!FactionCardAllowed(catalog.RequireCard(p.CardIds[(int)x-1]),match.deckIndex)))return Bad("faction deck needs owned, eligible and available cards");
            uint free=(uint)Math.Max(0,catalog.VipFor(p.VipExp).FactionPvpEnterCount);
            if(f.Used>=free&&f.PaidLeft==0)return fail("no faction entries remaining today",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var candidates=Bots.All().OrderBy(x=>x.Uid).ToArray();if(candidates.Length==0)return Bad("no local bot opponents configured");
            var source=candidates[(int)(f.Battles%(uint)candidates.Length)];
            int offset=f.Battles<5?-1:new[]{-1,-1,0,-1,0,1}[f.Battles%6];
            var variant=CompetitiveProfile(MakeCompetitiveOffer(p,source,CompetitiveBaseGrade(p,43)+offset,2),match.deckIndex);
            var enemy=new PKFactionArenaBattleEnemyInfo{rankInfo=new(){userInfo=FactionUser(variant),rank=0,arenaPoint=0},deckIndex=match.deckIndex,formationInfo=new(){id=1,level=1}};
            foreach(ulong sequence in variant.Deck) {
                var s=variant.CardStates[sequence];var clone=new PKCloneBattleCardInfo{cardId=variant.CardIds[(int)sequence-1],cardLevel=s.Level,cardEnchant=s.Enchant,cardSkinId=s.Skin};
                clone.skillLevel.AddRange(s.ActiveSkills.Concat(s.PassiveSkills));clone.cardSkillEnchant.AddRange(SkillEnchantSlots(s));
                foreach(ulong gear in new[]{s.WeaponSequence}.Concat(s.AccessorySequences))if(variant.EquipmentItems.FirstOrDefault(x=>x.Sequence==gear) is {} item)clone.cardEquipInfo.Add(item.ItemId);
                clone.cardEquipInfo.AddRange(s.GemIds);enemy.cardInfoList.Add(clone);
            }
            f.Opponent=source.Uid;f.MatchDeck=match.deckIndex;f.MatchedAt=Now;f.Cards=cards;f.ReplayWinner=null;f.Enemy=WireCodec.Serialize(enemy);
            return ok(new GsGetFactionArenaBattleEnemyInfoOk{enemyInfo=enemy});
        }
        if(request is GsUploadReplayDataRequest part) {
            if(p.ActiveStage!=null||f.Opponent==0||part.replayData==null||part.currentReplayDataSize!=part.replayData.Length||part.currentReplayDataSize is 0 or >16384||part.totalReplayDataSize is 0 or >2097152)return Bad("invalid faction replay chunk");
            if(part.firstSendDataFlag&&!factionUploads.ContainsKey(p.Uid)&&factionUploads.Count>=8)return Bad("too many pending faction replay uploads");
            if(part.firstSendDataFlag){if(factionUploads.Remove(p.Uid,out var old))old.Data.Dispose();factionUploads[p.Uid]=(part.totalReplayDataSize,new MemoryStream());f.ReplayWinner=null;}
            if(!factionUploads.TryGetValue(p.Uid,out var upload)||upload.Total!=part.totalReplayDataSize||upload.Data.Length+part.replayData.Length>upload.Total)return Bad("faction replay chunks are out of sequence");
            upload.Data.Write(part.replayData);
            if(upload.Data.Length==upload.Total) {
                try {
                    upload.Data.Position=0;using var zlib=new ZLibStream(upload.Data,CompressionMode.Decompress,true);using var decoded=new MemoryStream();byte[] buffer=new byte[65536];int n;
                    while((n=zlib.Read(buffer))>0){if(decoded.Length+n>16*1024*1024)return Bad("faction replay expands beyond limit");decoded.Write(buffer,0,n);}
                    var replay=(PKBattleReplayData)WireCodec.Deserialize(typeof(PKBattleReplayData),decoded.ToArray())!;
                    if(replay.gameMode!=BscTable.GAME_MODE.MODE_FACTION_PVP||replay.winTeamIndex>1||replay.thumbNailInfo.Count!=2||replay.thumbNailInfo[0].uid!=p.Uid||replay.thumbNailInfo[1].uid!=f.Opponent||replay.battleCardInfo.Count is <2 or >64||replay.battleProcessInfo.Count==0)return Bad("faction replay does not match the pending battle");
                    var chosen=DeckSlots(p,25+f.MatchDeck).Where(x=>x!=0).ToList();
                    if(chosen.Count is <1 or >5||chosen.Distinct().Count()!=chosen.Count||chosen.Any(x=>!Owns(p,x)||InTraining(p,x)||!FactionCardAllowed(catalog.RequireCard(p.CardIds[(int)x-1]),f.MatchDeck))||
                        !replay.battleCardInfo.Where(x=>x.teamIndex==0).OrderBy(x=>x.cardSlotIndex).Select(x=>x.cardId).SequenceEqual(chosen.Select(x=>p.CardIds[(int)x-1])))return Bad("faction replay player deck mismatch");
                    var pending=(PKFactionArenaBattleEnemyInfo)WireCodec.Deserialize(typeof(PKFactionArenaBattleEnemyInfo),f.Enemy)!;
                    if(!replay.battleCardInfo.Where(x=>x.teamIndex==1).OrderBy(x=>x.cardSlotIndex).Select(x=>x.cardId).SequenceEqual(pending.cardInfoList.Select(x=>x.cardId)))return Bad("faction replay opponent deck mismatch");
                    f.Cards=chosen;
                    f.ReplayWinner=replay.winTeamIndex;
                }catch(Exception error)when(error is IOException or InvalidDataException or ProtoException){return Bad("corrupt faction replay");}
                finally{factionUploads.Remove(p.Uid);upload.Data.Dispose();}
            }
            return ok(new GsUploadReplayDataOk{replaySeq=f.MatchedAt});
        }
        if(request is GsUpdateFactionArenaBattleResultRequest end) {
            if(p.ActiveStage!=null||f.Opponent==0||end.targetUid!=f.Opponent)return Bad("faction result has no matching pending opponent");
            if(end.battleResult>1||f.ReplayWinner!=end.battleResult)return Bad("faction result disagrees with completed native replay");
            // PKUserDeckInfo exposes fifteen slots even in a five-card faction match.
            if(end.useCardSeq.Count>15||!end.useCardSeq.Where(x=>x!=0).Order().SequenceEqual(f.Cards.Order()))return Bad("faction result card roster differs from native replay");
            // Native automatic PvP sends no stage loot/history entries. Protobuf omits
            // empty submessages, so a null history/sync object is a valid default.
            bool won=end.battleResult==0;uint delta=(uint)Math.Max(1,catalog.Numbers[BscTable.NUMBER_TYPE.FACTION_PVP_POINT_DELTA_MIN]);
            uint loss=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.FACTION_PVP_POINT_LOSE]);
            uint points=won?(uint)Math.Min(uint.MaxValue,(ulong)f.Points+delta):f.Points>loss?f.Points-loss:0;
            var grade=FactionGrades.Where(x=>x.Point_Min<=points&&x.Point_Max>=points).OrderByDescending(x=>x.Point_Min).FirstOrDefault();
            var grant=new ShopGrant();uint reward=0;
            if(won&&grade!=null&&!f.GradeClaims.Contains(grade.Id)) {
                for(int i=0;i<grade.RewardId.Count;i++)if(!AddShopReward(p,grant,grade.RewardIdType[i],grade.RewardId[i],grade.RewardQuantity[i]))return Bad("original faction grade reward unavailable");
                if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("faction grade reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                reward=(uint)grade.Id;
            }
            uint free=(uint)Math.Max(0,catalog.VipFor(p.VipExp).FactionPvpEnterCount);if(f.Used>=free){if(f.PaidLeft==0)return fail("no faction entries remaining",ServerErrorCode.ERROR_OVER_MAX_COUNT);f.PaidLeft--;}
            f.LossStreak=won?0:f.LossStreak+1;f.Used++;f.Battles++;f.Points=points;if(won){f.Wins++;f.Streak++;f.MaxStreak=Math.Max(f.MaxStreak,f.Streak);}else f.Streak=0;
            if(reward!=0){ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,[],[]);f.GradeClaims.Add((int)reward);f.GradeReward=reward;}
            ClearFactionMatch(p);
            RecordObjective(p, BscTable.CHECK_TYPE.MCT_ENTER_FACTION_PVP);
            if (won) RecordObjective(p, BscTable.CHECK_TYPE.MCT_WIN_FACTION_PVP);
            return ok(new GsUpdateFactionArenaBattleResultOk{playInfo=FactionPlay(p),deltaPoint=won?delta:loss,rewardId=reward,battleEndSyncData=new()});
        }
        if(request is GsBuyBonusCountRequest buy) {
            uint count=buy.buyCount,max=(uint)Math.Max(0,catalog.VipFor(p.VipExp).FactionPvpEnterBonusCount);
            if(count==0||(ulong)f.Bought+count>max)return fail("faction ticket purchase limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var price=Raids.Prices.Single(x=>x.VipRiceDiscountType==buy.priceType).PriceDiscountBuyCountInfoList.SingleOrDefault(x=>x.Count==count);
            if(price==null||price.Price<0||p.CashPoint<price.Price)return fail("not enough faction ticket diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)price.Price;f.Bought+=count;f.PaidLeft+=count;return ok(new GsBuyBonusCountOk{coinInfo=Coins(p),todayBuyCount=f.Bought,changedValue=f.PaidLeft,priceType=buy.priceType});
        }
        return Bad("unsupported faction operation");
    }
}
