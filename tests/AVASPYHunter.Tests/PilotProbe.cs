using AVASPYHunter.Engine;

namespace AVASPYHunter.Tests;

/// <summary>How far the computer driver gets in four minutes, and what wrecks it (set SPY_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_far_the_computer_drives()
    {
        if (Environment.GetEnvironmentVariable("SPY_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "spy-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            var pilot = new AttractPilot(seed);
            var causes = new Dictionary<string, int>();
            int wrecks = 0;
            Phase last = g.Phase;
            for (int t = 0; t < 60 * 240 && g.State == GameState.Playing; t++)
            {
                g.Tick(pilot.Decide(g));
                if (g.Phase == Phase.Crash && last != Phase.Crash)
                {
                    wrecks++;
                    causes[g.LastDeath ?? "?"] = causes.GetValueOrDefault(g.LastDeath ?? "?") + 1;
                }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: distance {g.Player.D:0} score {g.Score} wrecks {wrecks} state {g.State} sector {g.Sector} weapon {g.Player.Special} causes {string.Join(", ", causes.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
