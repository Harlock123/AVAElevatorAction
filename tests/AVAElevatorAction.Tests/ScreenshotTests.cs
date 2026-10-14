using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAElevatorAction.Engine;
using AVAElevatorAction.Views;

namespace AVAElevatorAction.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ELEVATOR_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaelevatoraction-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ELEVATOR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAElevatorAction-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 300)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
            if (g.State != GameState.Playing) g.StartGame();
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("SPY", 61250, 7);
        g.HighScores.Insert("AVA", 32000, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Tick(default);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void A_mission()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        g.Idle(50);
        c.Advance(0.5);
        Shot(w, c, "roof");
        PlayUntil(g, c, g => g.Phase == Phase.Playing && g.Spy.Riding >= 0 && g.Building.Shafts[g.Spy.Riding].Moving && g.LevelTime > 4);
        Shot(w, c, "lift");
        PlayUntil(g, c, g => g.Phase == Phase.Playing && g.Agents.Count(a => a.Alive && a.Floor == g.Spy.Floor && a.State != AgentState.Emerging) >= 2 && g.Bullets.Count >= 2);
        Shot(w, c, "gunfight");
        PlayUntil(g, c, g => g.Phase == Phase.Playing && g.Spy.InDoor > 0.3);
        Shot(w, c, "papers");
        // Shoot out a lamp over an agent: jump, and fire at the top of the jump.
        PlayUntil(g, c, g => g.Phase == Phase.Playing && g.Spy.Riding < 0 && g.Spy.InDoor <= 0 && g.Spy.Floor > 1 && g.Building.Lamps.Any(l => l.Floor == g.Spy.Floor && !l.Down && !l.Falling));
        var lamp = g.Building.Lamps.First(l => l.Floor == g.Spy.Floor && !l.Down && !l.Falling);
        g.Agents.Clear();
        g.Bullets.Clear();
        g.Place(g.Spy.Floor, lamp.X - 200);
        g.PutAgent(g.Spy.Floor, lamp.X + 4);
        g.Run(1, new Engine.Controls(1, 0));
        g.Run(1, new Engine.Controls(0, 0, Jump: true));
        g.Idle(16);
        g.Run(1, new Engine.Controls(0, 0, Fire: true));
        g.Idle(60);
        c.Advance(0.2);
        Shot(w, c, "lights-out");
        PlayUntil(g, c, g => g.Phase == Phase.Escape && g.PhaseTime > 0.8);
        Shot(w, c, "getaway");
        PlayUntil(g, c, g => g.Phase == Phase.Dying && g.PhaseTime > 0.4);
        Shot(w, c, "shot");
    }
}
