namespace AVASPYHunter.Engine;

/// <summary>
/// The computer driver for the attract mode (and the tests). It keeps to the middle of its
/// carriageway with the road ahead in mind, steers round traffic, bombs and drums, drives up into the
/// armoury van when it comes, fires at enemies in line ahead (but never with an innocent in the
/// way), and uses its special weapon on whatever is chasing it.
/// </summary>
public sealed class AttractPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _specialHeld;

    public Controls Decide(GameEngine g)
    {
        var me = g.Player;
        var ahead = g.Track.At(me.D + 90);
        double goal = ahead.Nearest(me.X);
        double speed = me.Boat ? 380 : 430;
        var van = g.Vehicles.FirstOrDefault(v => v.Kind == VehicleKind.Van && !v.Leaving && v.D > me.D);
        if (van is not null)
        {
            goal = van.X;
            speed = van.Speed + 90;
        }
        else
        {
            // Steer round whatever is in the way in the next stretch of road.
            foreach (var v in g.Vehicles.Where(v => v.D > me.D - 10 && v.D - me.D < 260).OrderBy(v => v.D))
            {
                if (Math.Abs(v.X - goal) > v.Width / 2 + 26) continue;
                double left = v.X - v.Width / 2 - 30, right = v.X + v.Width / 2 + 30;
                bool leftOk = ahead.Holds(left, 16), rightOk = ahead.Holds(right, 16);
                goal = leftOk && (!rightOk || Math.Abs(left - me.X) < Math.Abs(right - me.X)) ? left : rightOk ? right : goal;
                if (!leftOk && !rightOk) speed = Math.Min(speed, v.Speed - 30);
                break;
            }
            // Bombs about to land, drums and craters.
            foreach (var h in g.Hazards.Where(h => h.Kind is HazardKind.Bomb or HazardKind.Crater or HazardKind.Drum && h.D - me.D is > -20 and < 320))
            {
                if (Math.Abs(h.X - goal) > h.Radius + 24) continue;
                double left = h.X - h.Radius - 30, right = h.X + h.Radius + 30;
                goal = ahead.Holds(left, 16) && (Math.Abs(left - me.X) < Math.Abs(right - me.X) || !ahead.Holds(right, 16)) ? left : right;
            }
            // Keep away from a razorwheel's blades and a bulldog's flank.
            foreach (var v in g.Vehicles.Where(v => v.Kind is VehicleKind.Razorwheel or VehicleKind.Bulldog && Math.Abs(v.D - me.D) < 70))
                goal += v.X < me.X ? 30 : -30;
        }
        // A chaser alongside: brake and let it go by, then shoot it from behind.
        var alongside = g.Vehicles.FirstOrDefault(v => v.Enemy && !v.Boat && v.D - me.D is > -50 and < 60 && Math.Abs(v.X - me.X) < 90);
        if (alongside is not null)
        {
            // A razorwheel before its blades are out, or a sedan: shove it off the road. Anything else: let it by.
            bool ram = alongside.Kind == VehicleKind.Sedan || alongside.Kind == VehicleKind.Razorwheel && !alongside.Blades;
            if (ram && Math.Abs(alongside.D - me.D) < 30) goal = alongside.X;
            else
            {
                // Blades out, or a bulldog: swerve away from it and put your foot down.
                goal = me.X + (alongside.X < me.X ? 70 : -70);
                speed = me.Boat ? GameEngine.BoatSpeed : GameEngine.MaxSpeed;
            }
        }
        // A torpedo coming up from behind: get out of its line.
        foreach (var s in g.Shots.Where(s => s.Hostile && s.Kind == ShotKind.Torpedo && s.D < me.D && Math.Abs(s.X - goal) < 26))
            goal += s.X < me.X ? 40 : -40;
        goal = g.Track.At(me.D + 40).Nearest(goal);
        int dx = Math.Abs(goal - me.X) > 6 ? Math.Sign(goal - me.X) : 0;
        int dy = me.Speed < speed - 20 ? -1 : me.Speed > speed + 40 ? 1 : 0;
        // Fire at an enemy dead ahead, unless an innocent is in the line of fire.
        bool fire = false;
        var inLine = g.Vehicles.Where(v => v.D > me.D && v.D - me.D < 600 && Math.Abs(v.X - me.X) < v.Width / 2 + 8).OrderBy(v => v.D).FirstOrDefault();
        if (inLine is not null && inLine.Enemy) fire = _rng.NextDouble() > 0.05;
        if (g.Hazards.Any(h => h.Kind == HazardKind.Drum && h.D > me.D && h.D - me.D < 400 && Math.Abs(h.X - me.X) < 14)) fire = true;
        // The special weapon: missiles for the helicopter, oil and smoke for whatever is close behind.
        bool special = false;
        if (me.Special == Special.Missiles && (g.Copter is not null || inLine is { Enemy: true })) special = true;
        if (me.Special is Special.Oil or Special.Smoke && g.Vehicles.Any(v => v.Enemy && v.D < me.D && me.D - v.D < 160 && Math.Abs(v.X - me.X) < 40)) special = true;
        special = special && !_specialHeld;
        _specialHeld = special;
        return new Controls(dx, dy, fire, special);
    }
}
