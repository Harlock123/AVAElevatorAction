# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A spy caper in the style of Taito's 1983 arcade game Elevator Action, in C# on .NET 10 and Avalonia 12. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code,
and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAElevatorAction.slnx               # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAElevatorAction.slnx                # every test runs headless: no window opens
dotnet run --project src/AVAElevatorAction        # play it; --no-audio for silence
ELEVATOR_SHOTS=$PWD/Images dotnet test tests/AVAElevatorAction.Tests --filter ScreenshotTests   # refresh the README's pictures
SPY_PROBE=1 dotnet test tests/AVAElevatorAction.Tests --filter SpyProbe   # report how the computer spy does, for tuning
python3 tools/make_sounds.py                      # rebuild src/AVAElevatorAction/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAElevatorAction/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` steps a mission: the spy, the lifts, the doors, the agents and their bullets.
  - `Building.cs` builds each level's tower: floors, lift shafts, red and blue doors, the garage.
  - `Entities.cs` has `Controls`, `Field` (the screen, floor height and step length) and the people.
  - `SpyPilot.cs` is the computer spy, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAElevatorAction/Views/`: `GameCanvas` draws the frame (`GameCanvas.Draw.cs`) and runs the timer.
- `src/AVAElevatorAction/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAElevatorAction/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVAElevatorAction.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots, the Shift
  release case and the `SpyProbe` tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can play whole levels deterministically from a seed. Draw random numbers from the
  engine's seeded `_rng`, never from `Random.Shared`. `Building.Make` takes its `Random` from the caller.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls.Dy` is negative for up (through a door or up in a lift) and
  positive for down (duck or ride down). Keys, D-pad and stick must all keep that sign. Test new controls
  through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAElevatorAction`);
  tests load the table from a fresh temp folder and set `Silent = true` on the engine. `Calm = true` keeps
  agents from spawning unless a test places them.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
