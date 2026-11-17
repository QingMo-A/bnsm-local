using System.IO.Compression;
using ProtoBuf;

internal sealed partial class ProtocolRouter
{
    private CompetitiveCatalog? competitive;
    private CompetitiveCatalog Competitive=>competitive??=new(RootDirectory);
    private readonly Dictionary<(uint,int,string),LocalPlayer> competitiveBots=[];
    private readonly Dictionary<uint,(uint Total,MemoryStream Data)> competitiveUploads=[];
    private LocalPvpRecord PvpRecord(LocalPlayer p,uint mode)
    {
        if(!p.Competitive.Records.TryGetValue(mode,out var record))
            p.Competitive.Records[mode]=record=new(){RewardDay=mode==31?TowerDay(Now):0};
        return record;
    }
    private void RefreshCompetitive(LocalPlayer p)
    {
        var c=p.Competitive;ulong day=(Now+8*3600)/86400;
        if(c.WorldDay==0&&c.WorldScores.Count>0)
        {
            c.WorldDay=c.Day;
            c.WorldWeek=c.Week==c.Day/7?WorldWeek(c.Day):0;
        }
        if(c.Records.TryGetValue(31,out var infinite))
        {
            ulong rewardDay=TowerDay(Now);
            if(infinite.RewardDay==0)infinite.RewardDay=c.Day==0?rewardDay:Math.Min(c.Day,rewardDay);
            if(infinite.RewardDay<rewardDay){infinite.RewardDay=rewardDay;infinite.RewardPoints=0;infinite.RewardClaims.Clear();}
        }
        if(c.Day!=day){c.Day=day;foreach(var r in c.Records.Where(x=>x.Key!=29).Select(x=>x.Value)){r.Used=0;r.Bought=0;r.CoolResets=0;}c.CastleSkips.Clear();c.CastleExchangeBought=0;c.CastleAttacks=c.CastleBought=c.CastleRecoveries=c.CastleExchanges=c.CastleExchangeWealth=c.CastleRestoreHelps=0;c.WantedBattled.Clear();c.WorldRevives=0;c.WorldReviveBought=0;}
        if(c.Stamp!=0&&(Now<c.OpenedAt||Now-c.OpenedAt>1800))ClearCompetitive(p);
    }
    private void ClearCompetitive(LocalPlayer p)
    {
        var c=p.Competitive;LocalBattleAttempts.ClearMatching(p,0xF0000000u+c.ActiveMode,c.Stamp);
        c.ActiveMode=c.Opponent=c.OpponentRank=c.Map=c.Spot=c.Action=0;c.Stamp=0;c.Enemy=[];c.Cards.Clear();c.ReplayWinner=null;c.ReplayResults.Clear();c.Started=false;
        c.ReserveCards.Clear();c.ManualControl=false;c.GuildId=c.GuildMercenaryUid=0;c.GuildReplayCards.Clear();
        c.CastleStamped=false;c.CastleAdmissionPaid=false;
        if(competitiveUploads.Remove(p.Uid,out var upload))upload.Data.Dispose();
    }
    private PKCloneBattleCardInfo CompetitiveClone(LocalPlayer p,ulong seq)
    {
        var s=CardState(p,seq);var result=new PKCloneBattleCardInfo{cardId=p.CardIds[(int)seq-1],cardLevel=s.Level,cardEnchant=s.Enchant,cardSkinId=s.Skin};
        result.skillLevel.AddRange(s.ActiveSkills.Concat(s.PassiveSkills));result.cardSkillEnchant.AddRange(SkillEnchantSlots(s));
        foreach(ulong gear in new[]{s.WeaponSequence}.Concat(s.AccessorySequences))if(p.EquipmentItems.FirstOrDefault(x=>x.Sequence==gear) is {} item)result.cardEquipInfo.Add(item.ItemId);
        for(int i=0;i<EngravingRules.Attributes.Length;i++)result.empowerEffectSum.Add((uint)new[]{s.WeaponSequence}.Concat(s.AccessorySequences).Select(gear=>p.EquipmentItems.FirstOrDefault(x=>x.Sequence==gear)).Where(x=>x!=null).Sum(x=>(long)x!.EmpowerValues.ElementAtOrDefault(i)));
        result.cardEquipInfo.AddRange(s.GemIds);result.effectDataInfoList.AddRange(s.TranscendenceEffects.Select(x=>new PKEffectDataInfo{effectDataId=x.Id,effectDataValue=x.Value,effectGrade=x.Grade}));return result;
    }
    private PKCloneBattleEnemyInfo CompetitiveEnemy(LocalPlayer p)
    {
        var first=CardState(p,p.Deck.First(x=>x!=0));var r=new PKCloneBattleEnemyInfo{uid=p.Uid,commonInfo=NameCard(p).commonInfo,leaderCardId=p.CardIds[(int)p.Deck[0]-1],leaderCardLevel=first.Level,leaderCardEnchantGrade=first.Enchant,formationInfo=new(){id=1,level=1}};
        r.cardInfoList.AddRange(p.Deck.Where(x=>x!=0).Select(seq=>CompetitiveClone(p,seq)));return r;
    }
    private bool CompetitiveRoster(LocalPlayer p,IEnumerable<ulong> slots,int max,out List<ulong> cards)
    {
        cards=slots.Where(x=>x!=0).ToList();return cards.Count>0&&cards.Count<=max&&cards.Count==cards.Distinct().Count()&&cards.All(x=>Owns(p,x)&&!InTraining(p,x));
    }
    private void BeginCompetitive(LocalPlayer p,uint mode,LocalPlayer enemy,IEnumerable<ulong> cards,uint map=0,uint spot=0)
    {
        ClearCompetitive(p);var c=p.Competitive;c.ActiveMode=mode;c.Opponent=enemy.Uid;c.Map=map;c.Spot=spot;c.Stamp=NextLocalBattleStamp(p);c.OpenedAt=Now;c.Cards=cards.Where(x=>x!=0).ToList();c.Enemy=WireCodec.Serialize(mode==42?BalancedEnemy(enemy):CompetitiveEnemy(enemy));
    }
    private BscTable.PVP_BATTLE_TYPE PvpType(uint mode)=>(BscTable.PVP_BATTLE_TYPE)(mode==10?2:mode==42?23:mode==31?9:3);
    private uint PvpLimit(LocalPlayer p,uint mode)
    {
        var v=catalog.VipFor(p.VipExp);return (uint)Math.Max(0,mode==10?v.RankingPvpEnterCount:mode==42?v.BalancePvpEnterCount:mode==31?v.InfiniteArenaPvpEnterCount:v.ArenaPvpEnterCount);
    }
    private PKRankBattleRecord RankRecord(LocalPlayer p,uint mode)
    {
        var r=PvpRecord(p,mode);return new(){todayEntranceCount=r.Used,totalBattleCount=r.Battles,totalBattleWinCount=r.Wins,totalBattleWinningStreakCount=r.MaxStreak,currentWinningStreakCount=r.Streak,weeklyRankPoint=r.Points,weeklyBattleWinRate=r.Battles==0?0:(uint)(100UL*r.Wins/r.Battles),weeklyBattleWinningStreakCount=r.MaxStreak,rank=r.Battles==0?0:r.Rank,bestRank=r.BestRank,buyBonusCountInfo=new(){todayBuyBonusCount=r.Bought,remainPaidCount=r.PaidLeft}};
    }
    private GsGetPvpBattleEntranceInfoOk CompetitiveEntrance(LocalPlayer p)
    {
        PKBuyBonusCountInfo Bonus(uint mode){var r=PvpRecord(p,mode);return new(){todayBuyBonusCount=r.Bought,remainPaidCount=r.PaidLeft};}
        return new(){chainBattleTodayResetCount=p.ChainBattle.Used,chainBattleContinueFlag=p.ChainBattle.Continuing,chainBattleChallengeWinCount=p.ChainBattle.Wins,guildBattleEntranceCount=PvpRecord(p,29).Used,guildBattleLatestEntranceTime=PvpRecord(p,29).Latest,guildBattleEntranceResetCount=PvpRecord(p,29).Bought,guildBattleCoolTimeResetCount=PvpRecord(p,29).CoolResets,arenaBattleEntranceCount=PvpRecord(p,12).Used,arenaBattleLatestEntranceTime=PvpRecord(p,12).Latest,arenaBattleBuyBonusCountInfo=Bonus(12),rankBattleEntranceCount=PvpRecord(p,10).Used,rankBattleBuyBonusCountInfo=Bonus(10),infiniteArenaBattleEntranceCount=PvpRecord(p,31).Used,infiniteArenaBattleLatestEntranceTime=PvpRecord(p,31).Latest,infiniteArenaBattleBuyBonusCountInfo=Bonus(31),balanceBattleEntranceCount=PvpRecord(p,42).Used,balanceBattleBuyBonusCountInfo=Bonus(42),factionArenaBattleEntranceCount=p.Faction.Used,factionArenaBattleBuyBonusCountInfo=new(){todayBuyBonusCount=p.Faction.Bought,remainPaidCount=p.Faction.PaidLeft},factionArenaBattleDeckIndex=FactionDeckIndex(Now)};
    }
    private PKArenaBattleEnemyUserInfo ArenaEnemy(LocalPlayer enemy,uint grade,uint rank)
    {
        var s=CardState(enemy,enemy.Deck[0]);return new(){enemyUid=enemy.Uid,enemyCardId=enemy.CardIds[(int)enemy.Deck[0]-1],enemyCardLevel=s.Level,enemyCardEnchantGrade=s.Enchant,enemyCardSkinId=s.Skin,enemyCommonInfo=NameCard(enemy).commonInfo,enemyRank=rank,enemyRankGrade=grade};
    }
    private Reply? HandleCompetitive(string name,object? request,LocalPlayer p,SessionKeyInfo key,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool upload=request is GsUploadReplayDataRequest u&&p.Competitive.ActiveMode!=0&&((uint)u.gameMode==p.Competitive.ActiveMode||p.Competitive.ActiveMode is 10 or 42&&(uint)u.gameMode==12);
        bool handled=request is LocalRankCombatRequest or GsEnterArenaBattleRequest or GsSearchArenaBattleEnemyListRequest or GsGetArenaBattleHistoryRequest or GsUpdateArenaBattleResultRequest or GsEnterInfiniteArenaBattleRequest or GsSearchInfiniteArenaBattleEnemyListRequest or GsGetInfiniteArenaBattleEnemyDeckInfoRequest or GsUpdateInfiniteArenaBattleResultRequest or GsGetInfiniteArenaBattleHistoryRequest or GsUpdateInfiniteArenaUserDeckInfoRequest or GsGetInfinitArenaTodayRewardRequest or GsResetPvpBattleEntranceCountRequest;
        handled|=name=="GS_GET_PVP_ENTRANCE_INFO_REQUEST"||name=="GS_GET_INFINIT_ARENA_DECK_INFO_REQUEST"||request is GsGetArenaBattleEnemyDeckInfoRequest a&&!a.friendlyBattle;
        handled|=request is GsResetPvpBattleCoolTimeRequest reset && (int)reset.pvpBattleType is 3 or 6 or 9;
        handled|=request is GsBuyBonusCountRequest buy && (CompetitiveBuyMode(buy.priceType)!=0 || (int)buy.priceType is 4 or 5 or 16);
        handled|=name.Contains("CASTLE")||name.Contains("WORLD_WAR")||name=="GS_GET_BALANCE_BATTLE_RECORD_REQUEST";
        handled|=request is GsSearchMapSpotRequest or GsExchangeWealthPointRequest or GsBatchExchangeWealthPointRequest;
        if(!handled&&!upload)return null;
        RefreshCompetitive(p);RefreshFaction(p);
        bool mutation=upload||request is GsGetInfinitArenaTodayRewardRequest or GsGetCastleBattleAttackMatchRequest or GsGetAttackedCastleInfoRequest or GsGetFriendCastleMapInfoRequest or GsGetRevengeTargetCastleMapInfoRequest||!name.StartsWith("GS_GET_",StringComparison.Ordinal)||request is LocalRankCombatRequest r&&r.operation!=0;
        if(request is LocalRankCombatRequest{mode:18,operation:13})mutation=false;
        return FeatureReceipt(p,p.Competitive.Receipts,name,key,payload,()=>{
            if(upload)return CompetitiveUpload((GsUploadReplayDataRequest)request!,p,ok,fail);
            if(name=="GS_GET_PVP_ENTRANCE_INFO_REQUEST")return ok(CompetitiveEntrance(p));
            var guildBattle=GuildBattleOperation(name,request,p,ok,fail);if(guildBattle!=null)return guildBattle;
            var extra=CompetitiveExtras(request,p,ok,fail);if(extra!=null)return extra;
            var castle=HandleCompetitiveCastle(name,request,p,ok,fail);if(castle!=null)return castle;
            var world=HandleCompetitiveWorld(name,request,p,ok,fail);if(world!=null)return world;
            return ExecuteCompetitiveArena(name,request,p,ok,fail);
        },fail,mutation);
    }
    private Reply CompetitiveUpload(GsUploadReplayDataRequest part,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);var c=p.Competitive;
        if(c.Opponent==0||part.replayData==null||part.currentReplayDataSize!=part.replayData.Length||part.currentReplayDataSize is 0 or >16384||part.totalReplayDataSize is 0 or >2097152)return Bad("invalid competitive replay chunk");
        if(part.firstSendDataFlag){if(competitiveUploads.Remove(p.Uid,out var old))old.Data.Dispose();if(competitiveUploads.Count>=8)return Bad("too many replay uploads");competitiveUploads[p.Uid]=(part.totalReplayDataSize,new());c.ReplayWinner=null;}
        if(!competitiveUploads.TryGetValue(p.Uid,out var upload)||upload.Total!=part.totalReplayDataSize||upload.Data.Length+part.replayData.Length>upload.Total)return Bad("replay chunks out of sequence");
        upload.Data.Write(part.replayData);
        if(upload.Data.Length==upload.Total) {
            try {
                upload.Data.Position=0;using var zlib=new ZLibStream(upload.Data,CompressionMode.Decompress,true);using var decoded=new MemoryStream();byte[] buffer=new byte[65536];int n;
                while((n=zlib.Read(buffer))>0){if(decoded.Length+n>16*1024*1024)return Bad("replay expansion limit");decoded.Write(buffer,0,n);}
                var replay=(PKBattleReplayData)WireCodec.Deserialize(typeof(PKBattleReplayData),decoded.ToArray())!;
                bool modeMatches=(uint)replay.gameMode==c.ActiveMode||c.ActiveMode is 10 or 42&&(uint)replay.gameMode==12;
                if(!modeMatches||replay.winTeamIndex>1||replay.thumbNailInfo.Count!=2||replay.thumbNailInfo[0].uid!=p.Uid||replay.thumbNailInfo[1].uid!=c.Opponent||replay.battleProcessInfo.Count==0)return Bad("replay does not match pending battle");
                if(c.ActiveMode==18&&!c.Started)return Bad("castle battle was not started");
                var actual=replay.battleCardInfo.Where(x=>x.teamIndex==0).OrderBy(x=>x.cardSlotIndex).Select(x=>x.cardId).ToArray();
                var expected=c.Cards.Concat(c.ReserveCards).Select(x=>c.ActiveMode==42?(uint)BalanceRow(p.CardIds[(int)x-1]).CardId:p.CardIds[(int)x-1]).ToArray();
                if(c.ActiveMode==29)expected=c.GuildReplayCards.ToArray();
                if(c.ActiveMode!=31&&!actual.SequenceEqual(expected))return Bad("replay player roster mismatch");
                var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),c.Enemy)!;
                if(c.ActiveMode!=31&&!replay.battleCardInfo.Where(x=>x.teamIndex==1).OrderBy(x=>x.cardSlotIndex).Select(x=>x.cardId).SequenceEqual(enemy.cardInfoList.Select(x=>x.cardId)))return Bad("replay opponent roster mismatch");
                if(c.ManualControl&&!ManualReserveReplayMatches(p,replay))return fail("competitive replay has invalid reserve identity, slot or entry spirit",ServerErrorCode.ERROR_INVALID_SIDE_DECK_CARD_INFO);
                c.ReplayWinner=replay.winTeamIndex;c.ReplayResults.Add(replay.winTeamIndex);
                ulong replayId=checked(c.Stamp+(ulong)c.ReplayResults.Count-1);c.Replays[replayId]=upload.Data.ToArray();p.LastBattleStamp=Math.Max(p.LastBattleStamp,replayId);
                while(c.Replays.Count>20||c.Replays.Values.Sum(x=>(long)x.Length)>8388608)c.Replays.Remove(c.Replays.Keys.Min());
            }catch(Exception e)when(e is IOException or ProtoException){return Bad("corrupt competitive replay");}
            finally{competitiveUploads.Remove(p.Uid);upload.Data.Dispose();}
        }
        return ok(new GsUploadReplayDataOk{replaySeq=c.Stamp+(ulong)Math.Max(0,c.ReplayResults.Count-1)});
    }
}
