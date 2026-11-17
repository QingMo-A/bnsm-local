from pathlib import Path
r=Path('I:/BnsmModernWork')
def edit(path, old, new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:70]);p.write_text(s.replace(old,new),encoding='utf-8-sig')
edit(Path('Project/Assets/Scripts/Assembly-CSharp/LocalRankBattle.cs'),'    public static void StartArena(','''    private static LocalRankCombatRequest StartRequest(uint mode,uint target=0) {
        var request=new LocalRankCombatRequest{operation=1,mode=mode,targetUid=target};
        request.cards.AddRange(MonoSingleton<MainClient>.Instance.Player.GetDeck((USER_DECK_INDEX_TYPE)(mode==12?50:mode==31?57:52)).cardSeqList);return request;
    }
    public static void StartArena(''')
for old,new in [('new LocalRankCombatRequest{operation=1,mode=12,targetUid=target}','StartRequest(12,target)'),('new LocalRankCombatRequest{operation=1,mode=31,targetUid=target}','StartRequest(31,target)'),('new LocalRankCombatRequest{operation=1,mode=mode},delegate','StartRequest(mode),delegate')]:
 edit(Path('Project/Assets/Scripts/Assembly-CSharp/LocalRankBattle.cs'),old,new)
edit(Path('Project/Assets/Scripts/Assembly-CSharp/LocalClientCompetitiveProbe.cs'),'Dialog.CloseAllSpace();var p=MonoSingleton<MainClient>.Instance.Player;bool entrance=false;', '''Dialog.CloseAllSpace();var p=MonoSingleton<MainClient>.Instance.Player;
        foreach(int id in new[]{50,52,54})p.SetLocalDeck(new List<ulong>{1,2,3,4,5},(BscTable.USER_DECK_INDEX_TYPE)id);
        p.SetLocalDeck(System.Linq.Enumerable.Range(1,15).Select(x=>(ulong)x).ToList(),BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_INFINITE_ARENA_OFFENCE);
        bool entrance=false;''')
edit(Path('ServerBuildSource/LocalServer/CompetitiveArenaFeatures.cs'),'CompetitiveRoster(p,DeckSlots(p,deckId),mode==31?15:5,out var cards)||cards.Count<(mode==31?11:5)','CompetitiveRoster(p,rank.cards.Count>0?rank.cards:DeckSlots(p,deckId),mode==31?15:5,out var cards)||cards.Count<(mode==31?3:mode==12?1:5)')
edit(Path('ServerBuildSource/LocalServer/CompetitiveArenaFeatures.cs'),'BeginCompetitive(p,mode,enemy,cards);if(record.Used','p.Decks[deckId]=(rank.cards.Count>0?rank.cards:DeckSlots(p,deckId)).ToList();BeginCompetitive(p,mode,enemy,cards);if(record.Used')
edit(Path('Project/Assets/Scripts/Assembly-CSharp/BscDeckDialogPrivateSiegeCastleBattle.cs'),'\tpublic void StartBattle()\n\t{','''\tpublic void StartBattle()
    {
        if(LocalMode.Enabled) {
            var request=new LocalRankCombatRequest{operation=4,mode=18};request.cards.AddRange(MonoSingleton<MainClient>.Instance.Player.GetDeck(mDeckType).cardSeqList);
            MonoSingleton<ClientUpdate>.Instance.DoLocalRank(request,delegate(LocalRankCombatReply reply){StartLocalCastleBattle();},OnRequestFail);return;
        }
        StartLocalCastleBattle();
    }
    private void StartLocalCastleBattle()
    {''')
edit(Path('ServerBuildSource/LocalServer/CompetitiveArenaFeatures.cs'),'if(mode is not (10 or 12 or 31 or 42))','''if(mode==18&&rank.operation==4) {
                if(pending.ActiveMode!=18||pending.Started||p.ActiveStage!=null||!CompetitiveRoster(p,rank.cards,5,out var cards)||cards.Any(x=>pending.CastleDeadCards.Contains(x)))return Bad("castle start has no valid encounter and roster");
                p.Decks[54]=rank.cards.ToList();pending.Cards=cards;pending.Started=true;p.ActiveStage=0xF0000000u+18;p.ActiveBattleStamp=pending.Stamp;ConsumeBsBalls(p,cards);
                return ok(new LocalRankCombatReply{stamp=pending.Stamp,coins=Coins(p)});
            }
            if(mode is not (10 or 12 or 31 or 42))''')
edit(Path('ServerBuildSource/LocalServer/CompetitiveCastleFeatures.cs'),'if(!intrusion&&(mapId!=home.MapId||spot==null))','if(!intrusion&&(mapId!=home.MapId||spot==null||spot.Status==1))')
edit(Path('ServerBuildSource/LocalServer/CompetitiveCastleFeatures.cs'),'c.Spot!=spotId||end.cashSkip','c.Spot!=spotId||!c.Started||c.Action!=(uint)end.actionType||end.cashSkip')
edit(Path('ServerBuildSource/LocalServer/CompetitiveCastleFeatures.cs'),'PayCompetitiveCost(p,kinds,[0,0,0],costs);if(end.battleHistory?.useBsBallFlag==true)ConsumeBsBalls(p,cards);','PayCompetitiveCost(p,kinds,[0,0,0],costs);')
edit(Path('ServerBuildSource/LocalServer/CompetitiveFeatures.cs'),'if(c.ActiveMode==18){if(!CompetitiveRoster(p,DeckSlots(p,54),5,out var chosen))return Bad("invalid castle attack deck");c.Cards=chosen;}','if(c.ActiveMode==18&&!c.Started)return Bad("castle battle was not started");')
# Keep the protocol test focused on explicit start, never a fake preview settlement.
edit(Path('Verification/BotWorldTests/CompetitiveRegression.cs'),'await Replay(18);','''var castleStart=new LocalRankCombatRequest{operation=4,mode=18};castleStart.cards.AddRange(p.Decks[54]);await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,castleStart);await Replay(18);''')
# The native fixture alone seeds an explored neutral tile; protocol regression exercises exploration itself.
p=r/'Verification/BotWorldTests/CompetitiveRegression.cs';s=p.read_text(encoding='utf-8-sig');s=s.replace('p.Competitive.HomeMap=1;p.Competitive.Castles["1/1"]=new(){Map=1,Spot=1,WealthStamp=ProtocolRouter.Now};\n        ','');s=s.replace('if(runtime.Store.All().Count==0)Prepare(runtime);','if(runtime.Store.All().Count==0){Prepare(runtime);var p=runtime.Store.All().Single();p.Competitive.HomeMap=1;p.Competitive.Castles["1/1"]=new(){Map=1,Spot=1,WealthStamp=ProtocolRouter.Now};runtime.Store.Save();}');p.write_text(s,encoding='utf-8-sig')
