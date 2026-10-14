namespace AVASPYHunter.Engine;

/// <summary>The player's car (or, on the river, boat).</summary>
public sealed class PlayerCar
{
    public double X = Field.Width / 2;
    /// <summary>How far down the road it has come.</summary>
    public double D;
    public double Speed;
    public double Vx;
    public bool Boat;
    public Special Special;
    public int Ammo;
    public double FireTimer;
    public double SpecialTimer;
    public bool SpecialHeld;
    public double Invulnerable;
    public const double Width = 26, Length = 46;
}

public sealed partial class GameEngine
{
    public const double MaxSpeed = 560, BoatSpeed = 480, Accel = 330, Brake = 620, Drag = 45;
    public const double SteerSpeed = 270, BulletSpeed = 950, FireInterval = 0.11;
    public const int FirstExtraCar = 20000, ExtraCarEvery = 30000;
    public const double NoScoreSeconds = 8;

    public Track Track { get; private set; } = new(1);
    public PlayerCar Player { get; private set; } = new();
    public List<Vehicle> Vehicles { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Hazard> Hazards { get; } = [];
    public Copter? Copter { get; private set; }
    public int NextExtraCar { get; private set; } = FirstExtraCar;
    /// <summary>Seconds left in which the score doesn't count, after harming an innocent.</summary>
    public double NoScore { get; private set; }
    public string? LastDeath { get; private set; }
    public Zone Zone => Track.ZoneAt(Player.D);
    public int Sector => Track.Sector(Player.D);
    public Vehicle? DockedIn { get; private set; }

    private double _spawnTimer, _vanTimer, _copterTimer, _distance;
    private int _weaponTurn;
    private int _sector = 1;

    /// <summary>Test hook: no traffic arrives unless a test puts it there.</summary>
    internal bool Calm { get; set; }

    private double Difficulty => 1 + Player.D / 50000;
    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);
    private Lanes Here => Track.At(Player.D);

    private void NewRun(int seed)
    {
        _rng = new Random(seed);
        Track = new Track(seed);
        Player = new PlayerCar { Speed = 0 };
        Vehicles.Clear();
        Shots.Clear();
        Hazards.Clear();
        Copter = null;
        NoScore = 0;
        DockedIn = null;
        _spawnTimer = 3;
        _vanTimer = 25;
        _copterTimer = 45;
        _distance = 0;
        _weaponTurn = 0;
        _sector = 1;
        Rollout();
    }

    /// <summary>A fresh car rolls out of the back of the armoury van.</summary>
    private void Rollout()
    {
        var lanes = Here;
        Player.X = lanes.Nearest(lanes.Centre);
        Player.Vx = 0;
        Player.Speed = 120;
        Player.Invulnerable = 3;
        Player.Boat = lanes.Surface == Surface.Water;
        if (!Player.Boat)
            Vehicles.Add(new Vehicle { Kind = VehicleKind.Van, X = Player.X, D = Player.D + 70, Speed = 200, Leaving = true, Age = -0.8 });
        SetPhase(Phase.Rollout);
    }

    internal void AddScore(int points)
    {
        if (points <= 0) return;
        int before = Score;
        Score += points;
        if (Score >= NextExtraCar && before < NextExtraCar)
        {
            Cars++;
            Play(Sfx.ExtraLife);
            Popup(Player.X, Player.D + 60, "EXTRA CAR", 1.5);
            NextExtraCar = NextExtraCar == FirstExtraCar ? FirstExtraCar + ExtraCarEvery : NextExtraCar + ExtraCarEvery;
        }
    }

