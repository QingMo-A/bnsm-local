internal sealed partial class ProtocolRouter
{
    private void AbandonBattlesOnLogin(LocalPlayer player)
    {
        if (player.ActiveStage == null && player.ActiveBattleStamp == 0 && player.Raids.ActiveStamp == 0 &&
            player.Raids.ActiveMode == 0 && player.ChallengeTower.ActiveStamp == 0 && player.ChallengeTower.ActiveId == 0 &&
            player.Board.ActiveStamp == 0 && player.Resources.ActiveStamp == 0 &&
            player.Competitive.Stamp == 0 && player.Competitive.ActiveMode == 0)
            return;
        RememberBattleStamps(player);
        ClearRaidBattle(player);
        ClearTowerAttempt(player);
        ClearBoardAttempt(player);
        ClearResourceAttempt(player);
        ClearCompetitive(player);
        LocalBattleAttempts.Clear(player);
        store.Save();
    }

    private static void RememberBattleStamps(LocalPlayer player)
    {
        ulong last = Math.Max(player.ActiveBattleStamp, Math.Max(player.Raids.ActiveStamp, player.ChallengeTower.ActiveStamp));
        last = Math.Max(last, Math.Max(player.Board.ActiveStamp, Math.Max(player.Resources.ActiveStamp, player.Competitive.Stamp)));
        player.LastBattleStamp = Math.Max(player.LastBattleStamp, last);
    }

    private static void ClearBoardAttempt(LocalPlayer player)
    {
        LocalBattleAttempts.ClearMatching(player, player.Board.Node, player.Board.ActiveStamp);
        player.Board.ActiveStamp = 0;
    }

    private static void ClearTowerAttempt(LocalPlayer player)
    {
        var tower = player.ChallengeTower;
        LocalBattleAttempts.ClearMatching(player, tower.ActiveId, tower.ActiveStamp);
        tower.ActiveStamp = 0;
        tower.ActiveId = 0;
        tower.BattleCards.Clear();
    }

    private void AbandonRaidBattle(LocalPlayer p)
    {
        p.LastBattleStamp=Math.Max(p.LastBattleStamp,Math.Max(p.ActiveBattleStamp,p.Raids.ActiveStamp));
        ClearRaidBattle(p);store.Save();
    }
    private Reply RetreatBattle(LocalPlayer p,LocalRankCombatRequest request,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(request.mode==16&&p.Competitive.ActiveMode==16&&request.stamp!=0&&request.stamp==p.Competitive.Stamp&&p.ActiveBattleStamp==request.stamp)
        {
            ClearCompetitive(p);
            store.Save();
            return ok(new LocalRankCombatReply{stamp=request.stamp,coins=Coins(p)});
        }
        uint mode=p.Raids.ActiveMode switch{1=>2u,2 or 3=>5u,4=>33u,5=>32u,6=>3u,7=>4u,_=>0u};
        if(request.stamp==0||mode==0||mode!=request.mode||request.stamp!=p.Raids.ActiveStamp||request.stamp!=p.ActiveBattleStamp)
            return fail("stale battle retreat does not match the active attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        // Admission costs, cooldown, prior HP and journey progress remain intact.
        // Retreat never invents damage, a defeat, mission progress or a reward.
        AbandonRaidBattle(p);return ok(new LocalRankCombatReply{stamp=request.stamp,coins=Coins(p)});
    }
    // Returning to the native lobby abandons an unfinished attempt. The original
    // pause menu does not send a defeat, so do not invent casualties or rewards.
    private void AbandonWanderBattle(LocalPlayer p)
    {
        var r=p.Raids;if(r.ActiveMode!=5)return;
        p.LastBattleStamp=Math.Max(p.LastBattleStamp,r.ActiveStamp);
        LocalBattleAttempts.ClearMatching(p,r.ActiveBoss,r.ActiveStamp);
        r.ActiveMode=0;r.ActiveBoss=0;r.ActiveStamp=0;r.ActiveHp=0;r.ActiveLevel=0;r.BattleCards.Clear();r.BattleSideCards.Clear();r.RosterSnapshot=false;store.Save();
    }
    private void AbandonTowerBattle(LocalPlayer p)
    {
        var t=p.ChallengeTower;if(t.ActiveStamp==0&&t.ActiveId==0&&t.BattleCards.Count==0)return;
        p.LastBattleStamp=Math.Max(p.LastBattleStamp,t.ActiveStamp);
        ClearTowerAttempt(p);store.Save();
    }
    private static ulong NextLocalBattleStamp(LocalPlayer p)
    {
        RememberBattleStamps(p);
        return p.LastBattleStamp=Math.Max((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),checked(p.LastBattleStamp+1));
    }
}
