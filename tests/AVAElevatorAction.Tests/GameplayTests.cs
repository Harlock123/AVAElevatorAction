using AVAElevatorAction.Engine;
using Engine = AVAElevatorAction.Engine;

namespace AVAElevatorAction.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaelevatoraction-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game under way, the arrival skipped, and no agents unless a test puts them there.</summary>
    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.Go();
        g.Idle(1);
        return g;
    }

    /// <summary>A little building of the test's own: five floors, one lift from the garage to the top.</summary>
    private static GameEngine Small(int papersOnFloor = 3, double paperX = 300)
    {
        var g = Game();
        var b = new Building { Floors = 5 };
        b.Shafts.Add(new Shaft { Id = 0, X = 600, Lo = 0, Hi = 4, Car = 4, Target = 4 });
        b.Doors.Add(new Door { Floor = papersOnFloor, X = paperX, Red = true });
        b.Doors.Add(new Door { Floor = 2, X = 800 });
        b.Lamps.Add(new Lamp { Floor = 2, X = 400 });
        g.UseBuilding(b);
        g.Place(4, 500);
        return g;
    }

    private static Engine.Controls Press(int dx = 0, int dy = 0, bool fire = false, bool jump = false) => new(dx, dy, fire, jump);

    private static void WalkTo(GameEngine g, double x)
    {
        for (int i = 0; i < 600 && Math.Abs(g.Spy.X - x) > 3; i++) g.Run(1, Press(dx: Math.Sign(x - g.Spy.X)));
    }

    // ───────────────────────────── the building ─────────────────────────────

    [Fact]
    public void Every_building_can_be_got_down_by_lift_and_has_its_papers()
    {
        for (int level = 1; level <= 8; level++)
        for (int seed = 0; seed < 5; seed++)
        {
            var b = Building.Make(level, new Random(seed));
            // Every floor from the top can reach the garage, changing lifts where they overlap.
            var reach = new HashSet<int> { b.Floors - 1 };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var s in b.Shafts.Where(s => reach.Any(s.Serves)))
                    for (int f = s.Lo; f <= s.Hi; f++) grew |= reach.Add(f);
            }
            Assert.Contains(0, reach);
            Assert.True(b.PapersLeft >= 4);
            Assert.All(b.Doors.Where(d => d.Red), d => Assert.InRange(d.Floor, 2, b.Floors - 2));
        }
    }

    // ───────────────────────────── the spy ─────────────────────────────

    [Fact]
    public void Walking_ducking_and_jumping()
    {
        var g = Small();
        double x = g.Spy.X;
        g.Run(30, Press(dx: -1));
        Assert.True(g.Spy.X < x - 60);
        Assert.Equal(-1, g.Spy.Facing);
        g.Run(10, Press(dy: 1));
        Assert.True(g.Spy.Ducking);
        double ducked = g.Spy.X;
        g.Run(10, Press(dx: 1, dy: 1));
        Assert.Equal(ducked, g.Spy.X);
        g.Run(1, default);
        g.Run(1, Press(jump: true));
        g.Idle(15);
        Assert.True(g.Spy.JumpH > 30);
        g.Idle(60);
        Assert.Equal(0, g.Spy.JumpH);
    }

    [Fact]
    public void Into_the_lift_down_a_floor_at_a_time_and_out()
    {
        var g = Small();
        WalkTo(g, 600);
        g.Run(1, Press(dy: 1));
        Assert.Equal(0, g.Spy.Riding);
        g.Run(60 * 4, Press(dy: 1));
        Assert.Equal(0, g.Spy.Floor);
        g.Run(10, Press(dx: -1));
        Assert.Equal(-1, g.Spy.Riding);
        Assert.True(g.Spy.X < 560);
    }

    [Fact]
    public void An_empty_lift_comes_to_a_spy_waiting_at_its_door()
    {
        var g = Small();
        g.Place(1, 600);
        g.Idle(60 * 6);
        Assert.True(g.Building.Shafts[0].AtFloor(1));
    }

    [Fact]
    public void A_red_door_gives_up_its_papers()
    {
        var g = Small(papersOnFloor: 4, paperX: 300);
        WalkTo(g, 300);
        g.Run(1, Press(dy: -1));
        Assert.True(g.Spy.InDoor > 0);
        g.Idle(70);
        Assert.Equal(0, g.Building.PapersLeft);
        Assert.Equal(500, g.Score);
    }

    [Fact]
    public void The_car_waits_for_every_paper()
    {
        var g = Small(papersOnFloor: 3);
        g.Place(0, 700);
        WalkTo(g, Building.CarX - 30);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.Contains("PAPERS", g.Message);
        g.Building.Doors.First(d => d.Red).Done = true;
        g.Idle(2);
        Assert.Equal(Phase.Escape, g.Phase);
        g.Idle(60 * 4);
        Assert.Equal(2, g.Level);
        Assert.Equal(Phase.Arrive, g.Phase);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void A_shot_drops_an_agent_unless_he_ducks_under_it()
    {
        var g = Small();
        g.Place(2, 200);
        // Standing and taking aim, too busy to dodge.
        var a = g.PutAgent(2, 500);
        a.State = AgentState.Aiming;
        a.Timer = 9;
        g.Run(1, Press(dx: 1));
        g.Run(1, Press(fire: true));
        g.Idle(40);
        Assert.False(a.Alive);
        Assert.Equal(100, g.Score);
        var b = g.PutAgent(2, 500);
        b.State = AgentState.Ducking;
        b.Timer = 5;
        g.Run(1, Press(fire: true));
        g.Idle(40);
        Assert.True(b.Alive);
        // A crouching shot gets him.
        g.Run(1, Press(dy: 1, fire: true));
        g.Run(40, Press(dy: 1));
        Assert.False(b.Alive);
    }

    [Fact]
    public void Duck_under_high_shots_and_jump_over_low_ones()
    {
        var g = Small();
        g.Place(2, 200);
        g.Bullets.Add(new Bullet { Floor = 2, X = 400, Vx = -400, Height = Field.HighShot, Enemy = true });
        g.Run(40, Press(dy: 1));
        Assert.Equal(Phase.Playing, g.Phase);
        g.Bullets.Add(new Bullet { Floor = 2, X = 330, Vx = -400, Height = Field.LowShot, Enemy = true });
        g.Run(1, Press(jump: true));
        g.Idle(40);
        Assert.Equal(Phase.Playing, g.Phase);
        g.Idle(60);
        g.Bullets.Add(new Bullet { Floor = 2, X = 400, Vx = -400, Height = Field.HighShot, Enemy = true });
        g.Idle(40);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT!", g.Cause);
    }

    [Fact]
    public void Agents_come_out_of_doors_walk_up_and_shoot()
    {
        var g = Game();
        g.Calm = false;
        g.Idle(60 * 10);
        Assert.NotEmpty(g.Agents);
        var g2 = Small();
        g2.Place(2, 200);
        var a = g2.PutAgent(2, 800);
        a.Cool = 0;
        g2.Idle(60 * 4);
        Assert.True(a.X < 800);
        Assert.True(g2.Phase == Phase.Dying || g2.Bullets.Any(b => b.Enemy));
    }

    [Fact]
    public void A_jump_kick_knocks_an_agent_flat()
    {
        var g = Small();
        g.Place(2, 200);
        var a = g.PutAgent(2, 260);
        a.State = AgentState.Ducking;
        a.Timer = 9;
        g.Run(1, Press(jump: true, dx: 1));
        g.Run(25, Press(dx: 1));
        Assert.False(a.Alive);
        Assert.Equal(150, g.Score);
    }

    [Fact]
    public void A_lamp_shot_down_flattens_whoever_is_under_it_and_puts_the_lights_out()
    {
        var g = Small();
        g.Place(2, 200);
        var a = g.PutAgent(2, 404);
        a.State = AgentState.Ducking;
        a.Timer = 9;
        g.Run(1, Press(dx: 1));
        g.Run(1, Press(jump: true));
        g.Idle(16);
        g.Run(1, Press(fire: true));
        g.Idle(60);
        Assert.False(a.Alive);
        Assert.Equal(300, g.Score);
        Assert.True(g.Dark[2] > 5);
        Assert.True(g.Building.Lamps[0].Down);
    }

    // ───────────────────────────── lives and the machine ─────────────────────────────

    [Fact]
    public void A_new_spy_carries_on_where_the_last_fell_and_the_last_one_ends_it()
    {
        var g = Small();
        g.Place(2, 200);
        g.Bullets.Add(new Bullet { Floor = 2, X = 300, Vx = -400, Height = Field.HighShot, Enemy = true });
        g.Idle(60 * 3);
        Assert.Equal(GameEngine.StartingLives - 1, g.Lives);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.Equal(2, g.Spy.Floor);
        g.Lives = 1;
        g.Bullets.Add(new Bullet { Floor = 2, X = 300, Vx = -400, Height = Field.HighShot, Enemy = true });
        g.Idle(60 * 3);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_alarm_goes_off_after_a_while()
    {
        var g = Game();
        g.Idle((int)(60 * (g.AlarmAt + 1)));
        Assert.True(g.Alarm);
    }

    [Fact]
    public void The_computer_spy_completes_the_first_mission()
    {
        bool done = false;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 240 && g.Level == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
            if (g.Level >= 2) { done = true; break; }
        }
        Assert.True(done);
    }

    [Fact]
    public void The_attract_mode_plays_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        int top = g.Spy.Floor;
        g.Idle(60 * 25);
        Assert.True(g.Spy.Floor < top || g.State != GameState.Attract);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 10) && g.State == GameState.Attract; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void A_good_score_asks_for_initials()
    {
        var g = Small();
        g.Score = 99999;
        g.Lives = 1;
        g.Place(2, 200);
        g.Bullets.Add(new Bullet { Floor = 2, X = 300, Vx = -400, Height = Field.HighShot, Enemy = true });
        g.Idle(60 * 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('S');
        g.Initials.Type('P');
        g.Initials.Type('Y');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("SPY", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score}:{g.Level}:{g.Lives}:{g.Spy.Floor}:{g.Spy.X:0.0}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 9);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 9), HighScoreTable.Load(folder).Entries[0]);
    }
}
