namespace AVAElevatorAction.Engine;

public sealed partial class GameEngine
{
    public const double PaperSeconds = 1.0;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 30000;

    public Building Building { get; private set; } = new();
    public Spy Spy { get; private set; } = new();
    public List<Agent> Agents { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public double[] Dark { get; private set; } = [];
    public int Level { get; private set; } = 1;
    public int Score { get; internal set; }
    public int Lives { get; internal set; }
    public bool Alarm { get; private set; }
    public double LevelTime { get; private set; }
    public string? Cause { get; private set; }
    /// <summary>The computer spy, for the demonstration (and the tests).</summary>
    public SpyPilot Pilot { get; private set; } = new(1);

    private double _spawnTimer;
    private int _nextExtra;
    private Controls _prev;
    private double _lockedTimer;

    /// <summary>Test hook: no agents come out unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    private double Rand(double lo, double hi) => lo + _rng.NextDouble() * (hi - lo);

    public double AlarmAt => Math.Max(35, 75 - Level * 5);
    public int MaxAgents => Math.Min(7, 3 + Level / 2);
    public double BulletSpeed => Math.Min(460, 280 + Level * 25);

    private void NewGame()
    {
        _rng = new Random(_seed + (int)(StateTime * 997));
        Score = 0;
        Lives = StartingLives;
        _nextExtra = FirstExtraLife;
        Pilot = new SpyPilot(_rng.Next());
        Effects.Clear();
        StartLevel(1);
    }

    private void StartLevel(int level)
    {
        Level = level;
        Building = Building.Make(level, _rng);
        Dark = new double[Building.Floors];
        Spy = new Spy { Floor = Building.Floors - 1, X = 110, Facing = 1 };
        Agents.Clear();
        Bullets.Clear();
        Alarm = false;
        LevelTime = 0;
        _spawnTimer = 3;
        SetPhase(Phase.Arrive);
    }

    internal void AddScore(int points, double x, int floor, double height = 90)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Floor = floor, Height = height, Text = points.ToString(), Life = 0.9, Colour = 0xFFFFE14A });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
            Say("EXTRA SPY!", 2);
        }
    }

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        foreach (var s in Building.Shafts) MoveCar(s, dt);
        for (int f = 0; f < Dark.Length; f++) if (Dark[f] > 0) Dark[f] -= dt;
        switch (Phase)
        {
            case Phase.Arrive:
                if (PhaseTime > 2.2)
                {
                    SetPhase(Phase.Playing);
                    Music(true);
                }
                _prev = c;
                return;
            case Phase.Dying:
                MoveAgents(dt);
                if (PhaseTime > 2.4) AfterDeath();
                _prev = c;
                return;
            case Phase.Escape:
                if (PhaseTime > 3.5) StartLevel(Level + 1);
                _prev = c;
                return;
        }
        LevelTime += dt;
        if (!Alarm && LevelTime > AlarmAt)
        {
            Alarm = true;
            Play(Sfx.Alarm);
            Say("ALARM! THEY KNOW YOU'RE HERE", 2.5);
        }
        MoveSpy(c, dt);
        if (Phase != Phase.Playing) { _prev = c; return; }
        if (!Calm) Spawn(dt);
        MoveAgents(dt);
        MoveBullets(dt);
        MoveLamps(dt);
        Agents.RemoveAll(a => a.State == AgentState.Dead && a.DeadTime > 1.2 || Math.Abs(a.Floor - Spy.Floor) > 4);
        Bullets.RemoveAll(b => b.Gone);
        _prev = c;
    }

    // ───────────────────────────── the spy ─────────────────────────────

    private void MoveSpy(Controls c, double dt)
    {
        var p = Spy;
        if (p.Cool > 0) p.Cool -= dt;
        if (_lockedTimer > 0) _lockedTimer -= dt;
        bool up = c.Dy < 0 && _prev.Dy >= 0, down = c.Dy > 0 && _prev.Dy <= 0;
        if (p.InDoor > 0)
        {
            p.InDoor -= dt;
            if (p.InDoor <= 0 && Building.DoorAt(p.Floor, p.X) is { Red: true, Done: false } door)
            {
                door.Done = true;
                AddScore(500, door.X, door.Floor, 110);
                Play(Sfx.Papers);
                int left = Building.PapersLeft;
                Say(left > 0 ? $"SECRET PAPERS! {left} TO GO" : "ALL THE PAPERS! NOW FOR THE CAR", 2);
            }
            return;
        }
        if (p.Riding >= 0)
        {
            Ride(p, c, dt);
            Shoot(p, c);
            return;
        }
        // Jumping.
        if (p.Airborne)
        {
            p.JumpH += p.Vy * dt;
            p.Vy -= Spy.Gravity * dt;
            if (p.JumpH <= 0) { p.JumpH = 0; p.Vy = 0; }
        }
        else if (c.Jump && !_prev.Jump && c.Dy <= 0)
        {
            p.Vy = Spy.JumpSpeed;
            p.JumpH = 0.1;
            Play(Sfx.Jump);
        }
        p.Ducking = c.Dy > 0 && !p.Airborne;
        // Up at a red door goes in for the papers; up or down at a lift with its car here gets in.
        var shaft = Building.ShaftAt(p.Floor, p.X);
        if ((up || down) && !p.Airborne && shaft is not null && shaft.AtFloor(p.Floor) && !shaft.Moving)
        {
            p.Riding = shaft.Id;
            p.X = shaft.X;
            p.Ducking = false;
            Ride(p, c, dt);
            return;
        }
        if (up && !p.Airborne && Building.DoorAt(p.Floor, p.X) is { Red: true, Done: false })
        {
            p.InDoor = PaperSeconds;
            Play(Sfx.DoorOpen);
            return;
        }
        if (c.Dx != 0 && !p.Ducking)
        {
            p.X = Math.Clamp(p.X + c.Dx * Spy.WalkSpeed * dt, Field.Left + 16, Field.Right - 16);
            p.Facing = c.Dx;
            p.Walk += dt * 10;
        }
        else if (c.Dx != 0) p.Facing = c.Dx;
        Shoot(p, c);
        Kicks(p);
        // The garage: the car's waiting, if the papers are all in hand.
        if (p.Floor == 0 && p.X >= Building.CarX - 40)
        {
            if (Building.PapersLeft == 0) Escape();
            else if (_lockedTimer <= 0)
            {
                _lockedTimer = 3;
                Play(Sfx.Locked);
                Say($"{Building.PapersLeft} SECRET PAPERS STILL UPSTAIRS", 2.5);
            }
        }
    }

    /// <summary>In a lift: up and down move it a floor at a time; left or right steps out when it's stopped.</summary>
    private void Ride(Spy p, Controls c, double dt)
    {
        var s = Building.Shafts[p.Riding];
        p.X = s.X;
        if (!s.Moving)
        {
            int at = (int)Math.Round(s.Car);
            p.Floor = at;
            if (c.Dy < 0 && at < s.Hi) { s.Target = at + 1; Play(Sfx.Hum); }
            else if (c.Dy > 0 && at > s.Lo) { s.Target = at - 1; Play(Sfx.Hum); }
            else if (c.Dx != 0)
            {
                p.Riding = -1;
                p.X = s.X + c.Dx * (Shaft.HalfWidth + 2);
                p.Facing = c.Dx;
            }
        }
        else p.Floor = (int)Math.Round(s.Car);
    }

    private void Shoot(Spy p, Controls c)
    {
        if (!c.Fire || _prev.Fire || p.Cool > 0 || Bullets.Count(b => !b.Enemy) >= 3) return;
        if (p.Riding >= 0 && Building.Shafts[p.Riding].Moving) return;
        p.Cool = 0.18;
        double height = p.Ducking ? Field.LowShot : Field.HighShot + p.JumpH;
        Bullets.Add(new Bullet { Floor = p.Floor, X = p.X + p.Facing * 20, Vx = p.Facing * 560, Height = height });
        Play(Sfx.Shot);
    }

    /// <summary>A jump into an agent knocks him flat.</summary>
    private void Kicks(Spy p)
    {
        if (p.JumpH < 12) return;
        foreach (var a in Agents.Where(a => a.Alive && a.State != AgentState.Emerging && a.Floor == p.Floor && Math.Abs(a.X - p.X) < 26))
        {
            Kill(a, 150);
            Play(Sfx.Kick);
        }
    }

    private void Escape()
    {
        Music(false);
        int bonus = 1000 * Level;
        AddScore(bonus, Building.CarX, 0, 120);
        Play(Sfx.Engine);
        Say("MISSION COMPLETE!", 3);
        SetPhase(Phase.Escape);
    }

    // ───────────────────────────── lifts ─────────────────────────────

    private void MoveCar(Shaft s, double dt)
    {
        var p = Spy;
        bool ridden = p.Riding == s.Id;
        if (!s.Moving)
        {
            if (ridden) return;
            s.Wait -= dt;
            if (s.Wait > 0) return;
            s.Wait = Rand(1.2, 2.4);
            // Comes for the spy waiting at its door; otherwise wanders its floors.
            if (p.Riding < 0 && s.Serves(p.Floor) && Math.Abs(p.X - s.X) < 140) s.Target = p.Floor;
            else s.Target = _rng.Next(s.Lo, s.Hi + 1);
            return;
        }
        double step = Shaft.Speed * dt;
        if (Math.Abs(s.Target - s.Car) <= step)
        {
            s.Car = s.Target;
            if (ridden || p.Floor == s.Target && Math.Abs(p.X - s.X) < 200) Play(Sfx.Ding);
            if (!ridden) s.Wait = Rand(1.0, 2.0);
        }
        else s.Car += Math.Sign(s.Target - s.Car) * step;
    }

    // ───────────────────────────── agents ─────────────────────────────

    private void Spawn(double dt)
    {
        _spawnTimer -= dt;
        if (_spawnTimer > 0 || Agents.Count(a => a.Alive) >= MaxAgents) return;
        _spawnTimer = Math.Max(0.8, 2.8 - Level * 0.2) * (Alarm ? 0.5 : 1) * Rand(0.7, 1.3);
        var p = Spy;
        var doors = Building.Doors.Where(d => !d.Red && Math.Abs(d.Floor - p.Floor) <= 1 && d.Floor > 0 && (d.Floor != p.Floor || Math.Abs(d.X - p.X) > 140)).ToList();
        if (doors.Count == 0) return;
        // Most come out on the spy's own floor.
        var same = doors.Where(d => d.Floor == p.Floor).ToList();
        var door = same.Count > 0 && _rng.Next(3) > 0 ? same[_rng.Next(same.Count)] : doors[_rng.Next(doors.Count)];
        door.Open = 1;
        Agents.Add(new Agent { Floor = door.Floor, X = door.X, State = AgentState.Emerging, Timer = 0.6, Facing = p.X > door.X ? 1 : -1, Cool = Rand(0.8, 1.6) });
        Play(Sfx.DoorOpen);
    }

    private void MoveAgents(double dt)
    {
        var p = Spy;
        foreach (var d in Building.Doors) if (d.Open > 0) d.Open -= dt;
        foreach (var a in Agents)
        {
            if (a.State == AgentState.Dead) { a.DeadTime += dt; continue; }
            if (a.Cool > 0) a.Cool -= dt;
            bool sameFloor = a.Floor == p.Floor && p.InDoor <= 0 && Phase == Phase.Playing;
            switch (a.State)
            {
                case AgentState.Emerging:
                    a.Timer -= dt;
                    if (a.Timer <= 0) a.State = AgentState.Walking;
                    break;
                case AgentState.Walking:
                {
                    if (sameFloor)
                    {
                        a.Facing = p.X > a.X ? 1 : -1;
                        double dist = Math.Abs(p.X - a.X);
                        if (dist > 330) Step(a, a.Facing, dt);
                        else if (dist < 120) Step(a, -a.Facing, dt);
                        if (a.Cool <= 0 && dist < 520)
                        {
                            a.State = AgentState.Aiming;
                            a.Timer = Math.Max(0.35, 0.65 - Level * 0.03);
                            a.LowShot = _rng.Next(100) < 35;
                        }
                    }
                    else
                    {
                        // On another floor: wander.
                        Step(a, a.Facing, dt * 0.5);
                        if (a.X < Field.Left + 30 || a.X > Field.Right - 30) a.Facing = -a.Facing;
                    }
                    Dodge(a);
                    break;
                }
                case AgentState.Aiming:
                    a.Timer -= dt;
                    if (sameFloor) a.Facing = p.X > a.X ? 1 : -1;
                    if (a.Timer <= 0)
                    {
                        Bullets.Add(new Bullet { Floor = a.Floor, X = a.X + a.Facing * 20, Vx = a.Facing * BulletSpeed, Height = a.LowShot ? Field.LowShot : Field.HighShot, Enemy = true });
                        Play(Sfx.EnemyShot);
                        a.State = AgentState.Walking;
                        a.Cool = Rand(1.4, 2.6) / (Alarm ? 1.4 : 1);
                    }
                    break;
                case AgentState.Ducking:
                    a.Timer -= dt;
                    if (a.Timer <= 0) a.State = AgentState.Walking;
                    break;
                case AgentState.Jumping:
                    a.JumpH += a.Vy * dt;
                    a.Vy -= Spy.Gravity * dt;
                    if (a.JumpH <= 0) { a.JumpH = 0; a.Vy = 0; a.State = AgentState.Walking; }
                    break;
            }
        }
    }

    private static void Step(Agent a, int dir, double dt)
    {
        a.X = Math.Clamp(a.X + dir * 85 * dt, Field.Left + 20, Field.Right - 20);
        a.Walk += dt * 9;
    }

    /// <summary>Agents sometimes see a shot coming and duck under it or jump over it.</summary>
    private void Dodge(Agent a)
    {
        foreach (var b in Bullets.Where(b => !b.Enemy && b.Floor == a.Floor))
        {
            double ahead = (a.X - b.X) * Math.Sign(b.Vx);
            if (ahead < 40 || ahead > 120 || _rng.Next(100) > 4) continue;
            if (b.Height > 40) { a.State = AgentState.Ducking; a.Timer = 0.6; }
            else { a.State = AgentState.Jumping; a.Vy = Spy.JumpSpeed; a.JumpH = 0.1; }
            return;
        }
    }

    private void Kill(Agent a, int points)
    {
        a.State = AgentState.Dead;
        a.DeadTime = 0;
        AddScore(points, a.X, a.Floor);
        Play(Sfx.Down);
    }

    // ───────────────────────────── bullets and lamps ─────────────────────────────

    private void MoveBullets(double dt)
    {
        var p = Spy;
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            if (b.X < Field.Left || b.X > Field.Right) { b.Gone = true; continue; }
            if (!b.Enemy)
            {
                foreach (var a in Agents.Where(a => a.Alive && a.State != AgentState.Emerging && a.Floor == b.Floor))
                {
                    double bottom = a.JumpH, top = bottom + (a.Crouched ? 40 : 82);
                    if (Math.Abs(a.X - b.X) > 14 || b.Height < bottom || b.Height > top) continue;
                    b.Gone = true;
                    Kill(a, 100);
                    break;
                }
                if (b.Gone) continue;
                foreach (var l in Building.Lamps.Where(l => l.Floor == b.Floor && !l.Falling && !l.Down))
                {
                    if (Math.Abs(l.X - b.X) > 12 || b.Height < Field.LampHeight - 12) continue;
                    b.Gone = true;
                    l.Falling = true;
                    l.Drop = 0;
                    Play(Sfx.LampFall);
                    break;
                }
            }
            else if (b.Floor == p.Floor && p.InDoor <= 0 && Phase == Phase.Playing)
            {
                if (p.Riding >= 0 && Building.Shafts[p.Riding].Moving) continue;
                double bottom = p.JumpH, top = bottom + (p.Ducking ? 40 : 82);
                if (Math.Abs(p.X - b.X) > 12 || b.Height < bottom || b.Height > top) continue;
                b.Gone = true;
                Die("SHOT!");
                return;
            }
        }
    }

    private void MoveLamps(double dt)
    {
        foreach (var l in Building.Lamps.Where(l => l.Falling))
        {
            l.Drop += dt / 0.35;
            if (l.Drop < 1) continue;
            l.Falling = false;
            l.Down = true;
            Play(Sfx.Crash);
            Dark[l.Floor] = 8;
            Effects.Add(new Effect { Kind = EffectKind.Smoke, X = l.X, Floor = l.Floor, Height = 6, Life = 0.8 });
            foreach (var a in Agents.Where(a => a.Alive && a.Floor == l.Floor && Math.Abs(a.X - l.X) < 34)) Kill(a, 300);
            if (Spy.Floor == l.Floor && Math.Abs(Spy.X - l.X) < 26 && Spy.InDoor <= 0) Die("FLATTENED BY A LAMP!");
        }
    }

    // ───────────────────────────── death ─────────────────────────────

    private void Die(string why)
    {
        if (Phase != Phase.Playing) return;
        Cause = why;
        Music(false);
        Play(Sfx.Hit);
        Say(why, 2.4);
        SetPhase(Phase.Dying);
    }

    /// <summary>The next spy carries on where the last fell, with the floors around him cleared.</summary>
    private void AfterDeath()
    {
        Lives--;
        if (Lives <= 0) { GameOver(); return; }
        var p = Spy;
        if (p.Riding >= 0)
        {
            var s = Building.Shafts[p.Riding];
            s.Car = s.Target = (int)Math.Round(s.Car);
        }
        Spy = new Spy { Floor = p.Floor, X = p.X, Facing = p.Facing, Riding = p.Riding };
        Agents.RemoveAll(a => Math.Abs(a.Floor - p.Floor) <= 1);
        Bullets.Clear();
        _spawnTimer = 3;
        SetPhase(Phase.Playing);
        Music(true);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Go()
    {
        if (Phase != Phase.Arrive) return;
        SetPhase(Phase.Playing);
    }

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal void Place(int floor, double x)
    {
        Spy.Riding = -1;
        Spy.Floor = floor;
        Spy.X = x;
        Spy.JumpH = Spy.Vy = 0;
    }

    internal Agent PutAgent(int floor, double x)
    {
        var a = new Agent { Floor = floor, X = x, State = AgentState.Walking, Facing = Spy.X > x ? 1 : -1, Cool = 99 };
        Agents.Add(a);
        return a;
    }

    internal void UseBuilding(Building b)
    {
        Building = b;
        Dark = new double[b.Floors];
    }
}
