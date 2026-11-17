extern alias game;
using Difficulty = game::BscTable.GAME_DIFFICULTY;

// The original save fields and wire stamps carry the persisted battle identity.
internal static class LocalBattleAttempts
{
    internal static void Bind(LocalPlayer player, uint stage, ulong stamp,
        Difficulty difficulty = Difficulty.DIFFICULTY_NORMAL, bool dungeon = false)
    {
        player.ActiveStage = stage;
        player.ActiveBattleStamp = stamp;
        player.ActiveDifficulty = difficulty;
        player.ActiveDungeon = dungeon;
    }

    internal static bool Matches(LocalPlayer player, uint stage, ulong stamp) =>
        stamp != 0 && player.ActiveStage == stage && player.ActiveBattleStamp == stamp;

    internal static void ClearMatching(LocalPlayer player, uint stage, ulong stamp)
    {
        if (Matches(player, stage, stamp)) Clear(player);
    }

    internal static void Clear(LocalPlayer player)
    {
        player.ActiveStage = null;
        player.ActiveBattleStamp = 0;
        player.ActiveDungeon = false;
        player.ActiveDifficulty = Difficulty.DIFFICULTY_NORMAL;
    }
}
