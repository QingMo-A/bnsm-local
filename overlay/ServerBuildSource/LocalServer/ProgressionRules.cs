/// <summary>Both debug edits and regular progression use the original cumulative EXP tables.</summary>
internal static class ProgressionRules
{
    // PlayerInfoEx.ExpMax always reads level+1, including at the playable cap.
    public static uint PlayerMax(GameCatalog c) => (uint)c.PlayerStates
        .Where(x=>x.Level<=99 && x.Level<=c.Numbers[BscTable.NUMBER_TYPE.MAX_PLAYER_LEVEL] &&
            x.Level<=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_PC_LEVEL) &&
            c.PlayerStates.Any(next=>next.Level==x.Level+1)).Max(x=>x.Level);
    public static uint PlayerFloor(GameCatalog c,uint level) => (uint)Math.Max(0,c.PlayerStates.First(x=>x.Level==level).Exp);
    public static int CardMax(GameCatalog c,LocalPlayer p,BscTable.CardData card) => Math.Min(
        c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_LEVEL),
        Math.Min((int)c.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_LEVEL],
            Math.Min(c.PlayerStates.First(x=>x.Level==p.Level).MaxCardLevel,c.CardRanks.First(x=>x.Rank==card.Rank).MaxLevel)));
    public static uint CardFloor(GameCatalog c,uint level) => (uint)Math.Max(0,c.CardLevels.First(x=>x.Level==level).Rank1);
    public static uint CardCeiling(GameCatalog c,int maxLevel)
    {
        var next=c.CardLevels.FirstOrDefault(x=>x.Level==maxLevel+1);
        return next==null ? CardFloor(c,(uint)maxLevel) : (uint)Math.Max(CardFloor(c,(uint)maxLevel),next.Rank1-1L);
    }
    public static uint CardLevel(GameCatalog c,uint exp,int maxLevel) => (uint)c.CardLevels
        .Where(x=>x.Level<=maxLevel && x.Rank1<=exp).Max(x=>x.Level);
    public static bool RepairLegacy(GameCatalog c,LocalPlayer p)
    {
        bool changed=false;
        if(p.Level>PlayerMax(c)){p.Level=PlayerMax(c);p.Exp=PlayerFloor(c,p.Level);changed=true;}
        // Earlier debug versions changed only Level. Keep the chosen level and repair its EXP floor.
        var row=c.PlayerStates.FirstOrDefault(x=>x.Level==p.Level);
        if(row!=null && p.Exp<row.Exp) {p.Exp=(uint)row.Exp;changed=true;}
        foreach(var state in p.CardStates.Values) {
            var cardRow=c.CardLevels.FirstOrDefault(x=>x.Level==state.Level);
            if(cardRow!=null && state.Exp<cardRow.Rank1) {state.Exp=(uint)cardRow.Rank1;changed=true;}
        }
        return changed;
    }
}
