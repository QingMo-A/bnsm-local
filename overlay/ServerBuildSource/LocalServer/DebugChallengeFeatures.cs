internal sealed record DebugChallengeCount(string mode,string name,uint remaining,uint maximum);

internal sealed partial class ProtocolRouter
{
    // Called with Store.Gate held, just like all game mutations. Counters alone
    // are restored: boss HP, cleared tower areas, weekly rewards and journey claims survive.
    public DebugChallengeCount[] DebugChallengeCounts(LocalPlayer p)
    {
        var v=catalog.VipFor(p.VipExp);
        uint tower=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.CHALLENGE_TOWER_ENTER_DAILY_MAX_COUNT]);
        var result=new List<DebugChallengeCount>();
        string[] modes=["private-raid","world-boss","combo-boss","big-boss"];
        string[] names=["恶魔洞","血战","连招副本","魔王战"];
        for(int i=1;i<=4;i++)result.Add(new(modes[i-1],names[i-1],RaidLeft(p,i),RaidFree(p,i)+p.Raids.Bought.GetValueOrDefault(i)));
        result.Add(new("tower","挑战塔",(uint)Math.Max(0,(long)tower-p.ChallengeTower.UsedEntries),tower));
        result.Add(new("wander","闯荡江湖重置",WanderFreeLeft(p)+p.Wander.PaidLeft,(uint)Math.Max(0,v.WanderBattleEnterCount)+p.Wander.PaidLeft));
        return result.ToArray();
    }
    public string? DebugRestoreChallenges(LocalPlayer p,string mode)
    {
        string[] valid=["all","raids","private-raid","world-boss","combo-boss","big-boss","tower","wander","hard","legend","dungeons"];
        if(!valid.Contains(mode))return "挑战类型无效";
        if(p.ActiveStage!=null||p.Raids.ActiveStamp!=0||p.ChallengeTower.ActiveStamp!=0)return "请先结束当前战斗，再恢复次数";
        string[] modes=["private-raid","world-boss","combo-boss","big-boss"];
        for(int i=1;i<=4;i++)if(mode is "all" or "raids"||mode==modes[i-1]) {
            p.Raids.Used.Remove(i);p.Raids.Latest.Remove(i);
        }
        if(mode is "all" or "tower")p.ChallengeTower.UsedEntries=0;
        if(mode is "all" or "wander")p.Wander.Resets=0;
        if(mode is "all" or "hard")p.HardBattleCounts.Clear();
        if(mode is "all" or "legend")p.LegendBattleCounts.Clear();
        if(mode is "all" or "dungeons")p.HardDungeonBattleCounts.Clear();
        // Keep Bought/PaidLeft, purchase and paid-reset counters, boss HP, claims,
        // all record maps and receipts. A retry is a harmless assignment to zero.
        store.Save();return null;
    }
}
