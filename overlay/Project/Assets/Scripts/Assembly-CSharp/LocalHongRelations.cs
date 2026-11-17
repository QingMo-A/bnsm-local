#if BNSM_SERVER
extern alias game;
using game::BscTable;
using static game::BscTable.EQEFFECT_TYPE;
#else
using BscTable;
using static BscTable.EQEFFECT_TYPE;
#endif
using System;
using System.Collections.Generic;

internal static class LocalHongRelations
{
    private static readonly int[] Damage = {24,38,56,80,106,166,215,273,297,323,352,383,417,454,494,538};
    private static readonly int[] Force = {17,28,44,63,85,133,170,214,233,267,301,337,374,411,443,478};
    private static readonly int[] Critical = {11,19,28,37,45,59,67,76,82,94,106,118,130,143,154,166};
    private static readonly int[] Hit = {14,24,34,44,54,72,82,92,100,115,129,144,159,174,187,201};
    private static readonly int[] Penetration = {18,21,26,33,41,54,62,69,75,86,97,108,119,130,140,151};
    private static readonly int[] Resist = {10,10,10,10,10,10,10,10,20,20,20,20,20,20,20,20};

    internal static IEnumerable<RelationData> Create()
    {
        yield return Bond(1900045001, "初心！", "洪门的传承，也是最后的试炼。",
            new[] {30181, LocalHongCard.FirstCardId, 30081},
            (EQEFFECT_ADD_DAMAGE, Damage, 24, 538), (EQEFFECT_F_ATTACK, Force, 17, 478),
            (EQEFFECT_AVOID, Critical, 11, 478), (EQEFFECT_CRITICAL, Critical, 11, 166),
            (EQEFFECT_IMMUNE_STUN, Resist, 10, 20));
        yield return Bond(1900045002, "到底谁是洪玄公？", "平日里不起眼的小老头生气后竟是这般模样，还是别让他生气为好……",
            new[] {30121, 50171, LocalHongCard.FirstCardId},
            (EQEFFECT_ADD_F_DAMAGE, Force, 17, 478), (EQEFFECT_AVOID, Critical, 11, 478),
            (EQEFFECT_CRITICAL, Critical, 14, 168), (EQEFFECT_IMMUNE_STUN, Resist, 10, 20),
            (EQEFFECT_IMMUNE_KNEEL, Resist, 10, 20));
        yield return Bond(1900045003, "天下四杰", "若不忘初心，便天下太平……",
            new[] {30161, 20181, 70111, LocalHongCard.FirstCardId},
            (EQEFFECT_F_ATTACK, Force, 17, 478), (EQEFFECT_HIT, Hit, 20, 202),
            (EQEFFECT_PENETRATION, Penetration, 20, 160), (EQEFFECT_CRITICAL, Critical, 20, 188),
            (EQEFFECT_IMMUNE_STUN, Resist, 10, 20));
    }

    private static RelationData Bond(int id, string name, string description, int[] members,
        params (EQEFFECT_TYPE type, int[] curve, int first, int last)[] stats)
    {
        var bond = new RelationData { Id = id, NameAlias = name, DescAlias = description };
        bond.CardId.AddRange(members);
        for (int rank = 1; rank <= 16; rank++)
        {
            var row = new RelationRankData { Rank = rank };
            foreach (var (type, curve, first, last) in stats)
            {
                double progress = (double)(curve[rank - 1] - curve[0]) / (curve[15] - curve[0]);
                row.StatValueIdType.Add(type);
                row.StatUnit.Add(EQEFFECT_UNIT_TYPE.EQEFFECT_UNIT_DIFF);
                row.StatValueId.Add(first + (int)Math.Round((last - first) * progress, MidpointRounding.AwayFromZero));
            }
            bond.RankData.Add(row);
        }
        return bond;
    }
}
