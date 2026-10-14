using AVASPYHunter.Engine;

namespace AVASPYHunter.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaspyhunter-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game under way, the roll-out skipped, and no traffic unless a test puts it there.</summary>
    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.Go();
        g.Player.Speed = 300;
        g.Idle(1);
        return g;
    }

    private static Controls Fire => new(0, 0, true);

    // ───────────────────────────── the road ─────────────────────────────

    [Fact]
    public void The_road_is_the_same_every_time_and_always_drivable()
    {
        var a = new Track(5);
        var b = new Track(5);
        bool fork = false, bridge = false, river = false;
        for (double d = 0; d < 120000; d += 37)
        {
            var la = a.At(d);
            Assert.Equal(la, b.At(d));
            Assert.True(la.R1 - la.L1 >= 120, $"too narrow at {d}");
            Assert.True(la.Left >= 0 && la.Right <= Field.Width);
            fork |= la.Split;
            bridge |= la.Bridge;
            river |= la.Surface == Surface.Water;
        }
        Assert.True(fork && bridge && river);
        Assert.Equal(Zone.Coast, Track.ZoneAt(100));
        Assert.Equal(Zone.Winter, Track.ZoneAt(Track.ZoneLength * 2 + 5));
        Assert.Equal(Zone.Coast, Track.ZoneAt(Track.ZoneLength * 4 + 5));
    }

    [Fact]
    public void The_car_rolls_out_of_the_van_then_drives()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        Assert.Equal(Phase.Rollout, g.Phase);
        Assert.Contains(g.Vehicles, v => v.Kind == VehicleKind.Van);
        g.Idle(60 * 2);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.True(g.Player.Speed > 0);
    }

    [Fact]
    public void Up_speeds_up_down_brakes_and_the_wheel_steers()
    {
        var g = Game();
        g.Run(120, new Controls(0, -1));
        Assert.True(g.Player.Speed > 500);
        double d = g.Player.D;
        g.Run(60, new Controls(0, 1));
        Assert.True(g.Player.Speed < 100);
        Assert.True(g.Player.D > d);
        g.Player.Speed = 300;
        double x = g.Player.X;
        g.Run(15, new Controls(1, 0));
        Assert.True(g.Player.X > x + 20);
    }

    [Fact]
    public void Leaving_the_road_wrecks_the_car_but_free_time_costs_nothing()
    {
        var g = Game();
        for (int i = 0; i < 60 * 3 && g.Phase == Phase.Playing; i++) g.Tick(new Controls(1, 0));
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal("left the road", g.LastDeath);
        g.Idle(60 * 3);
        Assert.Equal(GameEngine.StartingCars, g.Cars);
        Assert.Equal(Phase.Rollout, g.Phase);
    }

    [Fact]
    public void After_the_free_time_a_wreck_costs_a_car_and_the_last_ends_the_game()
    {
        var g = Game();
        g.Idle((int)(60 * (GameEngine.FreeTime + 1)));
        Assert.True(g.Free <= 0);
        for (int lost = 0; lost <= GameEngine.StartingCars && g.State == GameState.Playing; lost++)
        {
            g.Go();
            g.Run(60 * 4, new Controls(-1, 0));
            g.Idle(60 * 3);
        }
        Assert.NotEqual(GameState.Playing, g.State);
        Assert.Equal(0, g.Cars);
    }

    // ───────────────────────────── shooting and bumping ─────────────────────────────

    [Fact]
    public void Guns_stop_a_razorwheel_but_bounce_off_a_bulldog()
    {
        var g = Game();
        var r = g.Put(VehicleKind.Razorwheel, 0, 200, 300);
        g.Run(30, Fire);
        Assert.True(r.Dead);
        Assert.True(g.Score >= 150);
        var b = g.Put(VehicleKind.Bulldog, 0, 260, 300);
        g.Run(40, Fire);
        Assert.False(b.Dead);
    }

    [Fact]
    public void A_sedan_takes_three_hits()
    {
        var g = Game();
        var s = g.Put(VehicleKind.Sedan, 0, 160, 300);
        g.Run(1, Fire);
        g.Idle(8);
        Assert.False(s.Dead);
        Assert.Equal(1, s.Hits);
        // It swerves aside to bring its gunman round: follow it with the guns.
        for (int i = 0; i < 90 && !s.Dead; i++) g.Tick(new Controls(Math.Abs(s.X - g.Player.X) > 4 ? Math.Sign(s.X - g.Player.X) : 0, 0, true));
        Assert.True(s.Dead);
    }

    [Fact]
    public void Harming_an_innocent_stops_the_score()
    {
        var g = Game();
        var c = g.Put(VehicleKind.Civilian, 0, 200, 300);
        g.Run(30, Fire);
        Assert.True(c.Dead);
        Assert.True(g.NoScore > 0);
        int score = g.Score;
        g.Idle(120);
        Assert.Equal(score, g.Score);
        g.Run((int)(60 * GameEngine.NoScoreSeconds), new Controls(0, -1));
        Assert.True(g.Score > score);
    }

    [Fact]
    public void Shoving_a_raider_off_the_road_counts()
    {
        var g = Game();
        var lanes = g.Track.At(g.Player.D);
        g.Player.X = lanes.Right - 70;
        var r = g.Put(VehicleKind.Razorwheel, 30, 0, 300);
        r.FireTimer = 0;
        int score = g.Score;
        for (int i = 0; i < 60 && !r.Dead; i++) g.Tick(new Controls(1, 0));
        Assert.True(r.Dead);
        Assert.True(g.Score >= score + 150);
        Assert.Equal(Phase.Playing, g.Phase);
    }

    [Fact]
    public void A_razorwheel_with_its_blades_out_shreds_the_tyres()
    {
        var g = Game();
        var r = g.Put(VehicleKind.Razorwheel, 50, 0, 300);
        r.FireTimer = 0;
        for (int i = 0; i < 60 * 3 && g.Phase == Phase.Playing; i++) g.Tick(default);
        Assert.Equal(Phase.Crash, g.Phase);
        Assert.Equal("had the tyres shredded", g.LastDeath);
    }

    [Fact]
    public void The_gunman_leans_out_and_fires()
    {
        var g = Game();
        g.Put(VehicleKind.Sedan, 58, 8, 300).FireTimer = 0.2;
        for (int i = 0; i < 60 * 3 && g.Phase == Phase.Playing; i++) g.Tick(default);
        Assert.Equal("was shot", g.LastDeath);
    }

    // ───────────────────────────── the armoury van ─────────────────────────────

    [Fact]
    public void Driving_into_the_van_gives_a_special_weapon()
    {
        var g = Game();
        var van = g.Put(VehicleKind.Van, 0, 120, 250);
        for (int i = 0; i < 60 * 3 && g.Phase != Phase.Docking; i++) g.Tick(new Controls(0, -1));
        Assert.Equal(Phase.Docking, g.Phase);
        g.Idle(60 * 2);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.NotEqual(Special.None, g.Player.Special);
        Assert.True(g.Player.D > van.D);
        Assert.True(van.Leaving);
    }

    [Fact]
    public void Oil_and_smoke_spin_out_the_pursuers()
    {
        var g = Game();
        g.Arm(Special.Oil, 3);
        g.Run(1, new Controls(0, 0, false, true));
        Assert.Equal(2, g.Player.Ammo);
        var chaser = g.Put(VehicleKind.Bulldog, 0, -140, 420);
        for (int i = 0; i < 60 * 3 && !chaser.Dead; i++) g.Tick(default);
        Assert.True(chaser.Dead);
        Assert.True(g.Score >= 700);
        g.Arm(Special.Smoke, 1);
        g.Run(2, new Controls(0, 0, false, true));
        Assert.Equal(Special.None, g.Player.Special);
        Assert.Contains(g.Hazards, h => h.Kind == HazardKind.Smoke);
    }

    [Fact]
    public void Only_missiles_bring_down_the_helicopter()
    {
        var g = Game();
        g.SendCopter();
        g.Run(60, Fire);
        Assert.NotNull(g.Copter);
        g.Arm(Special.Missiles, 4);
        for (int i = 0; i < 4; i++)
        {
            g.Run(1, new Controls(0, 0, false, true));
            g.Idle(30);
        }
        Assert.Null(g.Copter);
        Assert.True(g.Score >= 1500);
    }

    [Fact]
    public void A_bomb_landing_on_the_car_wrecks_it_and_leaves_a_crater()
    {
        var g = Game();
        g.Player.Speed = 0;
        g.PutHazard(HazardKind.Bomb, 0, 0, 1);
        g.Idle(60);
        Assert.Equal("was bombed", g.LastDeath);
        Assert.Contains(g.Hazards, h => h.Kind == HazardKind.Crater);
    }

    // ───────────────────────────── the river ─────────────────────────────

    [Fact]
    public void The_boathouse_makes_the_car_a_boat_and_the_landing_a_car_again()
    {
        var g = Game(5);
        double boathouse = 0;
        for (double d = 2000; d < 80000 && boathouse == 0; d += 50)
            if (g.Track.At(d).Surface == Surface.Water) boathouse = d;
        Assert.True(boathouse > 0);
        g.Teleport(boathouse - 300);
        g.Player.X = g.Track.At(boathouse).Centre;
        g.Player.Speed = 300;
        for (int i = 0; i < 60 * 3 && !g.Player.Boat; i++) g.Tick(new Controls(Math.Sign(g.Track.At(g.Player.D + 40).Centre - g.Player.X), 0));
        Assert.True(g.Player.Boat);
        Assert.Equal(Phase.Playing, g.Phase);
        var pilot = new AttractPilot(1);
        for (int i = 0; i < 60 * 40 && g.Player.Boat; i++) g.Tick(pilot.Decide(g) with { Fire = false });
        Assert.False(g.Player.Boat);
    }

    [Fact]
    public void Drums_on_the_river_sink_boats_and_bullets_sink_drums()
    {
        var g = Game();
        g.PutHazard(HazardKind.Drum, 0, 300);
        g.Run(40, Fire);
        Assert.DoesNotContain(g.Hazards, h => h.Kind == HazardKind.Drum);
        g.PutHazard(HazardKind.Drum, 0, 60);
        g.Idle(30);
        Assert.Equal("hit a drum", g.LastDeath);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void Points_for_the_road_and_an_extra_car()
    {
        var g = Game();
        g.Player.Speed = 500;
        g.Idle(60);
        Assert.InRange(g.Score, 30, 60);
        int cars = g.Cars;
        g.AddScore(GameEngine.FirstExtraCar);
        Assert.Equal(cars + 1, g.Cars);
    }

    [Fact]
    public void Title_demo_and_high_score_table_take_turns()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.NotNull(g.Pilot);
        for (int i = 0; i < 60 * 70 && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void A_high_score_takes_initials()
    {
        var g = Game();
        g.AddScore(40000);
        g.Idle((int)(60 * (GameEngine.FreeTime + 1)));
        while (g.State == GameState.Playing)
        {
            g.Go();
            g.Run(60 * 4, new Controls(-1, 0));
            g.Idle(60 * 3);
        }
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('B');
        g.Initials.Type('O');
        g.Initials.Type('B');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("BOB", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void Pause_stops_everything()
    {
        var g = Game();
        g.Pause();
        double d = g.Player.D;
        g.Idle(60);
        Assert.Equal(d, g.Player.D);
        g.Resume();
        g.Idle(10);
        Assert.True(g.Player.D > d);
    }

    [Fact]
    public void The_computer_driver_gets_well_down_the_road()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            var pilot = new AttractPilot(seed);
            for (int t = 0; t < 60 * 90 && g.State == GameState.Playing; t++) g.Tick(pilot.Decide(g));
            Assert.True(g.Player.D > 22000, $"seed {seed} got only {g.Player.D:0}");
            Assert.True(g.Score > 1500);
        }
    }

    [Fact]
    public void Every_sound_has_a_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>())
            if (s != Sfx.MusicStop) Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), s.ToString());
    }
}
