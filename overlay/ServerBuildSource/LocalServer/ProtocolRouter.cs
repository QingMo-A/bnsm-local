using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

internal enum ServiceKind { Directory, Login, Game }
internal sealed class ConnectionState
{
    public LocalPlayer? Player;
    public uint Sequence;
    public ulong LastBotNotice;
    public Dictionary<uint,string> MissionVersions = [];
}
internal sealed record Reply(ushort Command, object? Message, string Name);

internal sealed partial class ProtocolRouter
{
    private readonly PlayerStore store;
    private readonly GameCatalog catalog;
    private readonly int loginPort, gamePort;
    private readonly string advertisedAddress;
    private readonly uint advertisedIPv4;
    private readonly Dictionary<ServiceKind, Dictionary<ushort, string>> commands;
    private readonly Dictionary<string, Type> types;
    private readonly ConcurrentDictionary<string, uint> sessions = new();
    private readonly ConcurrentDictionary<string, int> observed = new();
    private readonly string[] notices;
    private readonly AmbientWorldChat worldChat;
    internal readonly LocalBotWorld Bots;
    private readonly string RootDirectory;
    public static ulong Now => (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    // PlayerInfoEx converts this protocol field with DateTime.AddMinutes.
    private const uint ServerTimeZoneMinutes = 8 * 60;
    public ProtocolRouter(string root, PlayerStore store, GameCatalog catalog, int loginPort, int gamePort, System.Net.IPAddress? advertisedAddress=null)
    {
        advertisedAddress ??= System.Net.IPAddress.Loopback;
        this.advertisedAddress=advertisedAddress.ToString();
        advertisedIPv4=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(advertisedAddress.GetAddressBytes());
        this.store = store; this.catalog = catalog; this.loginPort = loginPort; this.gamePort = gamePort;
        RootDirectory = root;
        notices = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/notices.json")))!;
        Bots=new(root,store.DirectoryPath,catalog,store.All());
        worldChat=new(root,store.DirectoryPath,catalog,actors:Bots.Actors);
        Dictionary<ushort, string> EnumMap<T>() where T : struct, Enum => Enum.GetValues<T>().ToDictionary(x => Convert.ToUInt16(x), x => x.ToString());
        commands = new() { [ServiceKind.Directory] = EnumMap<DirServerCommand>(),
            [ServiceKind.Login] = EnumMap<LoginServerCommand>(), [ServiceKind.Game] = EnumMap<GameServerCommand>() };
        var assembly = typeof(SessionKeyInfo).Assembly;
        types = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/protocol-types.json")))!
            .Select(name => assembly.GetType("USER_PACKET." + name))
            .Where(t => t != null && t.IsClass).ToDictionary(t => Normalize(t!.Name), t => t!);
        types["GSBUYINGAMESHOPGOODSREQUEST"]=typeof(LocalShopBuyRequest);
        types["GSGETUSERCARDLISTREQUEST"] = typeof(GsGetUserCardListRquest);
        types["LSCHECKVALIDACCOUNTREQUEST"] = typeof(LsCheckAccountRequest);
        types["GSGETINFINITARENADECKINFOREQUEST"] = typeof(GsGetInfiniteArenaUserDeckInfoRequest);
        types["GSGETREWARDLISTREQUEST"] = typeof(GsGetRewardInfoRequest);
        types["GSRECEIVEREWARDREQUEST"] = typeof(GsReceiveRewardItemDataRequest);
        types["GSGETCURRENTPACKETSEQREQUEST"] = typeof(GsGetCurrentPacketSequenceRequest);
        types["GSGETINVENTORYITEMLISTREQUEST"] = typeof(GsGetUserItemListRquest);
        foreach (var pair in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(
            File.ReadAllText(Path.Combine(root,"LocalServer/data/request-types.json")))!)
        {
            var type = assembly.GetType("USER_PACKET." + pair.Value)
                ?? throw new InvalidDataException("Original request type missing: " + pair.Value);
            types[Normalize(pair.Key)] = pair.Key=="GS_BUY_INGAME_SHOP_GOODS_REQUEST"?typeof(LocalShopBuyRequest):type;
        }
        types[Normalize("GS_BUY_PRIVATE_RAID_PLAY_COUNT_REQUEST")]=typeof(GsBuyPrivateRaidCountRequest);
        types[Normalize("GS_BUY_WORLD_BOSS_PLAY_COUNT_REQUEST")]=typeof(GsBuyWorldBossCountRequest);
        types[Normalize("GS_BUY_BIG_BOSS_PLAY_COUNT_REQUEST")]=typeof(GsBuyBigBossCountRequest);
        types[Normalize("GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST")]=typeof(GsGetMyGuildUserInfoListRequest);
        types[Normalize("GS_GET_GUILD_INVITED_LIST_REQUEST")]=typeof(GsGetGuildInvitedListRequest);
        types[Normalize("GS_GET_GUILD_BATTLE_USED_VOLUNTEER_LIST_REQUEST")]=typeof(GsGetGuildBattleVolunteerListRequest);
        types[Normalize("GS_GET_GUILD_BATTLE_VOLUNTEER_CARD_BATTLE_INFO_REQUEST")]=typeof(GsGetVolunteerCardDetailInfoRequest);
        types[Normalize("GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST")]=typeof(GsRecvGuildAttendanceRewardRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_GUILD_DETAIL_REQUEST")]=typeof(GsGetRankingBoardGuildDetailInfoRequest);
        types[Normalize("GS_GET_GUILD_RVR_BATTLE_LOBBYINFO_REQUEST")]=typeof(GsGetGuildRvRBattleLobbyInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_RANK_BATTLE_DETAIL_REQUEST")]=typeof(GsGetRankingBoardRankBattleDetailInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_BALANCE_BATTLE_DETAIL_REQUEST")]=typeof(GsGetRankingBoardBalanceBattleDetailInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_CHAIN_BATTLE_DETAIL_REQUEST")]=typeof(GsGetRankingBoardChainBattleDetailInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_TOP_RANKER_REQUEST")]=typeof(GsGetRankingBoardTopRankerRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_COMMON_DETAIL_REQUEST")]=typeof(GsGetRankingBoardCommonDetailInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_ARENA_BATTLE_DETAIL_REQUEST")]=typeof(GsGetRankingBoardArenaBattleDetailInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_WORLD_BOSS_DETAIL_REQUEST")]=typeof(GsGetRankingBoardWorldBossInfoRequest);
        types[Normalize("GS_GET_RAINKING_BOARD_WANDER_BATTLE_DETAIL_REQUEST")]=typeof(GsGetRankingBoardWanderBattleDetailInfoRequest);
        types[Normalize("GS_GET_FRIEND_LIST_REQUEST")]=typeof(GsGetFriendListRequest);
        types[Normalize("GS_GET_INVITE_FRIEND_LIST_REQUEST")]=typeof(GsGetInviteFriendListRequest);
        types[Normalize("GS_LBS_FRIEND_REQUEST")]=typeof(GsLBSFriendListRequest);
        types[Normalize("GS_GET_DEBUG_PROMOTION_DATA_REQUEST")]=typeof(GsGetDebugPromotionInfoRequest);
        types[Normalize("GS_CARD_RESET_REQUEST")]=typeof(LocalCardResetRequest);
        types[Normalize("GS_EXTRACT_EQUIPMENT_ITEM_REQUEST")]=typeof(LocalEquipmentRequest);
        types[Normalize("GS_GET_RANK_BATTLE_RECORD_REQUEST")]=typeof(LocalRankCombatRequest);
        types[Normalize("GS_CHAIN_BATTLE_END_CONFIRM_REQUEST")]=typeof(LocalChainBattleEndRequest);
        types[Normalize("GS_BUY_CHAIN_BATTLE_BUFF_REQUEST")]=typeof(LocalChainBattleBuffRequest);
        types[Normalize("GS_WANDER_BATTLE_START_REQUEST")]=typeof(LocalWanderStartRequest);
        types[Normalize("GS_FRIEND_RAID_HELP_SEND_FRIENDS_LIST_REQUEST")]=typeof(LocalCastleRaidHelpListRequest);
        types[Normalize("GS_FRIEND_RAID_HELP_REQUEST")]=typeof(LocalCastleRaidHelpRequest);
    }
    private static string Normalize(string text) => text.Replace("_", "").ToUpperInvariant();
    private Type? MessageType(string command) => types.GetValueOrDefault(Normalize(command));
    private static object? Field(object? value, string name) => value?.GetType().GetField(name)?.GetValue(value);
    private PKGameServerStatus ServerStatus() => new() { serverId = 1, serverName = "本地剑灵", clientVersion = "1.97.0.0" };
    private PKCoinInfo Coins(LocalPlayer p) => new() { gold = p.Gold, cashPoint = p.CashPoint, worldBossPoint=p.WorldBossPoint,guildToken=p.GuildTokens,arenaPoint=p.ArenaPoint,rankingPoint=p.RankingPoint };
    private PKUserNameCardInfo NameCard(LocalPlayer p)
    {
        var state = CardState(p,p.LeaderSequence);
        return new() {
            uid=p.Uid,guildName=MyGuild(p)?.Name??"",exp=p.Exp,helperID=(BscTable.HELPER_TYPE)p.HelperId,helperCostumeId=HelperList(p).First(x=>x.helperId==p.HelperId).costumeId,
            newbieFlag=p.NewbieFlag,vipExp=p.VipExp,introMsg=p.Intro,feeling=p.Feeling,
            avatarCardId=p.CardIds[(int)p.LeaderSequence-1],leaderCardInfo=new PKUserLeaderCardInfo {
                cardID=p.CardIds[(int)p.LeaderSequence-1],cardSeq=p.LeaderSequence,cardLevel=state.Level,cardExp=state.Exp,cardSkinId=state.Skin },
            commonInfo=new PKUserCommonInfo {titleId=SelectedTitle(p),level=p.Level,account=p.Account,nick=p.Nickname,vipGrade=(uint)catalog.VipFor(p.VipExp).VipLv}
        };
    }
    private Reply Response(ServiceKind service, ushort request, string requestName, object? value, bool failure = false)
    {
        string name = requestName.Replace("_REQUEST", failure ? "_FAIL" : "_OK");
        var entry = commands[service].FirstOrDefault(x => x.Value == name);
        if (entry.Value == null) throw new InvalidDataException($"No response command for {requestName}");
        return new(entry.Key, value, name);
    }
    public Reply Handle(ServiceKind kind, ushort command, byte[] payload, ConnectionState connection)
    {
        string name = commands[kind].GetValueOrDefault(command, $"UNKNOWN_{command}");
        observed.AddOrUpdate(kind + "/" + name, 1, (_, count) => count + 1);
        if (!name.EndsWith("_REQUEST")) throw new InvalidDataException($"Expected request, received {name}");
        var requestType = MessageType(name);
        object? request = WireCodec.Deserialize(requestType, payload);
        Reply Ok(object? result = null) {
            if(kind==ServiceKind.Game&&connection.Player is {} current){ObserveBsBallSuccess(current,request,result);ObserveMissionSuccess(current,request,result);}
            return Response(kind,command,name,result);
        }
        Reply Fail(string reason, ServerErrorCode code=ServerErrorCode.ERROR_UNKNOWN_COMMAND) => Response(kind, command, name, LocalFailureMessages.Create(name,reason,code), true);
        if (name.EndsWith("_PING_REQUEST")) return Response(kind,command,name,null);
        lock (store.Gate)
        {
            using var saveBatch = store.BatchSaves();
            SettlementTick();
            if (kind == ServiceKind.Directory)
            {
                switch (name)
                {
                    case "DS_GET_SERVER_LIST_REQUEST":
                        var result = new DsGetServerListOk { info = new PKLoginServerConnectionInfo
                        { loginServerAddress = advertisedAddress, loginServerVirtualPort = (uint)loginPort },
                            lastPlayServerStatus = ServerStatus() };
                        result.list.Add(ServerStatus()); return Ok(result);
                    case "DS_GET_CLIENT_VERSION_REQUEST": return Ok(new DsGetClientVersionOk { version = "1.97.0.0" });
                    case "DS_GET_SERVER_STATUS_REQUEST": return Ok(new DsGetServerStatusOk { serverStatus = ServerStatus() });
                    case "DS_GET_TUTORIAL_STATUS_REQUEST": return Ok(new DsGetTutorialStatusOk { tutorialFlag = uint.MaxValue });
                    case "DS_UPDATE_TUTORIAL_STATUS_REQUEST":
                    case "DS_SET_AUTH_POP_UP_FLAG_REQUEST": return Ok();
                    default: return Fail("directory operation not implemented: " + name);
                }
            }
            if (kind == ServiceKind.Login)
            {
                switch (name)
                {
                    case "LS_VERSION_CHECK_REQUEST": return Ok();
                    case "LS_CHECK_VALID_ACCOUNT_REQUEST":
                    case "LS_CREATE_ACCOUNT_REQUEST":
                    case "LS_LOGIN_REQUEST":
                        var account = Field(request, "userID") as string;
                        if (string.IsNullOrWhiteSpace(account)) account = Field(request, "openId") as string;
                        if (string.IsNullOrWhiteSpace(account)) return Fail("local account is empty");
                        connection.Player = store.GetOrCreate(account);
                        return name == "LS_LOGIN_REQUEST" ? Ok(new LsLoginOk { lastPlayServerStatus = ServerStatus() }) : Ok();
                    case "LS_GET_SERVER_LIST_REQUEST":
                        var list = new LsGetServerListOk(); list.list.Add(ServerStatus()); return Ok(list);
                    case "LS_GET_RANDOM_NAME_REQUEST": return Ok(new LsGetRandomNameOk { randomName = "本地剑灵" });
                    case "LS_GET_GAME_SETTING_REQUEST": return Ok(new LsGetGameSettingOk());
                    case "LS_GET_ACTIVE_GAME_SERVER_IP_REQUEST": return Ok(new LsGetActiveGameServerIpOk
                    { gameServerAddress = advertisedAddress, gameServerIP = advertisedIPv4, gameServerPort = (uint)gamePort, gameServerVirtualPort = (uint)gamePort });
                    case "LS_CREATE_NICK_NAME_REQUEST":
                        if (connection.Player == null) return Fail("login is required");
                        var nick = Field(request, "nickName") as string;
                        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 24) return Fail("invalid local nickname");
                        connection.Player.Nickname = nick; store.Save(); return Ok();
                    case "LS_GAME_START_REQUEST":
                        if (connection.Player == null) return Fail("login is required");
                        // Re-entering after closing/crashing the client abandons
                        // the unfinished battle; no rewards or extra stamina debit.
                        AbandonBattlesOnLogin(connection.Player);
                        var key = RandomNumberGenerator.GetHexString(32);
                        sessions[key] = connection.Player.Uid;
                        return Ok(new LsGameStartOk { uid = connection.Player.Uid, haveNick = 1,
                            loginSessionKey = key, nickName = connection.Player.Nickname,
                            gameServerAddress = advertisedAddress, gameServerIP = advertisedIPv4,
                            gameServerPort = (uint)gamePort, gameServerVirtualPort = (uint)gamePort });
                    default: return Fail("login operation not implemented: " + name);
                }
            }
            // Original packets use both spellings, including monster summon.
            var session = (Field(request, "keyInfo")??Field(request, "KeyInfo")) as SessionKeyInfo;
            if (session == null || !sessions.TryGetValue(session.loginSessionKey ?? "", out uint uid) || uid != session.uid)
                return Fail("invalid local session",ServerErrorCode.ERROR_INVALID_SESSION_KEY);
            var player = store.Find(uid);
            if (player == null) return Fail("local profile not found");
            connection.Player = player;player.LastActivity=Now;
            RefreshTime(player);
            RefreshChainBattle(player);
            RefreshTower(player);
            RefreshRaids(player);RefreshWander(player);
            RefreshGuilds();RefreshMissions(player);RefreshBoardResources(player);RefreshActivities(player);
            if(request is LocalRankCombatRequest retreat&&retreat.operation==10)
                return FeatureReceipt(player,player.RaidReceipts,name,session,payload,()=>RetreatBattle(player,retreat,value=>Ok(value),(reason,code)=>Fail(reason,code)),(reason,code)=>Fail(reason,code));
            if (request is LocalRankCombatRequest { operation: LocalStoryChapter.ProgressOperation })
                return Ok(StoryProgress(player));
            var chainReply=HandleChainBattle(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(chainReply!=null)return chainReply;
            var competitiveReply=HandleCompetitive(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(competitiveReply!=null)return competitiveReply;
            var ballReply=HandleBsBall(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(ballReply!=null)return ballReply;
            var factionReply=HandleFaction(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(factionReply!=null)return factionReply;
            var activityReply=HandleActivities(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(activityReply!=null)return activityReply;
            var boardReply=HandleBoard(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(boardReply!=null)return boardReply;
            var resourceReply=HandleResource(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(resourceReply!=null)return resourceReply;
            if((player.Board.ActiveStamp!=0||player.Resources.ActiveStamp!=0)&&name=="GS_NORMAL_BATTLE_END_CONFIRM_REQUEST")return Fail("active special-mode battle cannot settle as adventure",ServerErrorCode.ERROR_INVALID_ID);
            var rankingReply=HandleRanking(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(rankingReply!=null)return rankingReply;
            var missionReply=HandleMissions(name,request,player,connection,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(missionReply!=null)return missionReply;
            var friendsReply=HandleFriends(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(friendsReply!=null)return friendsReply;
            var guildReply=HandleGuild(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(guildReply!=null)return guildReply;
            var raidReply=HandleRaids(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(raidReply!=null)return raidReply;
            var friendRaidReply=HandleCastleRaid(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(friendRaidReply!=null)return friendRaidReply;
            var wanderReply=HandleWander(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(wanderReply!=null)return wanderReply;
            var polishReply=HandlePolish(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(polishReply!=null)return polishReply;
            var transcendenceReply=HandleTranscendence(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(transcendenceReply!=null)return transcendenceReply;
            var towerReply=HandleChallengeTower(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(towerReply!=null)return towerReply;
            var profileReply=HandleProfile(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(profileReply!=null)return profileReply;
            var innReply=HandleInn(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(innReply!=null)return innReply;
            var evilReply=HandleEvilWar(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(evilReply!=null)return evilReply;
            var trainingReply=HandleTraining(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(trainingReply!=null)return trainingReply;
            var helperReply=HandleHelperGifts(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(helperReply!=null)return helperReply;
            var lootReply=HandleLoot(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(lootReply!=null)return lootReply;
            var batchEquipmentReply=HandleBatchEquipment(name,request,player,session,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(batchEquipmentReply!=null)return batchEquipmentReply;
            var engravingReply=HandleEngraving(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(engravingReply!=null)return engravingReply;
            var craftingReply=HandleCrafting(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(craftingReply!=null)return craftingReply;
            var adventureReply=HandleAdventure(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(adventureReply!=null)return adventureReply;
            var commerceReply=HandleCommerceWithReceipt(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(commerceReply!=null)return commerceReply;
            var shopReply=HandleShopWithReceipt(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(shopReply!=null)return shopReply;
            var equipmentReply=HandleEquipment(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(equipmentReply!=null)return equipmentReply;
            var chatReply=HandleChat(name,request,player,session,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(chatReply!=null)return chatReply;
            var mailReply=HandleMailbox(name,request,player,session,payload,value=>Ok(value),(reason,code)=>Fail(reason,code));
            if(mailReply!=null)return mailReply;
            var progressionReply = HandleProgressionWithReceipt(name,request,player,session,payload,
                value=>Ok(value),(reason,code)=>Fail(reason,code));
            if (progressionReply != null) return progressionReply;
            var featureReply = HandleLocalFeature(name, request, player, session,
                value => Ok(value), (reason, code) => Response(kind, command, name,
                    new FailPacket { errorCode = (uint)code, message = "Local server: " + reason }, true));
            if (featureReply != null) return featureReply;
            // 签到、短期签到与运营活动：早于下面的 switch 拦截，switch 里不再保留空实现。
            var attendanceReply = HandleAttendance(name, request, player,
                value => Ok(value), (reason, code) => Response(kind, command, name,
                    new FailPacket { errorCode = (uint)code, message = "Local server: " + reason }, true));
            if (attendanceReply != null) return attendanceReply;
            switch (name)
            {
                case "GS_GET_CURRENT_PACKET_SEQ_REQUEST": return Ok(new GsGetCurrentPacketSequenceOk { currentPacketSequence = session.sequenceNo });
                case "GS_GET_USER_GAME_INFO_REQUEST": return Ok(GameInfo(player));
                case "GS_SAVE_USER_SETTING_REQUEST":
                    player.UserSettings = ((GsSaveUserSettingRequest)request!).settings;
                    store.Save();
                    return Ok();
                case "GS_GET_USER_COIN_INFO_REQUEST": return Ok(new GsGetUserCoinInfoOk { coinInfo = Coins(player), nameCardInfo = NameCard(player) });
                case "GS_GET_USER_CARD_LIST_REQUEST":
                    var cards = new GsGetUserCardListOk { totalDataCount = (uint)player.CardIds.Count(x=>x!=0), dataCount = (uint)player.CardIds.Count(x=>x!=0) };
                    for (int i = 0; i < player.CardIds.Count; i++)
                    {
                        if(player.CardIds[i]!=0) cards.userCardInfoList.Add(CardInfo(player.CardIds[i], (ulong)i + 1,player));
                    }
                    return Ok(cards);
                case "GS_GET_USER_DECK_LIST_REQUEST":
                    var decks = new GsGetUserDeckListOk();
                    foreach (BscTable.USER_DECK_INDEX_TYPE id in Enum.GetValues<BscTable.USER_DECK_INDEX_TYPE>())
                    {
                        if ((uint)id == 0 || id.ToString().EndsWith("MAX")) continue;
                        var slots=DeckSlots(player,(uint)id);
                        decks.userDeckList.Add(new PKUserDeckInfo { deckID = id,
                            cardSlot1 = slots.ElementAtOrDefault(0), cardSlot2 = slots.ElementAtOrDefault(1),
                            cardSlot3 = slots.ElementAtOrDefault(2), cardSlot4 = slots.ElementAtOrDefault(3), cardSlot5 = slots.ElementAtOrDefault(4) });
                    }
                    return Ok(decks);
                case "GS_UPDATE_USER_DECK_LIST_REQUEST":
                    var change = (GsUpdateUserDeckListRequest)request!;
                    if(change.userDeckList.Count==0 || change.userDeckList.Select(x=>x.deckID).Distinct().Count()!=change.userDeckList.Count)
                        return Fail("invalid deck list",ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE);
                    foreach(var deck in change.userDeckList)
                    {
                        var slots = new[] { deck.cardSlot1, deck.cardSlot2, deck.cardSlot3, deck.cardSlot4, deck.cardSlot5 };
                        if(!Enum.IsDefined(deck.deckID) || (uint)deck.deckID==0 || deck.deckID.ToString().EndsWith("MAX") ||
                           slots.Where(x=>x!=0).Distinct().Count()!=slots.Count(x=>x!=0) || slots.Any(x=>x!=0 && (!Owns(player,x)||((uint)deck.deckID!=10&&InTraining(player,x)))))
                            return Fail("invalid local deck",ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE);
                    }
                    foreach(var deck in change.userDeckList) {
                        var slots=new List<ulong> {deck.cardSlot1,deck.cardSlot2,deck.cardSlot3,deck.cardSlot4,deck.cardSlot5};
                        if((uint)deck.deckID==1) player.Deck=slots;else player.Decks[(uint)deck.deckID]=slots;
                    }
                    store.Save();return Ok();
                case "GS_GET_INVENTORY_ITEM_LIST_REQUEST":
                    var inventoryRequest=(GsGetUserItemListRquest)request!;
                    if(!Enum.IsDefined(inventoryRequest.reqType)) return Fail("invalid inventory filter",ServerErrorCode.ERROR_INVALID_ID);
                    var inventory=new GsGetUserItemListOk();
                    if((uint)inventoryRequest.reqType!=1) inventory.UserItemList.AddRange(player.Inventory.Where(x=>x.Value>0).OrderBy(x=>x.Key).Select(x=>ItemInfo(x.Key,x.Value)));
                    if((uint)inventoryRequest.reqType!=2) inventory.UserItemList.AddRange(player.EquipmentItems.Select(x=>EquipmentInfo(player,x)));
                    inventory.totalDataCount=inventory.dataCount=(uint)inventory.UserItemList.Count;return Ok(inventory);
                // Explicit empty local services used by ClientStartup's login chain.
                case "GS_GET_CASH_BALANCE_REQUEST": return Ok(new GsGetCashBalanceOk
                { coinInfo = Coins(player), nameCardInfo = NameCard(player), paidAccountInfo = new PKPaidAccountInfo() });



                case "GS_GET_INFINIT_ARENA_DECK_INFO_REQUEST":
                    var infinite = new PKInfiniteArenaUserDeckInfo();
                    infinite.userDeck.AddRange(player.Deck); infinite.userDeck.AddRange(new ulong[10]);
                    infinite.formationId.AddRange(new uint[3]);
                    return Ok(new GsGetInfiniteArenaUserDeckInfoOk { userDeckInfo = infinite });
                case "GS_GET_WORLD_TOURNAMENT_DECK_INFO_REQUEST":
                    var tournament = new GsGetWorldTournamentDeckInfoOk();
                    tournament.firstDeckCardSeq.AddRange(player.Deck);
                    tournament.secondDeckCardSeq.AddRange(new ulong[5]); tournament.thirdDeckCardSeq.AddRange(new ulong[5]);
                    return Ok(tournament);
                case "GS_GET_HARDMODE_BATTLE_COUNT_REQUEST":
                    // 原客户端用 BattleStageLink.RemainBattleCount = DailyLimit - 用掉的次数，
                    // 所以这里只要如实回报当日已用次数即可。
                    var hardCounts = new GsGetHardModeBattleCountOk();
                    foreach (var used in player.HardBattleCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))
                        hardCounts.stageBattleCount.Add(new PKBattleCountInfo { stageid=used.Key, battleCount=used.Value });
                    foreach (var used in player.LegendBattleCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))
                        hardCounts.stageLegendBattleCount.Add(new PKBattleCountInfo { stageid=used.Key, battleCount=used.Value });
                    foreach (var used in player.HardBattleResetCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))
                        hardCounts.stageResetCount.Add(new PKBattleCountInfo { stageid=used.Key, battleCount=used.Value });
                    foreach (var used in player.LegendBattleResetCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))
                        hardCounts.stageLegendResetCount.Add(new PKBattleCountInfo { stageid=used.Key, battleCount=used.Value });
                    foreach(var used in player.HardDungeonBattleCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))hardCounts.dungeonBattleCount.Add(new PKBattleCountInfo {stageid=used.Key,battleCount=used.Value});
                    foreach(var used in player.HardDungeonResetCounts.Where(x=>x.Value>0).OrderBy(x=>x.Key))hardCounts.dungeonResetCount.Add(new PKBattleCountInfo {stageid=used.Key,battleCount=used.Value});
                    return Ok(hardCounts);
                case "GS_RESET_HARDMODE_BATTLE_VIP_ENTRANCE_COUNT_REQUEST":
                    return ResetHardModeEntry(player, (GsResetHardModeBattleVIPEntranceCountRequest)request!,
                        value => Ok(value), (reason, code) => Response(kind, command, name,
                            new FailPacket { errorCode = (uint)code, message = "Local server: " + reason }, true));
                case "GS_GET_MY_HELPER_LIST_REQUEST":
                    var helpers=new GsGetMyHelperListOk();helpers.helperInfoList.AddRange(HelperList(player));return Ok(helpers);
                // The standard PvE deck page fetches these even before any
                // formation has been purchased. An empty ownership list is valid.
                case "GS_UPDATE_EPISODE_CUTSCENE_ID_REQUEST":
                    var cutscene = (GsUpdateEpisodeCutSceneIDRequest)request!;
                    player.ClearEpisodeCutScene = Math.Max(player.ClearEpisodeCutScene, cutscene.cutSceneID);
                    player.ClearHardEpisodeCutScene = Math.Max(player.ClearHardEpisodeCutScene, cutscene.hardCutSceneID);
                    player.ClearLegendEpisodeCutScene = Math.Max(player.ClearLegendEpisodeCutScene, cutscene.legendCutSceneID);
                    store.Save(); return Ok();
                case "GS_GET_SYSTEM_NOTICE_REQUEST":
                    var notice = new GsGetSystemNoticeOk(); notice.noticeList.AddRange(notices); return Ok(notice);
                case "GS_UPDATE_TUTORIAL_STATUS_REQUEST":
                    player.TutorialStatus = ((GsUpdateTutorialStatusRequest)request!).tutorialStatus; store.Save(); return Ok();
                case "GS_CHECK_GAME_ENTRY_REQUEST": return Ok(new GsCheckGameEntryOk
                { serverTime = Now, serverTimeZone = ServerTimeZoneMinutes, pollingDataRequest = true,userPollingData=BotPolling(connection),userCheckGameEntryData=MailEntry(player) });
                default: return Fail("gameplay operation not implemented: " + name);
            }
        }
    }
    public object Status() => new { observedRequests = observed.OrderBy(x => x.Key),
        originalCardCount = catalog.Cards.Count, normalStageCount = catalog.Stages.Count,
        localFeatures = new[] { "announcements", "tutorial selection", "simulated recharge", "single/ten card draws", "persistent local progress", "normal adventure progression", "persistent card progression", "original stamina recovery", "multipart inventory", "collectible card identity", "ambient world chat", "contextual local world chat", "bounded request persistence", "equipment debug growth and challenge refill", "persistent skill upgrades", "card enchant and evolution", "four-star gacha pool", "mixed gacha rewards", "native local recharge", "monthly and short-term attendance", "operation event switches", "diamond gold exchange", "vip privilege packages", "full item shop", "material and VIP gacha", "gacha previews and tickets", "persistent equipment and gems", "versioned save profiles", "fragment summons and chapter rewards", "hidden adventure stages", "equipment refinement awakening and gem crafting", "box opening stage loot and sweeps", "persistent user settings", "separate native shops and bulk purchases", "persistent helper gifts", "persistent debug progression and original item sales", "persistent original card training", "persistent infinite challenge", "persistent inn dice game", "persistent profile frames rename and experience shop", "persistent equipment extraction and engraving", "recursive upgrade material composition", "persistent original challenge tower", "restored Pizidao collectible resources", "persistent original transcendence", "persistent native polish systems", "persistent debug growth rules", "atomic batch equipment extraction", "persistent native raid battles", "persistent original wander journey", "persistent local guild management", "persistent local friends", "persistent local ranking boards", "native mission title rewards", "persistent faction bot battles", "overflow-safe mission stamina rewards", "native friend bot duels" },
        collectibleCardCount=catalog.UsableHeroes.Count(),repairPlaceholderCards=catalog.RepairPlaceholderCards,
        limitations = new[] { "Normal PvE uses original content and declared local drops where server probabilities are absent; CardRewardExp becomes shared growth points; wins remain client-reported.", "Gacha uses declared local pools, not original server probabilities. Recharge is a local simulation. Real payments, equipment creation from scratch, equipment runes, PvP, online guild wars, guild presents are not implemented; playable local activities use declared custom goals and rewards, while operation switches only drive client drop-bonus labels.", "Inn entry and solo dice are restored; multiplayer servant capture, friend raids require further implementation; local friends, stamina gifts and private chat are restored. Card protection has a server endpoint; its original APK client job is empty and still needs UI wiring." } };
}
