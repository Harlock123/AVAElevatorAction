namespace AVAElevatorAction.Engine;

/// <summary>
/// A building to get down through: floors from the roof to the basement garage, lift shafts that
/// each serve a stretch of floors (overlapping, so the way down means changing lifts), doors on
/// every floor (red ones hiding secret papers), and lamps hanging from the ceilings.
/// </summary>
public sealed class Building
{
    public int Floors;
    public List<Shaft> Shafts = [];
    public List<Door> Doors = [];
    public List<Lamp> Lamps = [];
    /// <summary>Where the getaway car waits in the garage.</summary>
    public const double CarX = 880;
    public static readonly double[] ShaftXs = [190, 410, 630, 850];

    public static Building Make(int level, Random rng)
    {
        var b = new Building { Floors = Math.Min(34, 22 + level * 2) };
        int top = b.Floors - 1;
        // Lifts in a chain down the building, each overlapping the next by a floor or two.
        int hi = top, id = 0, lastColumn = -1;
        while (hi > 0)
        {
            int span = 5 + rng.Next(4);
            int lo = Math.Max(0, hi - span);
            int column;
            do column = rng.Next(ShaftXs.Length); while (column == lastColumn);
            lastColumn = column;
            b.Shafts.Add(new Shaft { Id = id++, X = ShaftXs[column], Lo = lo, Hi = hi, Car = hi, Target = hi });
            hi = lo + 1 + rng.Next(2);
            if (lo == 0) break;
        }
        // A second lift alongside in places, for choice.
        for (int k = 0; k < 2 + level / 2; k++)
        {
            int lo = 1 + rng.Next(Math.Max(1, top - 6)), span = 3 + rng.Next(4);
            double x = ShaftXs[rng.Next(ShaftXs.Length)];
            int h = Math.Min(top, lo + span);
            if (b.Shafts.Any(s => s.X == x && s.Lo <= h && s.Hi >= lo)) continue;
            b.Shafts.Add(new Shaft { Id = id++, X = x, Lo = lo, Hi = h, Car = lo, Target = lo });
        }
        // Doors between the shafts on every floor but the garage; red ones scattered through.
        double[] spots = [80, 300, 520, 740, 950];
        for (int f = 1; f <= top; f++)
            foreach (double x in spots)
            {
                if (b.Shafts.Any(s => s.Serves(f) && Math.Abs(s.X - x) < 70)) continue;
                if (f == top && x < 200) continue;
                if (rng.Next(100) < 55) b.Doors.Add(new Door { Floor = f, X = x });
            }
        int papers = Math.Min(9, 4 + level / 2);
        var candidates = b.Doors.Where(d => d.Floor > 1 && d.Floor < top).OrderBy(_ => rng.Next()).ToList();
        foreach (var d in candidates.GroupBy(d => d.Floor).Select(g => g.First()).Take(papers)) d.Red = true;
        // Two lamps on each floor.
        for (int f = 1; f <= top; f++)
        {
            b.Lamps.Add(new Lamp { Floor = f, X = 250 + rng.Next(80) });
            b.Lamps.Add(new Lamp { Floor = f, X = 690 + rng.Next(80) });
        }
        return b;
    }

    public Shaft? ShaftAt(int floor, double x) => Shafts.FirstOrDefault(s => s.Serves(floor) && Math.Abs(s.X - x) < Shaft.HalfWidth - 8);

    public Door? DoorAt(int floor, double x) => Doors.FirstOrDefault(d => d.Floor == floor && Math.Abs(d.X - x) < Door.HalfWidth);

    public int PapersLeft => Doors.Count(d => d.Red && !d.Done);
}
