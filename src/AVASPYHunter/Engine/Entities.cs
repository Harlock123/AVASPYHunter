namespace AVASPYHunter.Engine;

/// <summary>The stick: steer left and right, speed up and slow down; the guns; and the special weapon.</summary>
public record struct Controls(int Dx, int Dy, bool Fire = false, bool Special = false);

public enum VehicleKind
{
    /// <summary>Ordinary drivers going about their day: never harm them.</summary>
    Civilian,
    /// <summary>Pulls alongside and runs blades out of its hubs to shred your tyres.</summary>
    Razorwheel,
    /// <summary>A heavy armoured car that rams: bullets bounce off it, so bump it off the road.</summary>
    Bulldog,
    /// <summary>A long sedan with a gunman leaning out of the window.</summary>
    Sedan,
    /// <summary>The armoury van: drive up its ramp to take on a special weapon.</summary>
    Van,
    /// <summary>On the river: pleasure boats, not to be harmed.</summary>
    Launch,
    /// <summary>On the river: a barge that rolls drums into the water behind it.</summary>
    DrumBoat,
    /// <summary>On the river: a fast boat that fires torpedoes up the river.</summary>
    TorpedoBoat,
}

/// <summary>A vehicle on the road or the river, at a distance <see cref="D"/> along it.</summary>
public sealed class Vehicle
{
    public VehicleKind Kind;
    public double X;
    public double D;
    public double Speed;
    public double Vx;
    public int Hits = 1;
    public uint Colour;
    /// <summary>For a razorwheel: its blades are out.</summary>
    public bool Blades;
    public double FireTimer;
    /// <summary>Spinning out of control (oil, smoke, a bad bump): it crashes when this runs out.</summary>
    public double Spin;
    /// <summary>Seconds since the player last bumped it, so a crash it causes is credited.</summary>
    public double Bumped = 99;
    /// <summary>Which side of the player it wants to be (-1 left, +1 right).</summary>
    public int Side = 1;
    public double Age;
    public bool Dead;
    public double Flash;
    public bool Leaving;

    public bool Boat => Kind is VehicleKind.Launch or VehicleKind.DrumBoat or VehicleKind.TorpedoBoat;
    public bool Innocent => Kind is VehicleKind.Civilian or VehicleKind.Launch;
    public bool Enemy => !Innocent && Kind != VehicleKind.Van;

    public double Width => Kind switch { VehicleKind.Van => 40, VehicleKind.Bulldog => 34, VehicleKind.DrumBoat => 40, VehicleKind.Sedan => 30, _ => 28 };
    public double Length => Kind switch { VehicleKind.Van => 80, VehicleKind.Sedan => 60, VehicleKind.Bulldog => 54, VehicleKind.DrumBoat => 70, VehicleKind.TorpedoBoat => 56, _ => 48 };
    /// <summary>How hard it is to shove aside.</summary>
    public double Mass => Kind switch { VehicleKind.Van => 6, VehicleKind.Bulldog => 3.5, VehicleKind.Sedan => 1.4, VehicleKind.DrumBoat => 3, _ => 1 };
}

public enum ShotKind { Bullet, Missile, Gunman, Torpedo }

public sealed class Shot
{
    public ShotKind Kind;
    public double X;
    public double D;
    public double Vx;
    /// <summary>Speed along the road (positive is up the screen).</summary>
    public double Vd;
    public bool Hostile;
    public bool Dead;
    public double Age;
}

public enum HazardKind
{
    /// <summary>A slick of oil left behind: whoever drives over it spins out.</summary>
    Oil,
    /// <summary>A cloud of smoke left behind: whoever drives into it loses the road.</summary>
    Smoke,
    /// <summary>A drum floating on the river.</summary>
    Drum,
    /// <summary>A bomb falling from the helicopter: where it lands, it explodes.</summary>
    Bomb,
    /// <summary>A hole the bomb left in the road.</summary>
    Crater,
}

public sealed class Hazard
{
    public HazardKind Kind;
    public double X;
    public double D;
    public double Age;
    public double Life;
    public bool Dead;
    public double Radius => Kind switch { HazardKind.Smoke => 46, HazardKind.Oil => 22, HazardKind.Crater => 20, HazardKind.Bomb => 38, _ => 12 };
}

/// <summary>The helicopter: it hangs ahead of the player and drops bombs on the road. Only missiles can reach it.</summary>
public sealed class Copter
{
    public double X;
    /// <summary>How far ahead of the player it hangs.</summary>
    public double Ahead = 520;
    public double BombTimer = 2;
    public double Age;
    public int Hits = 2;
    public bool Leaving;
    public bool Dead;
    public double Flash;
}

/// <summary>A special weapon from the armoury van.</summary>
public enum Special { None, Oil, Smoke, Missiles }

public enum EffectKind { Explosion, BigExplosion, Score, Splash, Spark }

public sealed class Effect
{
    public EffectKind Kind;
    /// <summary>Across the road, and the distance along it (effects scroll with the road).</summary>
    public double X;
    public double D;
    public double Age;
    public double Life = 0.6;
    public string Text = "";
    public bool Done => Age >= Life;
}
