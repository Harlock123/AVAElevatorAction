namespace AVAElevatorAction.Engine;

/// <summary>Left/right walk, down ducks, up goes through a door or rides a lift up; fire and jump.</summary>
public readonly record struct Controls(int Dx, int Dy, bool Fire = false, bool Jump = false);

/// <summary>The building in section: floors one above another, the walls at either side.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const double FloorHeight = 96;
    public const double Left = 32, Right = 992;
    /// <summary>Heights above a floor: a standing man's chest, a crouching man's, the lamps.</summary>
    public const double HighShot = 56, LowShot = 24, LampHeight = 84;
}

/// <summary>A lift shaft: where it is, the floors it serves, and its car.</summary>
public sealed class Shaft
{
    public const double HalfWidth = 34, Speed = 1.6;
    public int Id;
    public double X;
    public int Lo, Hi;
    /// <summary>The car's floor, as a fraction while it moves.</summary>
    public double Car;
    public int Target;
    public double Wait;
    public bool Serves(int floor) => floor >= Lo && floor <= Hi;
    public bool AtFloor(int floor) => Math.Abs(Car - floor) < 0.02;
    public bool Moving => Math.Abs(Car - Target) > 0.001;
}

public sealed class Door
{
    public const double HalfWidth = 22;
    public int Floor;
    public double X;
    /// <summary>A red door hides secret papers.</summary>
    public bool Red;
    public bool Done;
    /// <summary>How far open (for drawing), and whether someone is coming out.</summary>
    public double Open;
}

public sealed class Lamp
{
    public int Floor;
    public double X;
    public bool Falling, Down;
    public double Drop;
}

public enum AgentState { Emerging, Walking, Aiming, Ducking, Jumping, Dead }

public sealed class Agent
{
    public int Floor;
    public double X, Timer, Cool, JumpH, Vy, Walk, DeadTime;
    public int Facing = 1;
    public AgentState State;
    /// <summary>Which shot it's lining up: high (standing) or low (crouched).</summary>
    public bool LowShot;
    public bool Alive => State != AgentState.Dead;
    public bool Crouched => State == AgentState.Ducking || State == AgentState.Aiming && LowShot;
}

public sealed class Bullet
{
    public int Floor;
    public double X, Vx, Height;
    public bool Enemy, Gone;
}

public sealed class Spy
{
    public const double WalkSpeed = 170, JumpSpeed = 330, Gravity = 900;
    public int Floor;
    public double X, JumpH, Vy, Cool, Walk;
    public int Facing = 1;
    public bool Ducking;
    /// <summary>The lift it's riding, or -1.</summary>
    public int Riding = -1;
    /// <summary>Inside a red door, collecting the papers.</summary>
    public double InDoor;
    public bool Airborne => JumpH > 0 || Vy > 0;
}

public enum EffectKind { Score, Word, Flash, Smoke }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Floor, Height, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
