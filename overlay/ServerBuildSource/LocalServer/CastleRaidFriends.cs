using ProtoBuf;

[ProtoContract]
internal sealed class LocalCastleRaidHelpListRequest
{
    [ProtoMember(1,IsRequired=true)] public SessionKeyInfo keyInfo=new();
    [ProtoMember(2)] public uint mapId;
    [ProtoMember(3)] public uint spotId;
    [ProtoMember(4)] public ulong generation;
}

[ProtoContract]
internal sealed class LocalCastleRaidHelpRequest
{
    [ProtoMember(1,IsRequired=true)] public SessionKeyInfo keyInfo=new();
    [ProtoMember(2)] public List<uint> targetUidVec=[];
    [ProtoMember(3)] public uint mapId;
    [ProtoMember(4)] public uint spotId;
    [ProtoMember(5)] public ulong generation;
}

[ProtoContract]
internal sealed class LocalCastleRaidHelpList
{
    [ProtoMember(1)] public List<uint> uidList=[];
    [ProtoMember(2)] public List<uint> eligibleUidList=[];
    [ProtoMember(3)] public ulong generation;
    [ProtoMember(4)] public uint hp;
    [ProtoMember(5)] public FriendRaidBattleType state;
    [ProtoMember(6)] public bool battleAvailable;
}

internal sealed partial class ProtocolRouter
{
    private const int CastleRaidHelpLimit=10; // The native MAX_HELP_REQUEST.

    private bool CastleRaidBotDeck(LocalBot bot,BscTable.CastleSpotInfo row,out List<ulong> cards)
    {
        var p=bot.Profile;
        var valid=CompetitiveRoster(p,DeckSlots(p,13),5,out cards);
        return valid&&cards.Count>0&&p.Level>=row.RequirePcLevel
            &&FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CASTLE_MONSTER_SUMMON);
    }

    private Reply CastleRaidHelpList(LocalPlayer p,LocalPlayer owner,BscTable.CastleSpotInfo row,
        LocalCastleRaid? raid,ulong generation,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(raid==null||(generation!=0&&generation!=raid.Generation))
            return fail("castle monster help generation changed",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
        var reply=new LocalCastleRaidHelpList
        {
            generation=raid.Generation,hp=raid.Hp,state=raid.State,
            battleAvailable=raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE
                ||raid.State==FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE&&!raid.Participants.Contains(p.Uid),
            uidList=raid.BotHelpers.Order().ToList()
        };
        if(raid.State==FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE&&CastleRaidConnected(owner,row)&&raid.BotHelpers.Count<CastleRaidHelpLimit)
            foreach(uint uid in p.Friends.Links.Keys.Order())
                if(!raid.Participants.Contains(uid)&&Bots.Find(uid) is {} bot&&CastleRaidBotDeck(bot,row,out _))
                    reply.eligibleUidList.Add(uid);
        return ok(reply);
    }

    private Reply CastleRaidHelp(LocalPlayer p,LocalPlayer owner,BscTable.CastleSpotInfo row,
        LocalCastleRaid? raid,BscTable.CardData boss,LocalCastleRaidHelpRequest help,
        Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(raid==null||help.generation==0||help.generation!=raid.Generation)
            return fail("castle monster help generation changed",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
        if(help.targetUidVec.Count is <1 or >CastleRaidHelpLimit||help.targetUidVec.Distinct().Count()!=help.targetUidVec.Count)
            return fail("castle monster help invalid selection",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
        var pending=help.targetUidVec.Where(uid=>!raid.BotHelpers.Contains(uid)).ToArray();
        if(pending.Length==0)return ok(null);
        if(raid.State!=FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE)
            return fail("castle monster phase changed or final battle requires owner",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
        if(!CastleRaidConnected(owner,row))
            return fail("castle monster requires its linked occupied castle",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
        if(raid.BotHelpers.Count+pending.Length>CastleRaidHelpLimit)
            return fail("castle monster help summon limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(store.All().Any(actor=>actor.ActiveStage!=null&&actor.Raids.ActiveMode==6
            &&actor.Raids.CastleOwner==owner.Uid&&actor.Raids.CastleMap==help.mapId
            &&actor.Raids.CastleSpot==help.spotId&&actor.Raids.CastleGeneration==raid.Generation))
            return fail("castle monster help active battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);

        var contributions=new List<(LocalPlayer Player,uint Damage)>();
        foreach(uint uid in pending)
        {
            var bot=p.Friends.Links.ContainsKey(uid)?Bots.Find(uid):null;
            if(bot==null||raid.Participants.Contains(uid)||!CastleRaidBotDeck(bot,row,out var cards))
                return fail("castle monster help friend unavailable",ServerErrorCode.ERROR_NOT_FRIEND);
            contributions.Add((bot.Profile,CastleRaidBotDamage(bot.Profile,cards,boss,row.RequirePcLevel)));
        }
        foreach(var contribution in contributions)
        {
            uint damage=Math.Min(raid.Hp,contribution.Damage);
            ApplyCastleRaidCooperation(raid,contribution.Player.Uid,damage,boss);
            raid.BotHelpers.Add(contribution.Player.Uid);
            RecordCastleRaidDamage(raid,contribution.Player,owner.Uid,help.mapId,help.spotId,boss,damage,
                contribution.Damage>=boss.DamageMin);
            if(raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE)break;
        }
        return ok(null);
    }

    // Resolve assistance from the actual deck, using the boss's original per-fight cap.
    // Once a slot meets the dungeon's reference level and stars it contributes its full share.
    private uint CastleRaidBotDamage(LocalPlayer bot,List<ulong> cards,BscTable.CardData boss,int requiredLevel)
    {
        double level=Math.Max(1,requiredLevel),rank=Math.Clamp(boss.Rank,1,6);
        double strength=cards.Sum(seq=>Math.Min(1,CardState(bot,seq).Level/level)
            *Math.Min(1,catalog.RequireCard(bot.CardIds[(int)seq-1]).Rank/rank));
        return (uint)Math.Clamp(Math.Round(boss.DamageMax*strength/5),1,Math.Max(1,boss.DamageMax));
    }

    private static void ApplyCastleRaidCooperation(LocalCastleRaid raid,uint uid,uint damage,BscTable.CardData boss)
    {
        raid.Participants.Add(uid);
        raid.Hp-=damage;
        if(raid.Hp!=0)return;
        raid.State=FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE;
        raid.Hp=(uint)boss.StatHpFinalBattle;
    }

    private void RecordCastleRaidDamage(LocalCastleRaid raid,LocalPlayer p,uint ownerId,uint mapId,uint spotId,
        BscTable.CardData boss,uint damage,bool won)
    {
        raid.History.Add(WireCodec.Serialize(new PKFriendRaidBattleHistory
        {
            battleTime=Now,battleResult=won?0u:1u,damageAmount=damage,bossId=(uint)boss.Id,
            mapId=mapId,spotId=spotId,castleOwnerId=ownerId,attackUserInfo=GuildThumbnail(p)
        }));
        if(raid.History.Count>50)raid.History.RemoveAt(0);
    }
}
