namespace AVASPYHunter.Engine;

public enum Surface { Road, Water }

/// <summary>The four stretches of country the road runs through, in turn.</summary>
public enum Zone { Coast, Pines, Winter, Night }

/// <summary>
/// The road (or river) at one point: one carriageway, or two either side of a median where it forks;
/// a bridge has water either side.
/// </summary>
public readonly record struct Lanes(double L1, double R1, double L2, double R2, bool Split, Surface Surface, bool Bridge)
{
    public double Centre => Split ? (L1 + R2) / 2 : (L1 + R1) / 2;
    public double Left => L1;
    public double Right => Split ? R2 : R1;

    /// <summary>Whether a vehicle of a given half-width at x is wholly on a carriageway.</summary>
    public bool Holds(double x, double half) =>
        x - half >= L1 && x + half <= R1 || Split && x - half >= L2 && x + half <= R2;

    /// <summary>The middle of the carriageway nearest x.</summary>
    public double Nearest(double x)
    {
        if (!Split) return Math.Clamp(x, L1 + 20, R1 - 20);
        double a = Math.Clamp(x, L1 + 20, R1 - 20), b = Math.Clamp(x, L2 + 20, R2 - 20);
        return Math.Abs(a - x) <= Math.Abs(b - x) ? a : b;
    }
}

/// <summary>
/// The course: a road that winds, narrows, widens and forks round a median, crosses bridges, and now
/// and then runs into a boathouse where the car becomes a boat for a stretch of river. It is laid
/// down from a seed as the player drives, a key point at a time; between key points the road eases
/// from one shape to the next.
/// </summary>
public sealed class Track
{
    public const double ZoneLength = 24000;
    public const double StartWidth = 300;

    private sealed record Key(double D, double Centre, double Width, double Gap, Surface Surface, bool Bridge);

    private readonly List<Key> _keys = [];
    private readonly Random _rng;
    private double _nextRiver;

    public Track(int seed)
    {
        _rng = new Random(seed);
        _keys.Add(new Key(-2000, Field.Width / 2, StartWidth, 0, Surface.Road, false));
        _keys.Add(new Key(1500, Field.Width / 2, StartWidth, 0, Surface.Road, false));
        _nextRiver = 14000;
    }

    public static Zone ZoneAt(double d) => (Zone)((int)Math.Max(0, d / ZoneLength) % 4);
    public static int Sector(double d) => (int)Math.Max(0, d / ZoneLength) + 1;

    private double R(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    private void Add(double length, double centre, double width, double gap = 0, Surface surface = Surface.Road, bool bridge = false)
    {
        var last = _keys[^1];
        _keys.Add(new Key(last.D + length, Math.Clamp(centre, width / 2 + 24, Field.Width - width / 2 - 24), width, gap, surface, bridge));
    }

    /// <summary>Lays down more road until it reaches past a distance.</summary>
    private void Extend(double upTo)
    {
        while (_keys[^1].D < upTo)
        {
            var last = _keys[^1];
            double c = last.Centre;
            if (last.D > _nextRiver)
            {
                // A boathouse, a stretch of river, and a landing.
                _nextRiver = last.D + R(16000, 26000);
                Add(500, Field.Width / 2, 260);
                Add(1, Field.Width / 2, 300, 0, Surface.Water);
                double river = R(3000, 4200);
                for (double done = 0; done < river; done += 900)
                    Add(900, Field.Width / 2 + R(-80, 80), R(320, 400), 0, Surface.Water);
                Add(400, Field.Width / 2, 300, 0, Surface.Water);
                Add(1, Field.Width / 2, 260);
                Add(500, Field.Width / 2, StartWidth);
                continue;
            }
            switch (_rng.Next(7))
            {
                case 0 or 1:
                    Add(R(700, 1300), c + R(-130, 130), last.Width);
                    break;
                case 2:
                    Add(R(500, 800), c, R(190, 230));
                    Add(R(500, 900), c + R(-60, 60), R(190, 230));
                    Add(R(400, 600), c, StartWidth);
                    break;
                case 3:
                    // A fork: the road widens round a median, then joins again.
                    Add(R(400, 500), Field.Width / 2, 430, 100);
                    Add(R(1200, 1800), Field.Width / 2, 440, 110);
                    Add(R(400, 500), Field.Width / 2, StartWidth);
                    break;
                case 4:
                    // A bridge: narrow, with water either side.
                    Add(R(300, 400), c, 200);
                    Add(1, c, 190, 0, Surface.Road, bridge: true);
                    Add(R(700, 1000), c, 190, 0, Surface.Road, bridge: true);
                    Add(1, c, 200);
                    Add(R(300, 400), c, StartWidth);
                    break;
                default:
                    Add(R(600, 1000), c + R(-50, 50), R(280, 360));
                    break;
            }
        }
    }

    private int _cursor;

    public Lanes At(double d)
    {
        Extend(d + 3000);
        if (_cursor >= _keys.Count - 1 || _keys[_cursor].D > d) _cursor = 0;
        while (_cursor < _keys.Count - 2 && _keys[_cursor + 1].D <= d) _cursor++;
        var a = _keys[_cursor];
        var b = _keys[_cursor + 1];
        double t = b.D <= a.D ? 1 : Math.Clamp((d - a.D) / (b.D - a.D), 0, 1);
        t = t * t * (3 - 2 * t);
        double centre = a.Centre + (b.Centre - a.Centre) * t;
        double width = a.Width + (b.Width - a.Width) * t;
        double gap = a.Gap + (b.Gap - a.Gap) * t;
        bool split = gap > 8;
        double left = centre - width / 2, right = centre + width / 2;
        if (!split) return new Lanes(left, right, 0, 0, false, a.Surface, a.Bridge);
        return new Lanes(left, centre - gap / 2, centre + gap / 2, right, true, a.Surface, a.Bridge);
    }

    /// <summary>Where the surface changes between two distances (a boathouse or a landing), if it does.</summary>
    public double? Crossing(double from, double to)
    {
        Extend(to + 3000);
        foreach (var k in _keys)
        {
            if (k.D <= from || k.D > to) continue;
            int i = _keys.IndexOf(k);
            if (i > 0 && _keys[i - 1].Surface != k.Surface) return k.D;
        }
        return null;
    }
}