    private void Popup(double x, double d, string text, double life = 1) =>
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, D = d, Life = life, Text = text });

    private void Explode(double x, double d, bool big = false) =>
        Effects.Add(new Effect { Kind = big ? EffectKind.BigExplosion : EffectKind.Explosion, X = x, D = d, Life = big ? 1.2 : 0.6 });

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var p = Player;
        switch (Phase)
        {
            case Phase.Rollout:
                p.Speed = Math.Min(300, p.Speed + 200 * dt);
                p.D += p.Speed * dt;
                p.X += (Here.Nearest(p.X) - p.X) * Math.Min(1, 4 * dt);
                MoveVehicles(dt);
                if (PhaseTime > 1.4)
                {
                    SetPhase(Phase.Playing);
                    Music(true);
                }
                return;
            case Phase.Docking:
                Dock(dt);
                return;
            case Phase.Crash:
                p.Speed = Math.Max(0, p.Speed - 500 * dt);
                p.D += p.Speed * dt;
                MoveVehicles(dt);
                MoveShots(dt);
                if (PhaseTime > 2.2) Recover();
                return;
        }

        if (Free > 0)
        {
            Free -= dt;
            if (Free <= 0) Say("FREE TIME OVER", 2.5);
        }
        if (NoScore > 0) NoScore -= dt;
        double before = p.D;
        Drive(dt, c);
        if (Phase != Phase.Playing) return;
        Waterline(before);
        Sectors();
        if (!Calm) Spawn(dt);
        MoveVehicles(dt);
        MoveShots(dt);
        MoveHazards(dt);
        FlyCopter(dt);
        Collide();
        // Points for the road covered (unless an innocent was harmed lately).
        _distance += p.Speed * dt;
        int pts = (int)(_distance / 12);
        _distance -= pts * 12;
        if (NoScore <= 0) AddScore(pts);
    }

    private void Drive(double dt, Controls c)
    {
        var p = Player;
        if (p.Invulnerable > 0) p.Invulnerable -= dt;
        double top = p.Boat ? BoatSpeed : MaxSpeed;
        if (c.Dy < 0) p.Speed += Accel * dt;
        else if (c.Dy > 0) p.Speed -= Brake * dt;
        else p.Speed -= Drag * dt;
        p.Speed = Math.Clamp(p.Speed, 0, top);
        // Steering bites harder at speed; on ice and on water the car slides.
        double grip = p.Boat ? 4 : Zone == Zone.Winter ? 2.4 : 9;
        double want = c.Dx * SteerSpeed * (0.45 + 0.55 * Math.Min(1, p.Speed / 300));
        p.Vx += (want - p.Vx) * Math.Min(1, grip * dt);
        p.X += p.Vx * dt;
        p.D += p.Speed * dt;
        var lanes = Here;
        if (!lanes.Holds(p.X, PlayerCar.Width / 2 - 7) && p.Invulnerable <= 0)
        {
            Wreck(lanes.Surface == Surface.Water ? "ran aground" : lanes.Bridge ? "went off the bridge" : "left the road");
            return;
        }
        if (p.Invulnerable > 0) p.X = Math.Clamp(p.X, lanes.Left + 14, lanes.Right - 14);
        // The guns.
        p.FireTimer -= dt;
        if (c.Fire && p.FireTimer <= 0)
        {
            p.FireTimer = FireInterval;
            foreach (int side in new[] { -1, 1 })
                Shots.Add(new Shot { Kind = ShotKind.Bullet, X = p.X + side * 6, D = p.D + 26, Vd = p.Speed + BulletSpeed });
            Play(Sfx.Shoot);
        }
        // The special weapon, one press at a time.
        p.SpecialTimer -= dt;
        if (c.Special && !p.SpecialHeld && p.Special != Special.None && p.SpecialTimer <= 0) UseSpecial();
        p.SpecialHeld = c.Special;
    }

    private void UseSpecial()
    {
        var p = Player;
        p.SpecialTimer = 0.3;
        switch (p.Special)
        {
            case Special.Oil:
                Hazards.Add(new Hazard { Kind = HazardKind.Oil, X = p.X, D = p.D - 36, Life = 7 });
                Play(Sfx.Oil);
                break;
            case Special.Smoke:
                Hazards.Add(new Hazard { Kind = HazardKind.Smoke, X = p.X, D = p.D - 46, Life = 3 });
                Play(Sfx.Smoke);
                break;
            case Special.Missiles:
                Shots.Add(new Shot { Kind = ShotKind.Missile, X = p.X, D = p.D + 30, Vd = p.Speed + 520 });
                Play(Sfx.Missile);
                break;
        }
        if (--p.Ammo <= 0) p.Special = Special.None;
    }

    /// <summary>Into a boathouse the car becomes a boat; at the landing, a car again.</summary>
    private void Waterline(double before)
    {
        if (Track.Crossing(before, Player.D) is null) return;
        bool water = Here.Surface == Surface.Water;
        if (water == Player.Boat) return;
        Player.Boat = water;
        Player.Speed = Math.Min(Player.Speed, water ? BoatSpeed : MaxSpeed);
        // Whatever can't follow is left behind.
        Vehicles.RemoveAll(v => v.Boat != water && v.Kind != VehicleKind.Van || water && v.Kind == VehicleKind.Van);
        Hazards.RemoveAll(h => h.Kind is HazardKind.Drum or HazardKind.Crater);
        if (water && Copter is not null) Copter.Leaving = true;
        Say(water ? "BOATHOUSE" : "LANDING", 1.5);
        Play(water ? Sfx.Boat : Sfx.Car);
    }

    private void Sectors()
    {
        int s = Sector;
        if (s == _sector) return;
        _sector = s;
        Say(ZoneName(Zone), 3);
        AddScore(1000);
    }

    public static string ZoneName(Zone z) => z switch
    {
        Zone.Coast => "THE COAST ROAD",
        Zone.Pines => "PINE PASS",
        Zone.Winter => "WINTER RUN - ICE!",
        _ => "NIGHT DRIVE",
    };

    // ───────────────────────────── traffic ─────────────────────────────

    private void Spawn(double dt)
    {
        var p = Player;
        _vanTimer -= dt;
        if (_vanTimer <= 0 && !p.Boat && Here.Surface == Surface.Road && !Vehicles.Any(v => v.Kind == VehicleKind.Van))
        {
            _vanTimer = Rand(28, 40);
            SpawnAt(VehicleKind.Van, p.D - 420, p.Speed + 160);
        }
        _copterTimer -= dt;
        if (_copterTimer <= 0 && Copter is null && !p.Boat && p.D > 9000)
        {
            _copterTimer = Rand(40, 60) / Difficulty;
            Copter = new Copter { X = p.X, Ahead = 900, Hits = 2 };
            Say("HELICOPTER", 2);
            Play(Sfx.Copter);
        }
        _spawnTimer -= dt;
        if (_spawnTimer > 0) return;
        _spawnTimer = Rand(1.5, 2.5) / Math.Min(2, Difficulty);
        if (Vehicles.Count(v => !v.Dead) >= 2 + (int)(Difficulty * 2)) return;
        if (p.Boat)
        {
            int roll = _rng.Next(100);
            if (roll < 35) SpawnAt(VehicleKind.Launch, p.D + 860, Rand(140, 240));
            else if (roll < 70) SpawnAt(VehicleKind.DrumBoat, p.D + 860, Rand(150, 210));
            else SpawnAt(VehicleKind.TorpedoBoat, p.D - 420, p.Speed + 140);
            return;
        }
        // Early on most of the traffic is innocent; the raiders thicken as the road goes on.
        int innocents = (int)Math.Max(35, 65 - p.D / 2000);
        int r = _rng.Next(100);
        if (r < innocents)
        {
            bool overtake = _rng.Next(5) == 0;
            SpawnAt(VehicleKind.Civilian, overtake ? p.D - 420 : p.D + 860, overtake ? p.Speed + 120 : Rand(170, 300));
        }
        else if (r < innocents + 22 || p.D < 4000) SpawnAt(VehicleKind.Razorwheel, p.D - 420, p.Speed + 140);
        else if (r < innocents + 38 || p.D < 8000) SpawnAt(VehicleKind.Sedan, p.D - 420, p.Speed + 130);
        else SpawnAt(VehicleKind.Bulldog, p.D - 420, p.Speed + 120);
    }

    private static readonly uint[] CivilianColours = [0xFF3A7AD8, 0xFF2AA870, 0xFFE0C040, 0xFFD87A2A, 0xFF9A6AD8, 0xFFE8E8E8];

    private Vehicle SpawnAt(VehicleKind kind, double d, double speed)
    {
        var lanes = Track.At(d);
        double x = lanes.Split
            ? (_rng.Next(2) == 0 ? Rand(lanes.L1 + 24, lanes.R1 - 24) : Rand(lanes.L2 + 24, lanes.R2 - 24))
            : Rand(lanes.L1 + 26, lanes.R1 - 26);
        if (kind == VehicleKind.Van) x = lanes.Nearest(lanes.Centre);
        var v = new Vehicle
        {
            Kind = kind, X = x, D = d, Speed = speed, Side = Player.X < x ? 1 : -1,
            Hits = kind switch { VehicleKind.Sedan => 3, VehicleKind.TorpedoBoat => 2, VehicleKind.DrumBoat => 4, _ => 1 },
            Colour = CivilianColours[_rng.Next(CivilianColours.Length)], FireTimer = Rand(1, 2),
        };
        Vehicles.Add(v);
        return v;
    }

    private void MoveVehicles(double dt)
    {
        var p = Player;
        foreach (var v in Vehicles)
        {
            if (v.Dead) continue;
            v.Age += dt;
            v.Bumped += dt;
            if (v.Flash > 0) v.Flash -= dt;
            var lanes = Track.At(v.D);
            double rel = v.D - p.D;
            double targetX = lanes.Nearest(v.X), targetSpeed = v.Speed;
            bool chasing = Phase == Phase.Playing;
            switch (v.Kind)
            {
                case VehicleKind.Razorwheel:
                    // It draws alongside, runs its blades out (you can see them coming), then swerves in.
                    targetSpeed = p.Speed + Math.Clamp(-rel * 2.6, -220, 260);
                    if (chasing && Math.Abs(rel) < 40 || v.FireTimer < 0) v.FireTimer += dt;
                    else v.FireTimer = Math.Max(0, v.FireTimer - dt * 2);
                    v.Blades = v.FireTimer > 0.6;
                    targetX = v.FireTimer > 1.1 ? p.X + v.Side * 18 : p.X + v.Side * 44;
                    // A lunge that misses: it drops back and tries again.
                    if (v.FireTimer > 2.2) { v.FireTimer = -1.2; v.Blades = false; }
                    if (v.FireTimer < 0) { targetSpeed = p.Speed - 160; targetX = p.X + v.Side * 70; }
                    break;
                case VehicleKind.Bulldog:
                    targetSpeed = p.Speed + Math.Clamp(-rel * 2.2, -180, 230);
                    targetX = Math.Abs(rel) < 60 && chasing ? p.X + v.Side * 16 : p.X + v.Side * 44;
                    break;
                case VehicleKind.Sedan:
                    targetSpeed = p.Speed + Math.Clamp((8 - rel) * 2.4, -220, 240);
                    targetX = p.X + v.Side * 58;
                    // The gunman leans out and takes aim (you can see him) before he fires.
                    if (chasing && Math.Abs(rel) < 30) v.FireTimer -= dt;
                    if (v.FireTimer <= 0)
                    {
                        v.FireTimer = Rand(1.8, 2.6) / Math.Min(1.5, Difficulty);
                        Shots.Add(new Shot { Kind = ShotKind.Gunman, X = v.X - v.Side * 16, D = v.D + 6, Vx = -v.Side * 300, Vd = v.Speed, Hostile = true });
                        Play(Sfx.Shoot);
                    }
                    break;
                case VehicleKind.Van when !v.Leaving:
                    // It overtakes, then holds a steady pace for the player to catch it up.
                    targetSpeed = rel < 40 && v.Age < 6 ? p.Speed + 160 : 300;
                    targetX = lanes.Nearest(lanes.Centre);
                    if (v.Age > 22) v.Leaving = true;
                    break;
                case VehicleKind.Van:
                    targetSpeed = v.Age < 0 ? p.Speed : 720;
                    break;
                case VehicleKind.DrumBoat:
                    v.FireTimer -= dt;
                    if (chasing && rel is > 60 and < 760 && v.FireTimer <= 0)
                    {
                        v.FireTimer = Rand(1.3, 2.0);
                        Hazards.Add(new Hazard { Kind = HazardKind.Drum, X = v.X + Rand(-8, 8), D = v.D - v.Length / 2 - 10, Life = 30 });
                    }
                    break;
                case VehicleKind.TorpedoBoat:
                    if (v.Hits > 0 && v.Age < 9)
                    {
                        targetSpeed = p.Speed + Math.Clamp((-170 - rel) * 2, -200, 220);
                        targetX = p.X + Math.Sin(v.Age) * 30;
                        v.FireTimer -= dt;
                        if (chasing && rel < -150 && Math.Abs(v.X - p.X) < 30 && v.FireTimer <= 0)
                        {
                            v.FireTimer = Rand(2.6, 3.4);
                            Shots.Add(new Shot { Kind = ShotKind.Torpedo, X = v.X, D = v.D + v.Length / 2, Vd = v.Speed + 280, Hostile = true });
                            Play(Sfx.Missile);
                        }
                    }
                    else targetSpeed = p.Speed + 260;
                    break;
            }
            // Keep to the carriageway (the chasers ride its edge, the others its middle).
            targetX = Math.Clamp(targetX, lanes.Left + v.Width / 2 + 4, lanes.Right - v.Width / 2 - 4);
            if (lanes.Split && v.X > lanes.R1 && v.X < lanes.L2) targetX = lanes.Nearest(v.X);
            if (v.Spin > 0)
            {
                v.Spin -= dt;
                v.Vx += Math.Sin(v.Age * 13) * 900 * dt;
                targetSpeed = v.Speed * 0.97;
                if (v.Spin <= 0) { Smash(v, credited: v.Bumped < 3); continue; }
            }
            else
            {
                double max = v.Kind == VehicleKind.Bulldog ? 190 : 230;
                v.Vx += (Math.Clamp((targetX - v.X) * 4, -max, max) - v.Vx) * Math.Min(1, 5 * dt);
            }
            double accel = v.Kind == VehicleKind.Van && v.Leaving ? 600 : 420;
            v.Speed += Math.Clamp(targetSpeed - v.Speed, -accel * dt, accel * dt);
            v.Speed = Math.Max(0, v.Speed);
            v.X += v.Vx * dt;
            v.D += v.Speed * dt;
            if (!Track.At(v.D).Holds(v.X, v.Width / 2 - 6)) Smash(v, credited: v.Bumped < 1.5);
            if (rel > 1300 || rel < -800) v.Dead = true;
        }
        Vehicles.RemoveAll(v => v.Dead);
    }

    /// <summary>A vehicle is wrecked: points if it was an enemy the player put off the road, a penalty if it was an innocent.</summary>
    private void Smash(Vehicle v, bool credited, int points = -1)
    {
        if (v.Dead) return;
        v.Dead = true;
        Explode(v.X, v.D, big: v.Kind is VehicleKind.Bulldog or VehicleKind.DrumBoat or VehicleKind.Van);
        Play(v.Boat ? Sfx.Splash : Sfx.Explode);
        if (!credited) return;
        if (v.Innocent)
        {
            NoScore = NoScoreSeconds;
            Say("NO SCORE - SPARE THE INNOCENT", 2.5);
            Play(Sfx.NoScore);
            return;
        }
        if (points < 0) points = v.Kind switch
        {
            VehicleKind.Razorwheel => 150, VehicleKind.Sedan => 500, VehicleKind.Bulldog => 700,
            VehicleKind.DrumBoat => 400, VehicleKind.TorpedoBoat => 500, _ => 0,
        };
        if (NoScore > 0) return;
        AddScore(points);
        if (points > 0) Popup(v.X, v.D, points.ToString());
    }

    // ───────────────────────────── shots, hazards, the helicopter ─────────────────────────────

    private void MoveShots(double dt)
    {
        var p = Player;
        foreach (var s in Shots)
        {
            s.Age += dt;
            if (s.Kind == ShotKind.Missile)
            {
                // Missiles steer for the helicopter, or the nearest enemy ahead.
                double? tx = Copter is { Dead: false } c ? c.X : Vehicles.Where(v => v.Enemy && v.D > s.D).OrderBy(v => v.D - s.D).Select(v => (double?)v.X).FirstOrDefault();
                if (tx is { } x) s.Vx += Math.Clamp((x - s.X) * 6, -900, 900) * dt;
            }
            s.X += s.Vx * dt;
            s.D += s.Vd * dt;
            double rel = s.D - p.D;
            if (rel > 1000 || rel < -400 || s.X < -40 || s.X > Field.Width + 40 || s.Age > 3) s.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);
    }

    private void MoveHazards(double dt)
    {
        foreach (var h in Hazards.ToList())
        {
            h.Age += dt;
            if (h.Kind == HazardKind.Bomb && h.Age >= 0.9)
            {
                h.Dead = true;
                Explode(h.X, h.D, big: true);
                Play(Sfx.BombBlast);
                if (Here.Surface == Surface.Road) Hazards.Add(new Hazard { Kind = HazardKind.Crater, X = h.X, D = h.D, Life = 5 });
                foreach (var v in Vehicles.Where(v => Near(v.X, v.D, h.X, h.D, h.Radius))) Smash(v, credited: false);
                if (Near(Player.X, Player.D, h.X, h.D, h.Radius) && Player.Invulnerable <= 0) Wreck("was bombed");
                continue;
            }
            if (h.Age > h.Life || h.D < Player.D - 700) h.Dead = true;
        }
        Hazards.RemoveAll(h => h.Dead);
    }

    private static bool Near(double x1, double d1, double x2, double d2, double r) => (x1 - x2) * (x1 - x2) + (d1 - d2) * (d1 - d2) < r * r;

    private void FlyCopter(double dt)
    {
        if (Copter is not { } c) return;
        var p = Player;
        c.Age += dt;
        if (c.Flash > 0) c.Flash -= dt;
        if (c.Age > 26 || p.Boat) c.Leaving = true;
        if (c.Leaving)
        {
            c.Ahead += 260 * dt;
            if (c.Ahead > 1000) Copter = null;
            return;
        }
        c.Ahead += (470 - c.Ahead) * Math.Min(1, 1.2 * dt);
        c.X += Math.Clamp(p.X - c.X, -150 * dt, 150 * dt);
        c.BombTimer -= dt;
        if (c.BombTimer <= 0 && c.Ahead < 600)
        {
            c.BombTimer = Rand(1.4, 2.2) / Math.Min(1.6, Difficulty);
            Hazards.Add(new Hazard { Kind = HazardKind.Bomb, X = c.X, D = p.D + c.Ahead - 30, Life = 1 });
            Play(Sfx.BombDrop);
        }
    }

    // ───────────────────────────── collisions ─────────────────────────────

    private void Collide()
    {
        var p = Player;
        // The player's bullets and missiles.
        foreach (var s in Shots.Where(s => !s.Hostile && !s.Dead))
        {
            if (s.Kind == ShotKind.Missile && Copter is { Dead: false, Leaving: false } c && Near(s.X, s.D, c.X, p.D + c.Ahead, 44))
            {
                s.Dead = true;
                c.Flash = 0.1;
                if (--c.Hits <= 0)
                {
                    c.Dead = true;
                    Copter = null;
                    Explode(c.X, p.D + c.Ahead, big: true);
                    Play(Sfx.Explode);
                    if (NoScore <= 0) { AddScore(1500); Popup(c.X, p.D + c.Ahead, "1500", 1.5); }
                }
                else Play(Sfx.Hit);
                continue;
            }
            foreach (var v in Vehicles)
            {
                if (v.Dead || Math.Abs(s.X - v.X) > v.Width / 2 + 2 || Math.Abs(s.D - v.D) > v.Length / 2 + 4) continue;
                s.Dead = true;
                if (s.Kind == ShotKind.Bullet && v.Kind is VehicleKind.Bulldog or VehicleKind.Van)
                {
                    Effects.Add(new Effect { Kind = EffectKind.Spark, X = s.X, D = s.D, Life = 0.2 });
                    Play(Sfx.Ricochet);
                }
                else if (s.Kind == ShotKind.Missile || --v.Hits <= 0) Smash(v, credited: true);
                else { v.Flash = 0.08; Play(Sfx.Hit); }
                break;
            }
            if (s.Dead) continue;
            foreach (var h in Hazards.Where(h => h.Kind == HazardKind.Drum && Near(s.X, s.D, h.X, h.D, 14)))
            {
                s.Dead = true;
                h.Dead = true;
                Explode(h.X, h.D);
                Play(Sfx.Splash);
                if (NoScore <= 0) AddScore(50);
                break;
            }
        }
        // Enemy fire.
        foreach (var s in Shots.Where(s => s.Hostile && !s.Dead))
        {
            if (Math.Abs(s.X - p.X) < PlayerCar.Width / 2 + 2 && Math.Abs(s.D - p.D) < PlayerCar.Length / 2 + 2 && p.Invulnerable <= 0)
            {
                s.Dead = true;
                Wreck(s.Kind == ShotKind.Torpedo ? "was torpedoed" : "was shot");
                return;
            }
            if (s.Kind == ShotKind.Torpedo)
                foreach (var v in Vehicles.Where(v => v.Kind != VehicleKind.TorpedoBoat && Math.Abs(s.X - v.X) < v.Width / 2 && Math.Abs(s.D - v.D) < v.Length / 2))
                {
                    s.Dead = true;
                    Smash(v, credited: false);
                    break;
                }
        }
        // Bumping into things.
        foreach (var v in Vehicles.Where(v => !v.Dead).ToList())
        {
            double ox = (PlayerCar.Width + v.Width) / 2 - Math.Abs(p.X - v.X);
            double od = (PlayerCar.Length + v.Length) / 2 - Math.Abs(p.D - v.D);
            if (ox <= 0 || od <= 0) continue;
            // Up the ramp into the armoury van.
            if (v.Kind == VehicleKind.Van && !v.Leaving && p.D < v.D && Math.Abs(p.X - v.X) < 16 && p.Speed > v.Speed - 20 && Phase == Phase.Playing)
            {
                StartDocking(v);
                return;
            }
            if (p.Invulnerable > 0) continue;
            if (ox < od)
            {
                // Side to side: each shoves the other, the heavier the harder.
                int dir = p.X < v.X ? -1 : 1;
                // Blades out and swerving in, they shred the tyres; rammed first, the razorwheel is the one shoved.
                if (v.Kind == VehicleKind.Razorwheel && v.FireTimer > 0.85 && v.Vx * dir > -p.Vx * dir)
                {
                    Wreck("had the tyres shredded");
                    return;
                }
                double total = 1 + v.Mass;
                p.X += dir * ox * v.Mass / total;
                v.X -= dir * ox / total;
                p.Vx = dir * 170 * v.Mass / total;
                v.Vx = -dir * 300 / total * (v.Kind == VehicleKind.Van ? 0.2 : 1);
                v.Bumped = 0;
                Play(Sfx.Bump);
            }
            else
            {
                // Nose to tail: the one behind is slowed to the one in front.
                if (p.D < v.D)
                {
                    p.D -= od;
                    p.Speed = Math.Min(p.Speed, v.Speed * 0.85);
                }
                else
                {
                    v.D -= od;
                    v.Speed = Math.Min(v.Speed, p.Speed * 0.85);
                }
                v.Bumped = 0;
                Play(Sfx.Bump);
            }
        }
        // What lies on the road.
        foreach (var h in Hazards)
        {
            if (h.Kind is HazardKind.Oil or HazardKind.Smoke)
            {
                foreach (var v in Vehicles.Where(v => v.Spin <= 0 && v.Kind != VehicleKind.Van && Near(v.X, v.D, h.X, h.D, h.Radius + 10)))
                {
                    v.Spin = 0.9;
                    v.Bumped = 0;
                    Play(Sfx.Skid);
                }
                continue;
            }
            if (h.Kind is HazardKind.Crater or HazardKind.Drum)
            {
                foreach (var v in Vehicles.Where(v => !v.Dead && v.Kind != VehicleKind.DrumBoat && Near(v.X, v.D, h.X, h.D, h.Radius + 10))) Smash(v, credited: false);
                if (Near(p.X, p.D, h.X, h.D, h.Radius + 12) && p.Invulnerable <= 0)
                {
                    if (h.Kind == HazardKind.Drum) h.Dead = true;
                    Wreck(h.Kind == HazardKind.Drum ? "hit a drum" : "drove into a crater");
                    return;
                }
            }
        }
    }

    // ───────────────────────────── the van ─────────────────────────────

    private void StartDocking(Vehicle van)
    {
        DockedIn = van;
        van.Leaving = false;
        Shots.RemoveAll(s => s.Hostile);
        Play(Sfx.Dock);
        SetPhase(Phase.Docking);
    }

    /// <summary>Up the ramp, the weapon loaded, and out again ahead of the van.</summary>
    private void Dock(double dt)
    {
        var p = Player;
        var van = DockedIn!;
        van.Speed = 320;
        van.X += (Track.At(van.D).Nearest(Track.At(van.D).Centre) - van.X) * Math.Min(1, 2 * dt);
        van.D += van.Speed * dt;
        p.Speed = van.Speed;
        p.D = van.D - 6;
        p.X = van.X;
        MoveShots(dt);
        if (PhaseTime < 1.5) return;
        var weapon = Copter is not null ? Special.Missiles : (Special)(_weaponTurn++ % 3 + 1);
        p.Special = weapon;
        p.Ammo = weapon switch { Special.Oil => 8, Special.Smoke => 6, _ => 6 };
        p.D = van.D + van.Length / 2 + PlayerCar.Length / 2 + 6;
        p.Speed = van.Speed + 120;
        p.Invulnerable = 1.2;
        van.Leaving = true;
        van.Speed = 200;
        DockedIn = null;
        Say(weapon switch { Special.Oil => "OIL SLICK", Special.Smoke => "SMOKE SCREEN", _ => "MISSILES" }, 2);
        Play(Sfx.Weapon);
        if (NoScore <= 0) AddScore(250);
        SetPhase(Phase.Playing);
    }

    // ───────────────────────────── wrecks ─────────────────────────────

    private void Wreck(string cause)
    {
        if (Phase != Phase.Playing) return;
        LastDeath = cause;
        Explode(Player.X, Player.D, big: true);
        Play(Player.Boat ? Sfx.Splash : Sfx.Crash);
        Music(false);
        SetPhase(Phase.Crash);
    }

    private void Recover()
    {
        if (Free <= 0)
        {
            Cars--;
            if (Cars < 0)
            {
                Cars = 0;
                GameOver();
                return;
            }
        }
        Shots.Clear();
        Vehicles.RemoveAll(v => Math.Abs(v.D - Player.D) < 500);
        Hazards.RemoveAll(h => h.Kind is HazardKind.Bomb or HazardKind.Crater or HazardKind.Drum && Math.Abs(h.D - Player.D) < 600);
        Player.Special = Special.None;
        Player.Ammo = 0;
        Rollout();
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Test hook: skip the roll-out.</summary>
    internal void Go()
    {
        if (Phase != Phase.Rollout) return;
        Vehicles.Clear();
        Player.Invulnerable = 0;
        SetPhase(Phase.Playing);
        Message = null;
    }

    internal Vehicle Put(VehicleKind kind, double dx, double dd, double speed)
    {
        var v = new Vehicle
        {
            Kind = kind, X = Player.X + dx, D = Player.D + dd, Speed = speed, Side = dx < 0 ? -1 : 1, Colour = 0xFF3A7AD8, FireTimer = 99,
            Hits = kind switch { VehicleKind.Sedan => 3, VehicleKind.TorpedoBoat => 2, VehicleKind.DrumBoat => 4, _ => 1 },
        };
        Vehicles.Add(v);
        return v;
    }

    internal Hazard PutHazard(HazardKind kind, double dx, double dd, double life = 10)
    {
        var h = new Hazard { Kind = kind, X = Player.X + dx, D = Player.D + dd, Life = life };
        Hazards.Add(h);
        return h;
    }

    internal void Arm(Special s, int ammo)
    {
        Player.Special = s;
        Player.Ammo = ammo;
    }

    internal void SendCopter() => Copter = new Copter { X = Player.X, Ahead = 470, BombTimer = 99 };

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Test hook: jump the run forward to a distance down the road.</summary>
    internal void Teleport(double d)
    {
        Player.D = d;
        Player.X = Here.Nearest(Here.Centre);
        Player.Boat = Here.Surface == Surface.Water;
        _sector = Sector;
    }
}
