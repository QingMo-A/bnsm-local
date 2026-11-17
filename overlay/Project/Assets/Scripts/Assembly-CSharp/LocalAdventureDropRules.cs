#if BNSM_SERVER
extern alias game;
using ITEM_TYPE = game::BscTable.ITEM_TYPE;
using NPC_REWARD_TYPE = game::BscTable.NPC_REWARD_TYPE;
#else
using BscTable;
#endif
using System;

internal static class LocalAdventureDropRules
{
    // The recovered client tables list rewards but omit the original server chances.
    internal static double BaseChance(NPC_REWARD_TYPE type, ITEM_TYPE itemType)
    {
        if (type == NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM)
            return 20;
        if (type != NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM)
            return 100;
        switch (itemType)
        {
            case ITEM_TYPE.ITEM_TYPE_INGREDIENT: return 20;
            case ITEM_TYPE.ITEM_TYPE_GEM: return 0.2;
            case ITEM_TYPE.ITEM_TYPE_EQUIPMENT: return 0.3;
            default: return 100;
        }
    }

    internal static bool IsRandom(double chance) => chance > 0 && chance < 100;

    internal static double EffectiveChance(double chance, int multiplier) => Math.Min(100, chance * multiplier);
}
