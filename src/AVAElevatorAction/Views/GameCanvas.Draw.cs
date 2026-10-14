using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAElevatorAction.Engine;

namespace AVAElevatorAction.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAElevatorAction/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Ink = 0xFF101014;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
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

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private double _camFloor;

    /// <summary>The screen height of a floor's surface, with the camera on the spy.</summary>
    private double FloorY(double floor) => 560 - (floor - _camFloor) * Field.FloorHeight;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Spy;
        double target = p.Riding >= 0 ? g.Building.Shafts[p.Riding].Car : p.Floor;
        if (g.Phase == Phase.Arrive) target = g.Building.Floors - 1;
        _camFloor = target;
        DrawSky(dc);
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top)))
        {
            int lo = (int)Math.Floor(_camFloor) - 3, hi = (int)Math.Ceiling(_camFloor) + 6;
            for (int f = Math.Max(0, lo); f <= Math.Min(g.Building.Floors - 1, hi); f++) DrawFloor(dc, f);
            DrawRoof(dc);
            foreach (var s in g.Building.Shafts) DrawCar(dc, s);
            for (int f = Math.Max(0, lo); f <= Math.Min(g.Building.Floors - 1, hi); f++) DrawPeople(dc, f);
            foreach (var e in g.Effects) DrawEffect(dc, e);
        }
        DrawTower(dc);
        DrawHud(dc);
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
        if (g.State == GameState.GameOver) Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract && g.Phase == Phase.Playing)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, 90), 26, Yellow, center: true);
    }

    // ───────────────────────────── the building ─────────────────────────────

    private void DrawSky(DrawingContext dc)
    {
        for (int i = 0; i < 24; i++)
        {
            double t = i / 23.0;
            uint c = 0xFF000000 | (uint)(8 + 20 * t) << 16 | (uint)(12 + 18 * t) << 8 | (uint)(30 + 40 * t);
            dc.FillRectangle(Brush(c), new Rect(0, Field.Top + i * 30, Field.Width, 31));
        }
        for (int i = 0; i < 50; i++) dc.FillRectangle(Brush(0x90FFFFFF), new Rect(Hash(i, 1, 2) % 1024, Field.Top + Hash(i, 2, 2) % 300, 2, 2));
    }

    private void DrawRoof(DrawingContext dc)
    {
        var g = Engine;
        int top = g.Building.Floors - 1;
        double y = FloorY(top) - Field.FloorHeight;
        if (y < Field.Top - 60) return;
        // The parapet and an aerial, the city beyond.
        dc.FillRectangle(Brush(0xFF3A3A46), new Rect(Field.Left - 8, y - 12, Field.Right - Field.Left + 16, 14));
        dc.DrawLine(Pen(0xFF5A5A66, 3), new Point(820, y - 12), new Point(820, y - 90));
        if ((int)(_clock * 2) % 2 == 0) dc.DrawEllipse(Brush(Red), null, new Point(820, y - 92), 4, 4);
        // The line the spy comes down on.
        if (g.Phase == Phase.Arrive)
        {
            dc.DrawLine(Pen(0xFF8A8A9A, 1.5), new Point(0, y - 160), new Point(160, y - 20));
        }
    }

    private void DrawFloor(DrawingContext dc, int f)
    {
        var g = Engine;
        var b = g.Building;
        double y = FloorY(f), ceil = y - Field.FloorHeight;
        if (y < Field.Top - 10 || ceil > Field.Height + 10) return;
        bool garage = f == 0;
        uint wall = garage ? 0xFF4A4A52 : f % 2 == 0 ? 0xFFC8B89A : 0xFFB8C0A8;
        dc.FillRectangle(Brush(wall), new Rect(Field.Left, ceil, Field.Right - Field.Left, Field.FloorHeight));
        // A dado rail, windows to the night at the ends, the floor slab, the floor number.
        if (!garage)
        {
            dc.FillRectangle(Brush(Shade(wall, 0.85)), new Rect(Field.Left, y - 30, Field.Right - Field.Left, 30));
            dc.FillRectangle(Brush(0xFF14182A), new Rect(Field.Left + 2, ceil + 18, 20, 44));
            dc.FillRectangle(Brush(0xFF14182A), new Rect(Field.Right - 22, ceil + 18, 20, 44));
        }
        else
        {
            for (double x = Field.Left + 60; x < Field.Right; x += 160) dc.FillRectangle(Brush(0xFFE0C040), new Rect(x, y - 6, 60, 3));
        }
        dc.FillRectangle(Brush(0xFF2A2A30), new Rect(Field.Left - 6, y, Field.Right - Field.Left + 12, 10));
        dc.FillRectangle(Brush(0xFF3A3A44), new Rect(Field.Left - 6, ceil - 2, 8, Field.FloorHeight + 12));
        dc.FillRectangle(Brush(0xFF3A3A44), new Rect(Field.Right - 2, ceil - 2, 8, Field.FloorHeight + 12));
        // The floor number on a little plate.
        var plate = new Rect(Field.Right - (garage ? 104 : 70), ceil + 4, garage ? 74 : 40, 20);
        dc.DrawRectangle(Brush(0xFF1A1A22), Pen(0xFF8A8A98, 1), plate, 3, 3);
        Text(dc, garage ? "GARAGE" : f.ToString(), plate.Center, 20, garage ? Yellow : 0xFFF0E8D0, center: true);
        // Lift shafts on this floor: dark openings, a dial over the door.
        foreach (var s in b.Shafts.Where(s => s.Serves(f)))
        {
            var r = new Rect(s.X - Shaft.HalfWidth, ceil + 4, Shaft.HalfWidth * 2, Field.FloorHeight - 4);
            dc.FillRectangle(Brush(0xFF14141C), r);
            dc.DrawRectangle(Pen(0xFF6A6A78, 3), r);
            double dial = Math.Clamp((s.Car - s.Lo) / Math.Max(1, s.Hi - s.Lo), 0, 1);
            var c = new Point(s.X, ceil - 2);
            dc.DrawEllipse(Brush(0xFFE0D0A0), Pen(0xFF5A4A2A, 1), c, 9, 9);
            double a = Math.PI - dial * Math.PI;
            dc.DrawLine(Pen(Ink, 1.5), c, new Point(c.X + Math.Cos(a) * 7, c.Y - Math.Sin(a) * 7));
        }
        // Doors.
        foreach (var d in b.Doors.Where(d => d.Floor == f))
        {
            var r = new Rect(d.X - Door.HalfWidth, y - 76, Door.HalfWidth * 2, 76);
            uint col = d.Red ? (d.Done ? 0xFF6A3A3A : 0xFFD02A2A) : 0xFF2A5AB0;
            bool open = d.Open > 0 || d.Red && g.Spy.InDoor > 0 && g.Spy.Floor == f && Math.Abs(g.Spy.X - d.X) < Door.HalfWidth;
            dc.FillRectangle(Brush(0xFF0C0C10), r);
            if (!open)
            {
                dc.DrawRectangle(Brush(col), Pen(Shade(col, 0.6), 2), r);
                dc.DrawEllipse(Brush(Yellow), null, new Point(r.Right - 8, r.Center.Y), 3, 3);
                if (d.Red && !d.Done && (int)(_clock * 3) % 2 == 0) dc.DrawRectangle(Pen(0xFFFF8A7A, 2), r.Inflate(3));
            }
            else dc.DrawRectangle(Brush(Shade(col, 0.7)), null, new Rect(r.X, r.Y, 8, r.Height));
            dc.DrawRectangle(Pen(0xFF3A2A1A, 3), r.Inflate(2));
        }
        // Lamps hanging from the ceiling.
        foreach (var l in b.Lamps.Where(l => l.Floor == f))
        {
            if (l.Down)
            {
                dc.FillRectangle(Brush(0xFF6A6A70), new Rect(l.X - 12, y - 6, 24, 6));
                continue;
            }
            double h = l.Falling ? Field.LampHeight * (1 - l.Drop * l.Drop) : Field.LampHeight;
            double ly = y - h;
            if (!l.Falling) dc.DrawLine(Pen(0xFF3A3A3A, 1.5), new Point(l.X, ceil), new Point(l.X, ly - 6));
            var shade = new StreamGeometry();
            using (var c = shade.Open())
            {
                c.BeginFigure(new Point(l.X - 5, ly - 8), true);
                c.LineTo(new Point(l.X + 5, ly - 8));
                c.LineTo(new Point(l.X + 13, ly + 2));
                c.LineTo(new Point(l.X - 13, ly + 2));
                c.EndFigure(true);
            }
            dc.DrawGeometry(Brush(0xFF2A6A3A), Pen(Ink, 1), shade);
            if (g.Dark[f] <= 0 && !l.Falling) dc.DrawEllipse(Brush(0x30FFF0B0), null, new Point(l.X, ly + 30), 40, 30);
        }
        // The garage: the getaway car, and the ground beneath.
        if (garage)
        {
            dc.FillRectangle(Brush(0xFF1E1A16), new Rect(0, y + 10, Field.Width, Field.Height));
            DrawCarAway(dc, y);
        }
    }

    private void DrawCarAway(DrawingContext dc, double y)
    {
        var g = Engine;
        double x = Building.CarX + (g.Phase == Phase.Escape ? g.PhaseTime * g.PhaseTime * 120 : 0);
        dc.FillRectangle(Brush(0xFF0C0C10), new Rect(Field.Right - 70, y - 80, 70, 80));
        var body = new Rect(x - 60, y - 32, 120, 24);
        dc.DrawRectangle(Brush(0xFFC02A2A), Pen(Ink, 2), body, 8, 8);
        dc.DrawRectangle(Brush(0xFFA02020), Pen(Ink, 2), new Rect(x - 30, y - 50, 56, 22), 8, 8);
        dc.FillRectangle(Brush(0xFF8AB8E0), new Rect(x - 24, y - 46, 22, 14));
        dc.FillRectangle(Brush(0xFF8AB8E0), new Rect(x + 2, y - 46, 20, 14));
        dc.DrawEllipse(Brush(Ink), null, new Point(x - 36, y - 6), 11, 11);
        dc.DrawEllipse(Brush(Ink), null, new Point(x + 36, y - 6), 11, 11);
        dc.DrawEllipse(Brush(0xFFB0B0B8), null, new Point(x - 36, y - 6), 4, 4);
        dc.DrawEllipse(Brush(0xFFB0B0B8), null, new Point(x + 36, y - 6), 4, 4);
        if (g.Building.PapersLeft > 0) Text(dc, "LOCKED", new Point(x, y - 66), 18, Red, center: true);
    }

    private void DrawCar(DrawingContext dc, Shaft s)
    {
        double y = FloorY(s.Car), top = y - Field.FloorHeight + 6;
        if (y < Field.Top - 20 || top > Field.Height) return;
        // The cable up the shaft above the car.
        double cableTop = FloorY(s.Hi) - Field.FloorHeight + 4;
        dc.DrawLine(Pen(0xFF6A6A70, 2), new Point(s.X, Math.Max(Field.Top, cableTop)), new Point(s.X, top));
        var r = new Rect(s.X - Shaft.HalfWidth + 3, top, Shaft.HalfWidth * 2 - 6, Field.FloorHeight - 8);
        dc.DrawRectangle(Brush(0xFFB0A070), Pen(0xFF5A4A2A, 2), r, 2, 2);
        dc.FillRectangle(Brush(0xFFE8D8A0), new Rect(r.X + 4, r.Y + 6, r.Width - 8, r.Height - 12));
        dc.FillRectangle(Brush(0xFF5A4A2A), new Rect(r.X, r.Y, r.Width, 5));
    }

    // ───────────────────────────── people ─────────────────────────────

    private void DrawPeople(DrawingContext dc, int f)
    {
        var g = Engine;
        double y = FloorY(f);
        if (y < Field.Top - 10 || y - Field.FloorHeight > Field.Height) return;
        foreach (var a in g.Agents.Where(a => a.Floor == f)) DrawAgent(dc, a, y);
        foreach (var bl in g.Bullets.Where(b => b.Floor == f))
        {
            double by = y - bl.Height;
            dc.DrawLine(Pen(bl.Enemy ? 0xFFFF9A4A : 0xFFFFF0A0, 3), new Point(bl.X, by), new Point(bl.X - Math.Sign(bl.Vx) * 10, by));
        }
        var p = g.Spy;
        // In the car and away: no spy to be seen.
        if (g.Phase == Phase.Escape) { }
        else if (p.Floor == f && p.InDoor <= 0 && (p.Riding < 0 || !g.Building.Shafts[p.Riding].Moving)) DrawSpy(dc, p, y);
        if (p.Riding >= 0 && g.Building.Shafts[p.Riding].Moving && f == p.Floor) DrawSpy(dc, p, FloorY(g.Building.Shafts[p.Riding].Car));
        // Lights out: the floor goes dark.
        if (g.Dark[f] > 0)
        {
            uint a = (uint)(Math.Min(1, g.Dark[f]) * 0xC8) << 24;
            dc.FillRectangle(Brush(a), new Rect(Field.Left, y - Field.FloorHeight, Field.Right - Field.Left, Field.FloorHeight));
        }
    }

    private void DrawSpy(DrawingContext dc, Spy p, double floorY)
    {
        if (Engine.Phase == Phase.Arrive)
        {
            // Sliding down the line onto the roof.
            double t = Math.Min(1, Engine.PhaseTime / 1.6);
            DrawFigure(dc, 160 * t - 40 + 0 * p.X, floorY - 140 * (1 - t), 1, 0xFFC8C8D0, 0xFFE8C080, false, false, Engine.PhaseTime * 4, aim: false, hat: false);
            return;
        }
        bool dying = Engine.Phase == Phase.Dying;
        DrawFigure(dc, p.X, floorY - p.JumpH, p.Facing, 0xFFC8C8D0, 0xFFE8C080, p.Ducking, dying, p.Walk, aim: Engine.Phase == Phase.Playing && p.Cool > 0.05, hat: false);
    }

    private void DrawAgent(DrawingContext dc, Agent a, double floorY)
    {
        if (a.State == AgentState.Emerging) return;
        bool dead = a.State == AgentState.Dead;
        if (dead && (int)(a.DeadTime * 10) % 2 == 1 && a.DeadTime > 0.6) return;
        DrawFigure(dc, a.X, floorY - a.JumpH, a.Facing, 0xFF202028, 0xFFF0C8A0, a.Crouched, dead, a.Walk, aim: a.State == AgentState.Aiming, hat: true);
    }

    /// <summary>A man in a suit: standing, walking, crouching, aiming, or down.</summary>
    private static void DrawFigure(DrawingContext dc, double x, double feet, int f, uint suit, uint skin, bool crouch, bool down, double walk, bool aim, bool hat)
    {
        if (down)
        {
            dc.DrawRectangle(Brush(suit), Pen(Ink, 1.5), new Rect(x - 30, feet - 14, 52, 14), 5, 5);
            dc.DrawEllipse(Brush(skin), Pen(Ink, 1), new Point(x + 28 * -f + (f > 0 ? 0 : 0), feet - 8), 8, 7);
            return;
        }
        double h = crouch ? 44 : 84;
        double hip = feet - (crouch ? 16 : 38), shoulder = feet - h + 18;
        double s = crouch ? 0 : Math.Sin(walk) * 8;
        // Legs.
        if (crouch)
        {
            dc.DrawLine(Pen(Shade(suit, 0.8), 7), new Point(x, hip), new Point(x + f * 12, feet - 8));
            dc.DrawLine(Pen(Shade(suit, 0.8), 7), new Point(x + f * 12, feet - 8), new Point(x + f * 4, feet));
            dc.DrawLine(Pen(Shade(suit, 0.8), 7), new Point(x, hip), new Point(x - f * 8, feet));
        }
        else
        {
            dc.DrawLine(Pen(Shade(suit, 0.8), 7), new Point(x, hip), new Point(x + s, feet));
            dc.DrawLine(Pen(Shade(suit, 0.8), 7), new Point(x, hip), new Point(x - s, feet));
        }
        dc.FillRectangle(Brush(Ink), new Rect(x + s - 6, feet - 3, 12, 4));
        dc.FillRectangle(Brush(Ink), new Rect(x - s - 6, feet - 3, 12, 4));
        // Body, tie, head.
        dc.DrawRectangle(Brush(suit), Pen(Ink, 1.5), new Rect(x - 11, shoulder, 22, hip - shoulder + 4), 5, 5);
        dc.DrawLine(Pen(hat ? 0xFF101010 : 0xFFC02A2A, 3), new Point(x, shoulder + 2), new Point(x, shoulder + 14));
        var head = new Point(x + f * 2, shoulder - 10);
        dc.DrawEllipse(Brush(skin), Pen(Ink, 1.2), head, 9, 10);
        if (hat)
        {
            dc.FillRectangle(Brush(0xFF101014), new Rect(head.X - 13, head.Y - 8, 26, 4));
            dc.DrawRectangle(Brush(0xFF101014), null, new Rect(head.X - 8, head.Y - 17, 16, 10), 3, 3);
            dc.FillRectangle(Brush(0xFF101014), new Rect(head.X - 2 + f * 2, head.Y - 3, 9, 3));
        }
        else
        {
            dc.DrawEllipse(Brush(0xFFE0B040), null, new Point(head.X - f * 2, head.Y - 7), 9, 5);
            dc.DrawEllipse(Brush(Ink), null, new Point(head.X + f * 4, head.Y - 1), 1.5, 1.5);
        }
        // The gun arm: out straight when firing, otherwise at the side.
        var sh = new Point(x + f * 4, shoulder + 6);
        if (aim)
        {
            var hand = new Point(x + f * 26, shoulder + 8);
            dc.DrawLine(Pen(suit, 6), sh, hand);
            dc.FillRectangle(Brush(0xFF3A3A3A), new Rect(Math.Min(hand.X, hand.X + f * 10), hand.Y - 3, 10, 5));
        }
        else dc.DrawLine(Pen(suit, 6), sh, new Point(x + f * 8 - s * 0.4, hip));
    }

    // ───────────────────────────── effects and words ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        double t = e.Age / e.Life;
        double y = FloorY(e.Floor) - e.Height;
        switch (e.Kind)
        {
            case EffectKind.Score:
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, y - t * 30), 24, e.Colour, center: true);
                break;
            case EffectKind.Smoke:
                for (int i = 0; i < 6; i++)
                    dc.DrawEllipse(Brush((uint)((1 - t) * 160) << 24 | 0x00B0B0B0), null, new Point(e.X + (i - 2.5) * 8, y - t * 30 - (i % 2) * 6), 8 + t * 10, 6 + t * 6);
                break;
        }
    }

    /// <summary>A strip at the right edge: the whole building, the floors with papers still to get, and the spy.</summary>
    private void DrawTower(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Building;
        double top = Field.Top + 10, bottom = Field.Height - 10, x = Field.Right + 10;
        double h = (bottom - top) / b.Floors;
        dc.FillRectangle(Brush(0xC0000000), new Rect(x - 4, top - 4, 20, bottom - top + 8));
        for (int f = 0; f < b.Floors; f++)
        {
            double y = bottom - (f + 1) * h;
            uint c = b.Doors.Any(d => d.Floor == f && d.Red && !d.Done) ? Red : b.Doors.Any(d => d.Floor == f && d.Red) ? 0xFF4A6A4A : 0xFF3A3A48;
            dc.FillRectangle(Brush(c), new Rect(x, y + 1, 12, Math.Max(1, h - 2)));
        }
        double spyY = bottom - (g.Spy.Floor + 0.5) * h;
        dc.DrawEllipse(Brush(White), null, new Point(x + 6, spyY), 4, 4);
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C0C12), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF6A6A78), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(14, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(14, 42), 36, White);
        Text(dc, "HI", new Point(160, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(160, 42), 28, Dim);
        Text(dc, $"LEVEL {g.Level}", new Point(330, 16), 22, Yellow);
        Text(dc, g.Spy.Floor == 0 ? "GARAGE" : $"FLOOR {g.Spy.Floor}", new Point(330, 42), 28, White);
        int total = g.Building.Doors.Count(d => d.Red), got = total - g.Building.PapersLeft;
        Text(dc, "PAPERS", new Point(520, 16), 22, Dim);
        for (int i = 0; i < total; i++)
            dc.DrawRectangle(Brush(i < got ? 0xFFF0E8D0 : 0xFF3A3A44), Pen(i < got ? Red : 0xFF5A5A66, 1.5), new Rect(522 + i * 20, 34, 14, 18), 2, 2);
        Text(dc, "SPIES", new Point(Field.Width - 180, 16), 22, Dim);
        for (int i = 0; i < Math.Min(6, g.Lives); i++)
        {
            double x = Field.Width - 172 + i * 22;
            dc.DrawEllipse(Brush(0xFFE8C080), null, new Point(x, 38), 5, 5);
            dc.FillRectangle(Brush(0xFFC8C8D0), new Rect(x - 5, 44, 10, 10));
        }
        if (g.Alarm && g.Phase == Phase.Playing && (int)(_clock * 3) % 2 == 0)
            Text(dc, "ALARM", new Point(Field.Width / 2 + 210, 42), 30, Red, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Phase == Phase.Arrive && g.State is GameState.Playing or GameState.Attract or GameState.Paused)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(200, 250, 624, 150));
            Text(dc, $"MISSION {g.Level}", new Point(cx, 290), 60, Yellow, center: true);
            Text(dc, $"{g.Building.PapersLeft} SECRET PAPERS - {g.Building.Floors - 1} FLOORS TO THE CAR", new Point(cx, 350), 28, White, center: true);
            return;
        }
        if (g.Message is { } msg) Text(dc, msg, new Point(cx, 130), msg.Length > 24 ? 40 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA ELEVATOR ACTION", new Point(cx, 160), 92, Red, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 228), 28, Dim, center: true);
        string[] lines =
        [
            "GET DOWN THE BUILDING FROM THE ROOF TO THE GARAGE",
            "COLLECT THE SECRET PAPERS BEHIND EVERY RED DOOR",
            "RIDE THE LIFTS - UP OR DOWN TO GET IN, LEFT OR RIGHT TO GET OUT",
            "DUCK HIGH SHOTS, JUMP LOW ONES, SHOOT DOWN THE LAMPS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 320 + i * 40), 30, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 560), 48, Yellow, center: true);
        Text(dc, "ARROWS: MOVE    Z / SPACE: FIRE    X / SHIFT: JUMP", new Point(cx, 640), 28, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "BEST AGENTS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
