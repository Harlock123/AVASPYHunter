namespace AVASPYHunter.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public static class Field
{
    public const double Step = 1.0 / 60;
    /// <summary>The playfield: a tall strip of road down the middle of the screen.</summary>
    public const double Width = 576, Height = 768;
    /// <summary>Where the playfield starts across the 1024-wide screen; the panels sit either side.</summary>
    public const double Left = 224;
    /// <summary>Where the player's car sits on the screen, near the bottom: the road comes down at it.</summary>
    public const double PlayerY = 590;

    /// <summary>Where a point a distance down the road shows on the screen, given where the player is.</summary>
    public static double ScreenY(double d, double playerD) => PlayerY - (d - playerD);
}
