using AVAElevatorAction.Engine;

namespace AVAElevatorAction.Tests;

/// <summary>How the computer spy gets on (set SPY_PROBE to run it, for tuning).</summary>
public sealed class SpyProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_spies()
    {
        if (Environment.GetEnvironmentVariable("SPY_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "spy-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var last = g.Phase;
            int t = 0, lastLevel = 1;
            double levelStart = 0;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.Cause ?? "?"] = deaths.GetValueOrDefault(g.Cause ?? "?") + 1;
                if (g.Level != lastLevel) { levels.Add($"L{lastLevel} {(t / 60.0 - levelStart):0}s"); levelStart = t / 60.0; lastLevel = g.Level; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: {t / 60}s level {g.Level} floor {g.Spy.Floor} papers left {g.Building.PapersLeft} score {g.Score} {g.State} [{string.Join(", ", levels)}] deaths: {string.Join(", ", deaths.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
