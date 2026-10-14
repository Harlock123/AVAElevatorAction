namespace AVAElevatorAction.Engine;

/// <summary>
/// The computer spy: works out which red door to go for next (the nearest by floors), and the lifts
/// that get it there; on its way it shoots agents on its floor, ducks high shots and jumps low ones,
/// and with all the papers it heads down to the car. Used for the demonstration and the tests.
/// </summary>
public sealed class SpyPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _pulse;

    public Controls Decide(GameEngine g)
    {
        if (g.Phase != Phase.Playing) return default;
        var p = g.Spy;
        var b = g.Building;
        _pulse = !_pulse;
        if (p.InDoor > 0) return default;

        // A shot coming: duck the high ones, jump the low.
        foreach (var shot in g.Bullets.Where(x => x.Enemy && x.Floor == p.Floor))
        {
            double ahead = (p.X - shot.X) * Math.Sign(shot.Vx);
            if (ahead < 0 || ahead > 120) continue;
            if (p.Riding >= 0) break;
            return shot.Height > 40 ? new Controls(0, 1) : new Controls(0, 0, Jump: _pulse);
        }

        // Where to: the nearest red door, or the car.
        var door = b.Doors.Where(d => d.Red && !d.Done).OrderBy(d => Math.Abs(d.Floor - p.Floor) + (d.Floor > p.Floor ? 0.5 : 0)).FirstOrDefault();
        int goalFloor = door?.Floor ?? 0;
        double goalX = door?.X ?? Building.CarX;

        if (p.Riding >= 0)
        {
            var s = b.Shafts[p.Riding];
            int want = Math.Clamp(goalFloor, s.Lo, s.Hi);
            int at = (int)Math.Round(s.Car);
            if (s.Moving) return default;
            if (at != want) return new Controls(0, want > at ? -1 : 1);
            // Off at the best floor this lift reaches, towards where it's going next.
            double next = goalFloor == at ? goalX : NextShaft(g, at, goalFloor, s.X)?.X ?? goalX;
            return new Controls(next >= s.X ? 1 : -1, 0);
        }

        // An agent on this floor, out and facing: turn and shoot.
        var agent = g.Agents.Where(a => a.Alive && a.State != AgentState.Emerging && a.Floor == p.Floor && Math.Abs(a.X - p.X) < 480)
                            .OrderBy(a => Math.Abs(a.X - p.X)).FirstOrDefault();
        if (agent is not null)
        {
            int face = agent.X > p.X ? 1 : -1;
            if (p.Facing != face) return new Controls(face, 0);
            bool low = agent.Crouched;
            return new Controls(0, low ? 1 : 0, Fire: _pulse);
        }

        if (p.Floor == goalFloor)
        {
            double dx = goalX - p.X;
            if (Math.Abs(dx) > 6) return new Controls(Math.Sign(dx), 0);
            return door is not null ? new Controls(0, _pulse ? -1 : 0) : new Controls(1, 0);
        }

        // To a lift that brings the goal nearer, and in when its car is here.
        var shaft = NextShaft(g, p.Floor, goalFloor, p.X);
        if (shaft is null) return default;
        if (Math.Abs(shaft.X - p.X) > 4) return new Controls(Math.Sign(shaft.X - p.X), 0);
        if (shaft.AtFloor(p.Floor) && !shaft.Moving) return new Controls(0, _pulse ? (goalFloor > p.Floor ? -1 : 1) : 0);
        return default;
    }

    /// <summary>The lift on this floor that gets nearest the goal floor (nearest to hand if several do as well).</summary>
    private static Shaft? NextShaft(GameEngine g, int floor, int goal, double x)
    {
        Shaft? best = null;
        double bestScore = double.MaxValue;
        foreach (var s in g.Building.Shafts.Where(s => s.Serves(floor)))
        {
            int reach = Math.Clamp(goal, s.Lo, s.Hi);
            if (reach == floor) continue;
            double score = Math.Abs(reach - goal) * 1000 + Math.Abs(s.X - x);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }
}
