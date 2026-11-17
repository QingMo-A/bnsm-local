using System.Text.Json;

internal sealed partial class ProtocolRouter
{
    private int RankReserveLimit(uint mode)=>Competitive.BattleRules[(BscTable.GAME_MODE)mode].SideDeckCardNum;
    private bool ManualReserveReplayMatches(LocalPlayer player,PKBattleReplayData replay)
    {
        var counts=replay.battleCardInfo.Where(x=>x.cardSlotIndex>=5).GroupBy(x=>(x.teamIndex,x.cardId)).ToDictionary(g=>g.Key,g=>g.Count());
        foreach(var card in replay.battleCardInfo)if(card.teamIndex>1||card.uid!=(card.teamIndex==0?player.Uid:player.Competitive.Opponent))return false;
        foreach(var used in replay.battleProcessInfo.SelectMany(x=>x.battleSideDeckRet)){
            var card=used.battleCardInfo;if(card==null||used.targetTeamIndex>1||used.targetSlotIndex>=5||card.teamIndex!=used.targetTeamIndex||card.cardSlotIndex!=used.targetSlotIndex||card.uid!=(used.targetTeamIndex==0?player.Uid:player.Competitive.Opponent))return false;
            var key=(used.targetTeamIndex,card.cardId);if(counts.GetValueOrDefault(key)<=0)return false;counts[key]--;
            var original=catalog.FindCard(card.cardId);if(original==null||card.cardSpritPoint!=(uint)(original.MaxSp>0?original.MaxSp:100))return false;
        }
        return true;
    }
    // Assemble extra cards from the same immutable difficulty offer. Do not add
    // cards or equipment to the resident's saved profile or the cached template.
    private List<(LocalPlayer Player,ulong Sequence)> RankEnemyReserves(LocalPlayer p,uint mode,LocalPlayer enemy)
    {
        var offer=p.Competitive.Offers[mode].Single(x=>x.Uid==enemy.Uid);
        var result=new List<(LocalPlayer,ulong)>();
        var ancestors=enemy.Deck.Select(s=>catalog.RequireCard(enemy.CardIds[(int)s-1]).EvolveFirstAncestor).ToHashSet();
        for(int attempt=1;result.Count<RankReserveLimit(mode)&&attempt<=32;attempt++){
            var variant=JsonSerializer.Deserialize<LocalCompetitiveOffer>(JsonSerializer.Serialize(offer))!;
            variant.Seed=unchecked(offer.Seed+7919*attempt);
            var candidate=CompetitiveProfile(variant);
            foreach(var seq in candidate.Deck){
                var card=catalog.RequireCard(candidate.CardIds[(int)seq-1]);
                if(!ancestors.Add(card.EvolveFirstAncestor))continue;
                result.Add((candidate,seq));if(result.Count==RankReserveLimit(mode))break;
            }
        }
        if(result.Count!=RankReserveLimit(mode))throw new InvalidDataException("Original ranked reserve templates unavailable");
        return result;
    }
}
