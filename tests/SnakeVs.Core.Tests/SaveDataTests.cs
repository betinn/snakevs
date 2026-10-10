using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class SaveDataTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var data = new SaveData
        {
            Difficulty = Difficulty.Hard,
            SoundOn = false,
            VibrationOn = false,
            Controls = ControlScheme.Buttons,
            VersusWins = 3,
            VersusLosses = 2,
            VersusDraws = 1,
            GamesPlayed = 9,
        };
        data.SubmitScore(GameMode.Classic, Difficulty.Hard, 1234);
        data.SubmitScore(GameMode.Versus, Difficulty.Easy, 7);

        var copy = SaveData.Parse(data.Serialize());

        Assert.Equal(Difficulty.Hard, copy.Difficulty);
        Assert.False(copy.SoundOn);
        Assert.False(copy.VibrationOn);
        Assert.Equal(ControlScheme.Buttons, copy.Controls);
        Assert.Equal(3, copy.VersusWins);
        Assert.Equal(2, copy.VersusLosses);
        Assert.Equal(1, copy.VersusDraws);
        Assert.Equal(9, copy.GamesPlayed);
        Assert.Equal(1234, copy.GetBest(GameMode.Classic, Difficulty.Hard));
        Assert.Equal(7, copy.GetBest(GameMode.Versus, Difficulty.Easy));
        Assert.Equal(0, copy.GetBest(GameMode.Classic, Difficulty.Easy));
    }

    [Fact]
    public void SubmitScore_OnlyKeepsBest()
    {
        var data = new SaveData();

        Assert.True(data.SubmitScore(GameMode.Classic, Difficulty.Normal, 100));
        Assert.False(data.SubmitScore(GameMode.Classic, Difficulty.Normal, 50));
        Assert.False(data.SubmitScore(GameMode.Classic, Difficulty.Normal, 100));
        Assert.True(data.SubmitScore(GameMode.Classic, Difficulty.Normal, 101));
        Assert.False(data.SubmitScore(GameMode.Classic, Difficulty.Normal, 0));
        Assert.Equal(101, data.GetBest(GameMode.Classic, Difficulty.Normal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("difficulty=99\nsound=abc\n=5\nbest.classic.normal=-3\ncontrols=7")]
    public void Parse_CorruptInput_FallsBackToDefaults(string? text)
    {
        var data = SaveData.Parse(text);

        Assert.Equal(Difficulty.Normal, data.Difficulty);
        Assert.True(data.SoundOn);
        Assert.Equal(ControlScheme.Swipe, data.Controls);
        Assert.Equal(0, data.GetBest(GameMode.Classic, Difficulty.Normal));
    }

    [Fact]
    public void RecordVersus_CountsOutcomes()
    {
        var data = new SaveData();
        data.RecordVersus(MatchOutcome.PlayerWon);
        data.RecordVersus(MatchOutcome.PlayerWon);
        data.RecordVersus(MatchOutcome.RivalWon);
        data.RecordVersus(MatchOutcome.Draw);
        data.RecordVersus(MatchOutcome.None);

        Assert.Equal(2, data.VersusWins);
        Assert.Equal(1, data.VersusLosses);
        Assert.Equal(1, data.VersusDraws);
    }
}
