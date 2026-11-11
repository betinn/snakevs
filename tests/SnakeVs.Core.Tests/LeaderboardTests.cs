using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class LeaderboardTests
{
    [Theory]
    [InlineData("ana", "ANA")]
    [InlineData("  zé   lagarto  ", "ZÉ LAGARTO")]
    [InlineData("joão!!@#da_silva-2", "JOÃODA_SILVA")]
    [InlineData("Ñandú.ç", "ÑANDÚ.Ç")]
    [InlineData("ABCDEFGHIJKLMNOP", "ABCDEFGHIJKL")]
    [InlineData("abcdefghijk lmno", "ABCDEFGHIJK")]
    [InlineData("<script>", "SCRIPT")]
    public void NormalizeName_KeepsWhatTheFontDraws(string raw, string expected)
    {
        Assert.Equal(expected, Leaderboard.NormalizeName(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@#$%&")]
    [InlineData("日本語")]
    public void NormalizeName_RejectsEmptyResults(string? raw)
    {
        Assert.Null(Leaderboard.NormalizeName(raw));
    }

    [Fact]
    public void NormalizeName_NeverExceedsMaxLength()
    {
        var name = Leaderboard.NormalizeName(new string('A', 11) + " " + new string('B', 5));
        Assert.NotNull(name);
        Assert.True(name.Length <= Leaderboard.NameMaxLength);
        Assert.False(name.EndsWith(' '));
    }

    [Theory]
    [InlineData(GameMode.Endless, 10, 1, 1, 30, true)]
    [InlineData(GameMode.Endless, 0, 0, 1, 30, false)]          // nothing scored
    [InlineData(GameMode.Endless, 999_999, 3, 1, 300, false)]   // far beyond 3 apples
    [InlineData(GameMode.Endless, 50, 5, 1, 3, false)]          // more apples than steps
    [InlineData(GameMode.Endless, 50, 5, 2, 300, false)]        // endless has no levels
    [InlineData(GameMode.Levels, 900, 20, 3, 900, true)]
    [InlineData(GameMode.Levels, 900, 20, 9, 900, false)]       // 20 apples cannot reach level 9
    [InlineData(GameMode.Versus, 10, 1, 1, 30, false)]          // versus is not ranked
    public void IsPlausible_FollowsTheRules(GameMode mode, int score, int apples, int level, long steps, bool expected)
    {
        var s = new ScoreSubmission(mode, Difficulty.Normal, "TEST", score, apples, level, steps);
        Assert.Equal(expected, Leaderboard.IsPlausible(s));
    }

    /// <summary>The server check must never reject a real run: autoplay many games and validate each final score.</summary>
    [Theory]
    [InlineData(GameMode.Endless, Difficulty.Easy)]
    [InlineData(GameMode.Endless, Difficulty.Hard)]
    [InlineData(GameMode.Levels, Difficulty.Normal)]
    [InlineData(GameMode.Levels, Difficulty.Hard)]
    public void IsPlausible_AcceptsEveryAutoplayedRun(GameMode mode, Difficulty difficulty)
    {
        for (var game = 0; game < 12; game++)
        {
            var s = new GameSession(mode, difficulty, seed: 500 + game);
            s.Player.IsAiControlled = true;
            s.Player.AiMistakeChance = 0.01;
            for (var frame = 0; frame < 15000; frame++)
            {
                s.ClearEvents();
                s.Update(16.7);
                if (s.State == SessionState.LevelCleared) s.AdvanceLevel();
                if (s.State == SessionState.GameOver) break;
            }

            var p = s.Player;
            if (p.Score <= 0) continue;
            var submission = new ScoreSubmission(mode, difficulty, "BOT", p.Score, p.ApplesEaten, s.Level, s.StepCount);
            Assert.True(Leaderboard.IsPlausible(submission), $"Real run rejected: {submission}");
        }
    }

    [Fact]
    public void SaveData_KeepsThePlayerName()
    {
        var data = new SaveData { PlayerName = "  zé lagarto " };
        var copy = SaveData.Parse(data.Serialize());
        Assert.Equal("ZÉ LAGARTO", copy.PlayerName);
        Assert.Null(SaveData.Parse("name=@@@\n").PlayerName);
    }
}
