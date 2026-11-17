internal static class CardIdentityPolicy
{
    // USE_CARD/CARD_HERO alone also includes old NPC/test templates. X=99 and
    // collection ID zero are not ordinary collectible hero variants.
    public static bool IsCollectible(BscTable.CardData card) =>
        card.Use==BscTable.CARD_USE.USE_CARD && card.Type==BscTable.CARD_TYPE.CARD_HERO && card.CollectionId>0 &&
        card.Talent is >= BscTable.CARD_TALENT.CARD_TALENT_C and <= BscTable.CARD_TALENT.CARD_TALENT_SSS &&
        !string.IsNullOrWhiteSpace(card.CardMini) && !string.IsNullOrWhiteSpace(card.CardSmall) && !string.IsNullOrWhiteSpace(card.Minimi);

    public static BscTable.CardData? Replacement(GameCatalog catalog,BscTable.CardData old) =>
        catalog.UsableHeroes.Where(c=>c.Rank==old.Rank && c.Class==old.Class && c.Minimi==old.Minimi)
            .OrderBy(c=>c.Talent).ThenBy(c=>c.Id).FirstOrDefault();

    public static List<LocalCardIdentityRepair> Plan(GameCatalog catalog,LocalPlayer player)
    {
        var result=new List<LocalCardIdentityRepair>();
        for(int i=0;i<player.CardIds.Count;i++) {
            uint id=player.CardIds[i];if(id==0)continue;
            var old=catalog.RequireCard(id);
            // Only repair definitively invalid X templates automatically.
            // Collection ID zero alone is insufficient reason to replace an S/SS card.
            if(old.Talent!=BscTable.CARD_TALENT.CARD_TALENT_X)continue;
            var replacement=Replacement(catalog,old);
            if(replacement==null || player.CardStates.GetValueOrDefault((ulong)i+1)?.Skin>0)continue;
            result.Add(new() {Sequence=(ulong)i+1,PreviousCardId=id,CardId=(uint)replacement.Id,
                PreviousNameAlias=old.NameAlias,NameAlias=replacement.NameAlias,Rank=old.Rank,Minimi=old.Minimi});
        }
        return result;
    }
}

internal sealed class LocalCardIdentityRepair
{
    public ulong Sequence {get;set;}
    public uint PreviousCardId {get;set;}
    public uint CardId {get;set;}
    public string PreviousNameAlias {get;set;}="";
    public string NameAlias {get;set;}="";
    public int Rank {get;set;}
    public string Minimi {get;set;}="";
}
