using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASPYHunter.Engine;
using AVASPYHunter.Views;

namespace AVASPYHunter.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SPY_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspyhunter-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SPY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASPYHunter-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Drives with the computer driver until something worth a picture is on screen.</summary>
    private static void DriveUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int seed, int maxSeconds = 240)
    {
        var pilot = new AttractPilot(seed);
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(pilot.Decide(g));
            if (g.State != GameState.Playing) g.StartGame();
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("SPY", 48250, 3);
        g.HighScores.Insert("AVA", 21000, 2);
        for (int i = 0; i < 60 * 16; i++) g.Tick(default);
        g.Idle(60 * 52);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Out_on_the_road()
    {
        var (w, c, g) = Open(5);
        g.StartGame();
        g.Idle(20);
        Shot(w, c, "rollout");
        DriveUntil(g, c, g => g.Phase == Phase.Playing && g.Player.D > 2500 && g.Vehicles.Count(v => Math.Abs(v.D - g.Player.D) < 500) >= 3, 1);
        Shot(w, c, "traffic");
        DriveUntil(g, c, g => g.Phase == Phase.Playing && g.Vehicles.Any(v => v.Kind == VehicleKind.Van && !v.Leaving && v.D - g.Player.D is > 60 and < 300), 2);
        Shot(w, c, "van");
        DriveUntil(g, c, g => g.Phase == Phase.Playing && g.Player.Invulnerable <= 0 && !g.Track.At(g.Player.D).Split && g.Vehicles.All(v => Math.Abs(v.D - g.Player.D) > 120), 3);
        var lanes = g.Track.At(g.Player.D);
        int side = g.Player.X - lanes.Left > lanes.Right - g.Player.X ? -1 : 1;
        var razor = g.Put(VehicleKind.Razorwheel, side * 46, 4, g.Player.Speed);
        razor.FireTimer = 0.75;
        g.Put(VehicleKind.Sedan, -side * 60, 160, g.Player.Speed).FireTimer = 0.5;
        g.Idle(3);
        c.Advance(0.016);
        Shot(w, c, "razorwheel");
        DriveUntil(g, c, g => g.Phase == Phase.Playing && g.Track.At(g.Player.D + 200).Split, 4);
        Shot(w, c, "fork");
    }

    [AvaloniaFact]
    public void Further_down_the_road()
    {
        var (w, c, g) = Open(6);
        g.StartGame();
        g.Go();
        g.Teleport(9000);
        g.SendCopter();
        g.Arm(Special.Missiles, 6);
        g.Idle(30);
        g.PutHazard(HazardKind.Bomb, -40, 200, 1);
        g.Idle(20);
        Shot(w, c, "helicopter");
        // The river.
        var (w2, c2, g2) = Open(7);
        g2.StartGame();
        g2.Go();
        for (double d = 9000; d < 60000; d += 200)
            if (g2.Track.At(d).Surface == Surface.Water && g2.Track.At(d - 1200).Surface == Surface.Water) { g2.Teleport(d); break; }
        g2.Put(VehicleKind.DrumBoat, 40, 320, 150);
        g2.PutHazard(HazardKind.Drum, 30, 200);
        g2.Put(VehicleKind.Launch, -60, 500, 160);
        g2.Idle(30);
        Shot(w2, c2, "river");
        // Winter and night.
        var (w3, c3, g3) = Open(8);
        g3.StartGame();
        g3.Go();
        g3.Teleport(Track.ZoneLength * 2 + 3000);
        g3.Put(VehicleKind.Civilian, 50, 300, 250);
        g3.Put(VehicleKind.Bulldog, -40, -80, 450);
        g3.Idle(20);
        Shot(w3, c3, "winter");
        g3.Teleport(Track.ZoneLength * 3 + 3000);
        g3.Put(VehicleKind.Sedan, 60, 10, 400);
        g3.Put(VehicleKind.Civilian, -40, 350, 250);
        g3.Arm(Special.Smoke, 4);
        g3.Run(2, new Engine.Controls(0, 0, false, true));
        g3.Idle(20);
        Shot(w3, c3, "night");
    }
}
