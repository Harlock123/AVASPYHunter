# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls,
scoring and what the tests check; this file covers how the code is put together and the rules that keep it
working.

## What this is

A road-combat chase in the style of Bally Midway's 1983 arcade game Spy Hunter, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASPYHunter.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASPYHunter.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVASPYHunter          # play it; --no-audio for silence
SPY_SHOTS=$PWD/Images dotnet test tests/AVASPYHunter.Tests --filter ScreenshotTests   # refresh the README's pictures
SPY_PROBE=1 dotnet test tests/AVASPYHunter.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                   # rebuild src/AVASPYHunter/Sounds
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASPYHunter/Engine/`: the game, with no Avalonia drawing in it.
  - `Geometry.cs` has `Vec` and the `Field`: the tall 576x768 playfield in the middle of the screen, the
    1/60 s `Step`, and `ScreenY`, which maps distance down the road to the screen.
  - `Track.cs` lays the road (or river) down from a seed as the player drives: zones, forks round a median,
    bridges and boathouses.
  - `Entities.cs` has `Controls` and the vehicles, shots, hazards, the helicopter, specials and effects.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` has `PlayerCar` and plays the road: traffic, weapons, crashes and scoring.
  - `AttractPilot.cs` is the computer driver, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASPYHunter/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVASPYHunter/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVASPYHunter/Sounds/`: the generated WAV files.
- `tests/AVASPYHunter.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and the opt-in `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the game in fixed
  `Field.Step` (1/60 s) steps, so tests can drive deterministically from a seed. Draw random numbers from the
  engine's seeded `_rng` (or the `Track`'s own, both `new Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** `Dy` is speed: up on keys, D-pad and stick gives -1 (faster), down
  gives +1 (brake). When both of a pair are held, the key pressed last wins. Test new controls through
  `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. Shift fires the special weapon here, and `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASPYHunter`); tests load
  the table from a temporary folder and run with `Silent = true`. `Calm = true` keeps traffic away unless a test
  places it.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, scoring, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
