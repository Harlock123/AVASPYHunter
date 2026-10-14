using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASPYHunter.Engine;

namespace AVASPYHunter.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASPYHunter/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Cyan = 0xFF4FD7FF, Dim = 0xFF9AA2B8, Orange = 0xFFFF9A2A, Green = 0xFF70E070;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return 0xFF000000 | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, 1.5) : null, g);
    }

    /// <summary>Where a point on the road shows on the screen.</summary>
    private double Y(double d) => Field.ScreenY(d, Engine.Player.D);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        using (dc.PushClip(new Rect(Field.Left, 0, Field.Width, Field.Height)))
        using (dc.PushTransform(Matrix.CreateTranslation(Field.Left, 0)))
        {
            DrawRoad(dc);
            bool driving = g.State is GameState.Playing or GameState.Paused or GameState.Attract or GameState.GameOver;
            if (driving) DrawPlay(dc);
            if (g.Zone == Zone.Night) DrawNight(dc, driving);
            foreach (var e in g.Effects) DrawEffect(dc, e);
            switch (g.State)
            {
                case GameState.Title: DrawTitle(dc); break;
                case GameState.HighScores: DrawHighScores(dc); break;
                case GameState.EnterInitials: DrawInitials(dc); break;
                default: DrawMessages(dc); break;
            }
            if (g.State == GameState.Paused)
            {
                dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
                Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
                Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
            }
            if (g.State == GameState.GameOver) Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 90, Red, center: true);
            if (g.State == GameState.Attract)
            {
                Text(dc, "DEMONSTRATION", new Point(Field.Width / 2, 300), 40, Yellow, center: true);
                Text(dc, "PRESS ENTER TO PLAY", new Point(Field.Width / 2, 336), 26, White, center: true);
            }
        }
        DrawPanels(dc);
    }

    // ───────────────────────────── the road ─────────────────────────────

    private static (uint Verge, uint VergeDark, uint Road, uint Line) Palette(Zone z) => z switch
    {
        Zone.Pines => (0xFF3E6A2E, 0xFF325A26, 0xFF55565C, 0xFFE8E0C0),
        Zone.Winter => (0xFFE6ECF2, 0xFFCAD4DE, 0xFF6A7480, 0xFFF4F8FF),
        Zone.Night => (0xFF1E4A24, 0xFF183E1E, 0xFF3A3C44, 0xFFE8E0A0),
        _ => (0xFF5AA848, 0xFF4E9640, 0xFF5E5E66, 0xFFF0F0F0),
    };

    private const uint WaterDeep = 0xFF2A5A9A, WaterLight = 0xFF3A70B0;

    private void DrawRoad(DrawingContext dc)
    {
        var g = Engine;
        double pd = g.Player.D;
        const double band = 6;
        for (double y = 0; y < Field.Height; y += band)
        {
            double d = pd + (Field.PlayerY - y);
            var lanes = g.Track.At(d);
            var (verge, vergeDark, road, _) = Palette(Track.ZoneAt(d));
            bool stripe = ((int)Math.Floor(d / 48)) % 2 == 0;
            uint outside = lanes.Bridge ? (stripe ? WaterDeep : WaterLight) : stripe ? verge : vergeDark;
            dc.FillRectangle(Brush(outside), new Rect(0, y, Field.Width, band + 0.5));
            uint surface = lanes.Surface == Surface.Water ? (((int)Math.Floor((d + g.StateTime * 40) / 30)) % 2 == 0 ? WaterDeep : WaterLight) : road;
            void Carriageway(double l, double r)
            {
                if (lanes.Surface == Surface.Water)
                {
                    dc.FillRectangle(Brush(0xFFC8B880), new Rect(l - 8, y, r - l + 16, band + 0.5));
                    dc.FillRectangle(Brush(surface), new Rect(l, y, r - l, band + 0.5));
                    return;
                }
                dc.FillRectangle(Brush(surface), new Rect(l, y, r - l, band + 0.5));
                // Kerbs, red and white, and on a bridge its rails.
                bool kerb = ((int)Math.Floor(d / 24)) % 2 == 0;
                uint k = lanes.Bridge ? 0xFFA0A4AC : kerb ? 0xFFD83A30 : 0xFFF0F0F0;
                dc.FillRectangle(Brush(k), new Rect(l - 5, y, 5, band + 0.5));
                dc.FillRectangle(Brush(k), new Rect(r, y, 5, band + 0.5));
            }
            Carriageway(lanes.L1, lanes.R1);
            if (lanes.Split) Carriageway(lanes.L2, lanes.R2);
            // The boathouse and the landing, across the road where it meets the water.
            var next = g.Track.At(d + band);
            if (next.Surface != lanes.Surface)
            {
                dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(lanes.Left - 30, y - 30, lanes.Right - lanes.Left + 60, 30));
                for (double x = lanes.Left - 26; x < lanes.Right + 26; x += 14) dc.FillRectangle(Brush(0xFF8A6438), new Rect(x, y - 28, 10, 26));
                Text(dc, next.Surface == Surface.Water ? "BOATHOUSE" : "LANDING", new Point((lanes.Left + lanes.Right) / 2, y - 16), 22, Yellow, center: true);
            }
        }
        // Lane markings: a dashed line down each carriageway.
        double first = Math.Floor((pd - 200) / 60) * 60;
        for (double d = first; d < pd + 800; d += 60)
        {
            var lanes = g.Track.At(d);
            if (lanes.Surface == Surface.Water) continue;
            uint line = Palette(Track.ZoneAt(d)).Line;
            double y = Y(d);
            dc.FillRectangle(Brush(line), new Rect((lanes.L1 + lanes.R1) / 2 - 2, y - 14, 4, 28));
            if (lanes.Split) dc.FillRectangle(Brush(line), new Rect((lanes.L2 + lanes.R2) / 2 - 2, y - 14, 4, 28));
        }
        DrawScenery(dc);
    }

    /// <summary>Trees, rocks and houses by the roadside (or reeds by the river), placed by the road's own distance.</summary>
    private void DrawScenery(DrawingContext dc)
    {
        var g = Engine;
        double pd = g.Player.D;
        const double step = 70;
        double first = Math.Floor((pd - 260) / step) * step;
        for (double d = first; d < pd + 820; d += step)
        {
            var lanes = g.Track.At(d);
            if (lanes.Bridge) continue;
            var zone = Track.ZoneAt(d);
            for (int side = 0; side < 2; side++)
            {
                uint h = Hash((int)(d / step), side, 31);
                if (h % 3 == 0) continue;
                double room = side == 0 ? lanes.Left - 28 : Field.Width - lanes.Right - 28;
                if (room < 30) continue;
                double x = side == 0 ? 10 + (h >> 4) % (uint)(room - 16) : lanes.Right + 22 + (h >> 4) % (uint)(room - 16);
                double y = Y(d) + (h >> 12) % 40;
                if (lanes.Surface == Surface.Water)
                {
                    dc.DrawLine(Pen(0xFF6A8A3A, 2), new Point(x, y), new Point(x - 3, y - 14));
                    dc.DrawLine(Pen(0xFF7A9A4A, 2), new Point(x + 4, y), new Point(x + 6, y - 12));
                    continue;
                }
                switch (zone)
                {
                    case Zone.Pines or Zone.Winter:
                    {
                        uint needles = zone == Zone.Winter ? 0xFF2A4A3A : 0xFF1E4A22;
                        Poly(dc, needles, 0xFF102814, new Point(x, y - 30), new Point(x + 16, y + 4), new Point(x - 16, y + 4));
                        Poly(dc, Shade(needles, 1.2), null, new Point(x, y - 38), new Point(x + 11, y - 12), new Point(x - 11, y - 12));
                        if (zone == Zone.Winter) Poly(dc, 0xFFF4F8FF, null, new Point(x, y - 38), new Point(x + 5, y - 28), new Point(x - 5, y - 28));
                        break;
                    }
                    default:
                        if (h % 7 == 1)
                        {
                            // A house by the road.
                            dc.FillRectangle(Brush(0xFFE8D8B8), new Rect(x - 16, y - 12, 32, 24));
                            Poly(dc, 0xFFB04A30, 0xFF5A2418, new Point(x - 20, y - 12), new Point(x, y - 26), new Point(x + 20, y - 12));
                        }
                        else
                        {
                            uint leaf = zone == Zone.Night ? 0xFF143A1A : 0xFF2E7A2A;
                            dc.DrawEllipse(Brush(Shade(leaf, 0.7)), null, new Point(x + 3, y + 3), 15, 15);
                            dc.DrawEllipse(Brush(leaf), null, new Point(x, y), 14, 14);
                            dc.DrawEllipse(Brush(Shade(leaf, 1.3)), null, new Point(x - 4, y - 4), 6, 6);
                        }
                        break;
                }
            }
        }
    }

    // ───────────────────────────── vehicles ─────────────────────────────

    private void DrawPlay(DrawingContext dc)
    {
        var g = Engine;
        foreach (var h in g.Hazards) DrawHazard(dc, h);
        foreach (var v in g.Vehicles) DrawVehicle(dc, v);
        foreach (var s in g.Shots) DrawShot(dc, s);
        bool hidden = g.Phase == Phase.Docking || g.Phase == Phase.Crash;
        if (!hidden) DrawPlayer(dc);
        if (g.Copter is { } c) DrawCopter(dc, c);
    }

    /// <summary>A car from above: a body, a windscreen and roof, and four wheels.</summary>
    private static void Car(DrawingContext dc, double x, double y, double w, double l, uint body, double spin = 0, bool flash = false)
    {
        using var t = dc.PushTransform(Matrix.CreateRotation(spin) * Matrix.CreateTranslation(x, y));
        uint b = flash ? 0xFFFFFFFF : body;
        foreach (var (wx, wy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            dc.FillRectangle(Brush(0xFF141414), new Rect(wx * w / 2 - 4 + (wx > 0 ? 0 : 0), wy * l * 0.3 - 6, 8, 12));
        dc.DrawRectangle(Brush(b), Pen(0xFF101010, 1.5), new Rect(-w / 2 + 2, -l / 2, w - 4, l), 6, 6);
        dc.DrawRectangle(Brush(0xFF1A2A3A), null, new Rect(-w / 2 + 5, -l / 2 + l * 0.2, w - 10, l * 0.14), 3, 3);
        dc.FillRectangle(Brush(Shade(b, 1.2)), new Rect(-w / 2 + 6, -l / 2 + l * 0.36, w - 12, l * 0.3));
        dc.DrawRectangle(Brush(0xFF1A2A3A), null, new Rect(-w / 2 + 5, -l / 2 + l * 0.68, w - 10, l * 0.1), 2, 2);
        dc.FillRectangle(Brush(0xFFFFF0B0), new Rect(-w / 2 + 4, -l / 2, 5, 3));
        dc.FillRectangle(Brush(0xFFFFF0B0), new Rect(w / 2 - 9, -l / 2, 5, 3));
        dc.FillRectangle(Brush(0xFFD02020), new Rect(-w / 2 + 4, l / 2 - 3, 5, 3));
        dc.FillRectangle(Brush(0xFFD02020), new Rect(w / 2 - 9, l / 2 - 3, 5, 3));
    }

    private static void Boat(DrawingContext dc, double x, double y, double w, double l, uint body, double t, bool flash = false)
    {
        // The wake.
        Poly(dc, 0x50FFFFFF, null, new Point(x - w / 3, y + l / 2), new Point(x - w, y + l / 2 + 60), new Point(x + w, y + l / 2 + 60), new Point(x + w / 3, y + l / 2));
        uint b = flash ? 0xFFFFFFFF : body;
        Poly(dc, b, 0xFF101010, new Point(x, y - l / 2), new Point(x + w / 2, y - l / 6), new Point(x + w / 2, y + l / 2), new Point(x - w / 2, y + l / 2), new Point(x - w / 2, y - l / 6));
        dc.FillRectangle(Brush(Shade(b, 0.7)), new Rect(x - w / 4, y - l / 8, w / 2, l / 4));
        double bob = Math.Sin(t * 6 + x) * 1.5;
        dc.FillRectangle(Brush(0x60FFFFFF), new Rect(x - w / 2 - 2, y + l / 2 - 4 + bob, w + 4, 3));
    }

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        if (p.Invulnerable > 0 && g.Phase == Phase.Playing && (int)(_clock * 12) % 2 == 0) return;
        double y = Field.PlayerY;
        if (p.Boat) Boat(dc, p.X, y, PlayerCar.Width + 4, PlayerCar.Length + 6, 0xFFF0F0F4, _clock);
        else
        {
            Car(dc, p.X, y, PlayerCar.Width, PlayerCar.Length, 0xFFE8ECF0, p.Vx / 2000);
            dc.FillRectangle(Brush(0xFF2A5AD8), new Rect(p.X - 2, y - PlayerCar.Length / 2 + 1, 4, PlayerCar.Length - 2));
        }
        if (p.Special != Special.None && (int)(_clock * 3) % 2 == 0)
            dc.DrawEllipse(null, Pen(p.Special switch { Special.Oil => 0xFF202020, Special.Smoke => 0xFFB0B0B0, _ => Orange }, 2), new Point(p.X, y), 30, 34);
    }

    private void DrawVehicle(DrawingContext dc, Vehicle v)
    {
        double y = Y(v.D);
        if (y < -120 || y > Field.Height + 120) return;
        double spin = v.Spin > 0 ? (0.9 - v.Spin) * 9 : v.Vx / 2200;
        bool flash = v.Flash > 0;
        switch (v.Kind)
        {
            case VehicleKind.Civilian:
                Car(dc, v.X, y, v.Width, v.Length, v.Colour, spin, flash);
                break;
            case VehicleKind.Razorwheel:
                if (v.Blades)
                    foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    {
                        double bx = v.X + sx * (v.Width / 2 + 2), by = y + sy * v.Length * 0.3;
                        double spinB = _clock * 30;
                        for (int k = 0; k < 3; k++)
                        {
                            double a = spinB + k * 2.1;
                            Poly(dc, 0xFFD0D4DC, 0xFF404040, new Point(bx, by), new Point(bx + sx * 12 + Math.Cos(a) * 3, by + Math.Sin(a) * 7), new Point(bx + sx * 4, by + 4));
                        }
                    }
                Car(dc, v.X, y, v.Width, v.Length, 0xFFB02A2A, spin, flash);
                break;
            case VehicleKind.Bulldog:
                Car(dc, v.X, y, v.Width, v.Length, 0xFF2A2C34, spin, flash);
                for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFF8A8C94), new Rect(v.X - v.Width / 2 + 4, y - v.Length / 2 + 8 + k * 11, v.Width - 8, 2));
                break;
            case VehicleKind.Sedan:
                Car(dc, v.X, y, v.Width, v.Length, 0xFF1E2A5A, spin, flash);
                if (v.FireTimer < 0.7)
                {
                    double gx = v.X - v.Side * (v.Width / 2 + 6);
                    dc.FillRectangle(Brush(0xFFE0B080), new Rect(Math.Min(gx, v.X - v.Side * v.Width / 2), y - 4, Math.Abs(gx - (v.X - v.Side * v.Width / 2)), 6));
                    dc.FillRectangle(Brush(0xFF202020), new Rect(gx - 5, y - 2, 10, 3));
                }
                break;
            case VehicleKind.Van:
            {
                // A cab, a long box, and the ramp at the back with its chevrons.
                Car(dc, v.X, y - v.Length / 2 + 14, v.Width - 4, 28, 0xFFE8E8E8, spin, flash);
                dc.DrawRectangle(Brush(0xFF6A6E78), Pen(0xFF202020, 2), new Rect(v.X - v.Width / 2, y - v.Length / 2 + 26, v.Width, v.Length - 26), 3, 3);
                Text(dc, "ARMOURY", new Point(v.X, y + 8), 13, Yellow, center: true);
                for (int k = 0; k < 3; k++)
                    Poly(dc, Yellow, null, new Point(v.X - 12, y + v.Length / 2 - 4 - k * 7), new Point(v.X, y + v.Length / 2 - 10 - k * 7), new Point(v.X + 12, y + v.Length / 2 - 4 - k * 7));
                break;
            }
            case VehicleKind.Launch:
                Boat(dc, v.X, y, v.Width, v.Length, v.Colour, _clock, flash);
                break;
            case VehicleKind.DrumBoat:
                Boat(dc, v.X, y, v.Width, v.Length, 0xFF7A5A3A, _clock, flash);
                for (int k = 0; k < 3; k++) dc.DrawEllipse(Brush(0xFFC03020), Pen(0xFF601010, 1.5), new Point(v.X - 10 + k * 10, y + 8), 5, 5);
                break;
            case VehicleKind.TorpedoBoat:
                Boat(dc, v.X, y, v.Width, v.Length, 0xFF5A6068, _clock, flash);
                dc.FillRectangle(Brush(0xFF202428), new Rect(v.X - 9, y - v.Length / 2 + 10, 4, 18));
                dc.FillRectangle(Brush(0xFF202428), new Rect(v.X + 5, y - v.Length / 2 + 10, 4, 18));
                break;
        }
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        double y = Y(s.D);
        switch (s.Kind)
        {
            case ShotKind.Bullet:
                dc.FillRectangle(Brush(Yellow), new Rect(s.X - 1.5, y - 8, 3, 12));
                break;
            case ShotKind.Gunman:
                dc.DrawEllipse(Brush(0xFFFFB040), Pen(0xFF803000, 1), new Point(s.X, y), 4, 4);
                break;
            case ShotKind.Missile:
                dc.FillRectangle(Brush(0xFFE8E8E8), new Rect(s.X - 3, y - 10, 6, 18));
                Poly(dc, Red, null, new Point(s.X - 3, y - 10), new Point(s.X, y - 16), new Point(s.X + 3, y - 10));
                dc.DrawEllipse(Brush(0xC0FFA030), null, new Point(s.X, y + 12 + Math.Sin(_clock * 40) * 2), 4, 7);
                break;
            case ShotKind.Torpedo:
                dc.DrawRectangle(Brush(0xFF2A2E34), Pen(0xFF101010, 1), new Rect(s.X - 4, y - 12, 8, 24), 4, 4);
                for (int k = 1; k < 4; k++) dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(s.X + Math.Sin(k + _clock * 9) * 3, y + 12 + k * 9), 2.5, 2.5);
                break;
        }
    }

    private void DrawHazard(DrawingContext dc, Hazard h)
    {
        double y = Y(h.D);
        if (y < -80 || y > Field.Height + 80) return;
        var at = new Point(h.X, y);
        switch (h.Kind)
        {
            case HazardKind.Oil:
                dc.DrawEllipse(Brush(0xE0101014), null, at, h.Radius, h.Radius * 0.75);
                dc.DrawEllipse(Brush(0x506070A0), null, new Point(at.X - 6, at.Y - 4), 8, 4);
                break;
            case HazardKind.Smoke:
            {
                double grow = 0.6 + Math.Min(1, h.Age * 1.5) * 0.6;
                uint a = (uint)(Math.Clamp(1 - h.Age / h.Life, 0, 1) * 0xC0) << 24;
                for (int k = 0; k < 6; k++)
                {
                    double ang = k * 1.05 + h.Age;
                    dc.DrawEllipse(Brush(a | 0x00B8B8C0), null, new Point(at.X + Math.Cos(ang) * 20 * grow, at.Y + Math.Sin(ang) * 16 * grow), 26 * grow, 22 * grow);
                }
                break;
            }
            case HazardKind.Drum:
                dc.DrawEllipse(Brush(0xFFC03020), Pen(0xFF501010, 2), at, 10, 10);
                dc.DrawEllipse(null, Pen(0xFFE8A040, 1.5), at, 5, 5);
                break;
            case HazardKind.Crater:
                dc.DrawEllipse(Brush(0xFF3A2A1A), Pen(0xFF6A5A4A, 3), at, h.Radius, h.Radius * 0.8);
                break;
            case HazardKind.Bomb:
            {
                // The bomb's shadow grows on the road as it falls.
                double t = Math.Clamp(h.Age / 0.9, 0, 1);
                dc.DrawEllipse(Brush(0x60000000), null, at, 8 + 22 * t, (8 + 22 * t) * 0.7);
                double fall = (1 - t) * 160;
                dc.DrawEllipse(Brush(0xFF202020), Pen(0xFF505050, 1), new Point(at.X, at.Y - fall), 7, 9);
                if ((int)(_clock * 8) % 2 == 0) dc.DrawEllipse(null, Pen(Red, 2), at, 34, 26);
                break;
            }
        }
    }

    /// <summary>The helicopter: its shadow on the road beneath, and the machine itself above.</summary>
    private void DrawCopter(DrawingContext dc, Copter c)
    {
        var p = Engine.Player;
        double ground = Y(p.D + c.Ahead);
        dc.DrawEllipse(Brush(0x50000000), null, new Point(c.X + 30, ground + 40), 30, 14);
        double y = ground - 60;
        uint body = c.Flash > 0 ? 0xFFFFFFFF : 0xFF3A4A2A;
        dc.FillRectangle(Brush(body), new Rect(c.X - 3, y - 50, 6, 44));
        dc.DrawRectangle(Brush(body), Pen(0xFF101010, 2), new Rect(c.X - 18, y - 16, 36, 46), 16, 16);
        dc.DrawEllipse(Brush(0xFF70B8E0), null, new Point(c.X, y + 18), 11, 9);
        dc.FillRectangle(Brush(0xFF101010), new Rect(c.X - 26, y + 26, 52, 3));
        double a = _clock * 26;
        for (int k = 0; k < 2; k++)
        {
            double ang = a + k * Math.PI / 2;
            dc.DrawLine(Pen(0xC0202020, 4), new Point(c.X - Math.Cos(ang) * 46, y + 6 - Math.Sin(ang) * 46), new Point(c.X + Math.Cos(ang) * 46, y + 6 + Math.Sin(ang) * 46));
        }
        dc.DrawLine(Pen(0xC0202020, 2), new Point(c.X - 10, y - 48), new Point(c.X + 10, y - 48));
    }

    /// <summary>Night: everything dark but the cone of the headlights and the lights of other cars.</summary>
    private void DrawNight(DrawingContext dc, bool driving)
    {
        var p = Engine.Player;
        var dark = new StreamGeometry();
        using (var ctx = dark.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            ctx.BeginFigure(new Point(0, 0), true);
            ctx.LineTo(new Point(Field.Width, 0));
            ctx.LineTo(new Point(Field.Width, Field.Height));
            ctx.LineTo(new Point(0, Field.Height));
            ctx.EndFigure(true);
            if (driving)
            {
                double y = Field.PlayerY - 20;
                ctx.BeginFigure(new Point(p.X - 12, y), true);
                ctx.LineTo(new Point(p.X - 150, y - 430));
                ctx.LineTo(new Point(p.X + 150, y - 430));
                ctx.LineTo(new Point(p.X + 12, y));
                ctx.EndFigure(true);
                // A pool of light round the car itself.
                ctx.BeginFigure(new Point(p.X + 34, Field.PlayerY), true);
                for (int k = 1; k < 12; k++)
                {
                    double a = k * Math.PI / 6;
                    ctx.LineTo(new Point(p.X + Math.Cos(a) * 34, Field.PlayerY + Math.Sin(a) * 40));
                }
                ctx.EndFigure(true);
            }
        }
        dc.DrawGeometry(Brush(0xB0000814), null, dark);
        if (!driving) return;
        foreach (var v in Engine.Vehicles)
        {
            double y = Y(v.D);
            dc.DrawEllipse(Brush(0xE0FF3030), null, new Point(v.X - v.Width / 2 + 6, y + v.Length / 2 - 2), 3, 3);
            dc.DrawEllipse(Brush(0xE0FF3030), null, new Point(v.X + v.Width / 2 - 6, y + v.Length / 2 - 2), 3, 3);
            dc.DrawEllipse(Brush(0x60FFF0B0), null, new Point(v.X, y - v.Length / 2 - 30), 18, 34);
        }
    }

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        var at = new Point(e.X, Y(e.D));
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Explosion or EffectKind.BigExplosion:
            {
                double r = (e.Kind == EffectKind.BigExplosion ? 70 : 30) * Math.Sqrt(t);
                uint a = (uint)((1 - t) * 255) << 24;
                dc.DrawEllipse(Brush(a | 0x00FF6020), null, at, r, r);
                dc.DrawEllipse(Brush(a | 0x00FFE060), null, at, r * 0.55, r * 0.55);
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4 + e.X;
                    var q = new Point(at.X + Math.Cos(ang) * r * 1.4, at.Y + Math.Sin(ang) * r * 1.4);
                    dc.FillRectangle(Brush(a | 0x00402010), new Rect(q.X - 3, q.Y - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Spark:
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, at, 5 * (1 - t) + 1, 5 * (1 - t) + 1);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(at.X, at.Y - t * 30), 26, Yellow, center: true);
                break;
        }
    }

    // ───────────────────────────── words ─────────────────────────────

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Message is { } msg)
        {
            bool warn = msg is "HELICOPTER" || msg.StartsWith("NO SCORE");
            if (!warn || (int)(_clock * 4) % 2 == 0)
                Text(dc, msg, new Point(cx, 200), msg.Length > 18 ? 40 : 60, warn ? Red : Yellow, center: true);
        }
        if (g.Phase == Phase.Crash && g.State is GameState.Playing or GameState.Attract)
        {
            Text(dc, "WRECKED", new Point(cx, 300), 64, Red, center: true);
            if (g.LastDeath is { } why) Text(dc, $"YOU {why.ToUpperInvariant()}", new Point(cx, 350), 28, White, center: true);
            Text(dc, g.Free > 0 ? "FREE TIME: NO CAR LOST" : g.Cars > 0 ? $"{g.Cars} CAR{(g.Cars == 1 ? "" : "S")} LEFT" : "LAST CAR", new Point(cx, 386), 26, g.Free > 0 ? Green : Yellow, center: true);
        }
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "AVA SPY HUNTER", new Point(cx, 90), 84, Yellow, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 146), 26, Dim, center: true);
        Car(dc, cx, 222, 36, 62, 0xFFE8ECF0);
        dc.FillRectangle(Brush(0xFF2A5AD8), new Rect(cx - 3, 192, 6, 60));
        string[] lines =
        [
            "DRIVE THE ROAD AS FAR AS YOU DARE",
            "RUN THE RAIDERS OFF IT - BUT SPARE",
            "THE INNOCENT, OR YOUR SCORE STOPS",
            "",
            "DRIVE UP INTO THE ARMOURY VAN FOR",
            "OIL - SMOKE - MISSILES (X OR SHIFT)",
            "ONLY MISSILES BRING DOWN THE HELICOPTER",
            "",
            "THE BOATHOUSE MAKES YOUR CAR A BOAT",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 300 + i * 30), 25, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 680), 40, Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "BEST AGENTS", new Point(cx, 90), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  SECTOR", new Point(cx, 160), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,2}", new Point(cx, 210 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000010), new Rect(0, 0, Field.Width, Field.Height));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 180), 60, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 250), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 330), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 420), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 460, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 540), 24, White, center: true);
    }

    // ───────────────────────────── the panels either side ─────────────────────────────

    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        foreach (var x in new[] { 0.0, Field.Left + Field.Width })
        {
            dc.FillRectangle(Brush(0xFF10141C), new Rect(x, 0, Field.Left, Field.Height));
            dc.FillRectangle(Brush(0xFF3A4A5A), new Rect(x == 0 ? Field.Left - 3 : x, 0, 3, Field.Height));
        }
        double lx = Field.Left / 2, rx = Field.Left + Field.Width + Field.Left / 2;
        Text(dc, "AVA", new Point(lx, 46), 52, Orange, center: true);
        Text(dc, "SPY HUNTER", new Point(lx, 92), 44, Yellow, center: true);
        Text(dc, "SCORE", new Point(lx, 170), 28, Dim, center: true);
        Text(dc, g.Score.ToString(), new Point(lx, 202), 40, g.NoScore > 0 && (int)(_clock * 4) % 2 == 0 ? Red : White, center: true);
        Text(dc, "HI-SCORE", new Point(lx, 256), 28, Dim, center: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString(), new Point(lx, 288), 40, Yellow, center: true);
        bool driving = g.State is GameState.Playing or GameState.Paused or GameState.Attract or GameState.GameOver;
        if (driving)
        {
            if (g.Free > 0)
            {
                Text(dc, "FREE TIME", new Point(lx, 350), 28, Dim, center: true);
                Text(dc, ((int)Math.Ceiling(g.Free)).ToString(), new Point(lx, 386), 48, Green, center: true);
                Text(dc, "WRECKS COST NOTHING", new Point(lx, 422), 19, Dim, center: true);
            }
            else
            {
                Text(dc, "CARS", new Point(lx, 350), 28, Dim, center: true);
                for (int i = 0; i < Math.Min(g.Cars, 6); i++) Car(dc, lx - 60 + (i % 4) * 40, 396 + i / 4 * 52, 20, 36, 0xFFE8ECF0);
            }
            if (g.NoScore > 0) Text(dc, $"NO SCORE {g.NoScore:0}", new Point(lx, 470), 26, Red, center: true);
            Text(dc, "SECTOR", new Point(lx, 520), 28, Dim, center: true);
            Text(dc, g.Sector.ToString(), new Point(lx, 554), 44, White, center: true);
            Text(dc, GameEngine.ZoneName(g.Zone), new Point(lx, 592), 21, Cyan, center: true);

            Text(dc, "SPEED", new Point(rx, 50), 28, Dim, center: true);
            double top = g.Player.Boat ? GameEngine.BoatSpeed : GameEngine.MaxSpeed;
            Text(dc, $"{g.Player.Speed / 4:0} MPH", new Point(rx, 86), 36, White, center: true);
            var bar = new Rect(30 + Field.Left + Field.Width, 112, Field.Left - 60, 14);
            dc.DrawRectangle(Pen(Dim, 2), bar);
            dc.FillRectangle(Brush(g.Player.Speed > top * 0.8 ? Orange : Green), new Rect(bar.X + 2, bar.Y + 2, (bar.Width - 4) * g.Player.Speed / top, bar.Height - 4));
            Text(dc, g.Player.Boat ? "BOAT" : "CAR", new Point(rx, 146), 24, Cyan, center: true);
            Text(dc, "WEAPON", new Point(rx, 200), 28, Dim, center: true);
            var sp = g.Player.Special;
            Text(dc, sp switch { Special.Oil => "OIL SLICK", Special.Smoke => "SMOKE SCREEN", Special.Missiles => "MISSILES", _ => "GUNS ONLY" }, new Point(rx, 234), 28, sp == Special.None ? Dim : Yellow, center: true);
            for (int i = 0; i < Math.Min(g.Player.Ammo, 8); i++)
            {
                var c = new Point(rx - 75 + (i % 4) * 50, 274 + i / 4 * 36);
                uint col = sp switch { Special.Oil => 0xFF303038, Special.Smoke => 0xFFB0B0B8, _ => Orange };
                dc.DrawEllipse(Brush(col), Pen(White, 1.5), c, 13, 13);
            }
            if (g.Vehicles.Any(v => v.Kind == VehicleKind.Van && !v.Leaving) && (int)(_clock * 3) % 2 == 0)
                Text(dc, "ARMOURY VAN!", new Point(rx, 370), 28, Green, center: true);
            if (g.Copter is not null) Text(dc, "HELICOPTER!", new Point(rx, 400), 28, Red, center: true);
        }
        string[] help = ["ARROWS / WASD: DRIVE", "UP FASTER  DOWN BRAKE", "Z / SPACE: GUNS", "X / SHIFT: WEAPON", "P PAUSE   M MUTE", "CTRL+S SCREENSHOT"];
        for (int i = 0; i < help.Length; i++) Text(dc, help[i], new Point(rx, 590 + i * 26), 21, Dim, center: true);
    }
}
