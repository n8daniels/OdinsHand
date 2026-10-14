# Odin's Hand

**An in-game test harness for Valheim (BepInEx) mods.** One command launches the real game, loads a disposable test
character and world by itself, runs your test cases against live game objects, takes screenshots, quits, and prints a
pass/fail report. No clicking through menus, no "I think it works".

```
> .\odins.ps1 run
Launching Valheim with the MyMod dev profile...
[harness] PASS  mymod:world-is-up   a local player exists
[harness] PASS  mymod:world-is-up   vanilla content is loaded
[harness] PASS  engine              no engine errors during the run
[harness] 3 passed, 0 failed, 3 checks total.
```

It was extracted from the harness that verified several of the author's Valheim mods (hundreds of in-game checks per mod,
single-player and against a dedicated server).

## Where it fits

| | Odin's Hand | Agent bridges (e.g. ValAgentBridge) |
|---|---|---|
| You write | C# test cases, run unattended | Nothing; an AI agent drives the live game |
| Best for | Repeatable regression suites, "did my change break anything", CI-style runs | Exploratory testing, poking at a running game |
| Result | A pass/fail report you can diff run to run | A conversation |

They complement each other. Odin's Hand is the one you run before every release.

## Quick start

Requirements: Windows, the Steam copy of Valheim (tested on 1.0.17), the [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer,
PowerShell 5.1+, and Steam running. BepInEx 5 is downloaded for you.

```powershell
dotnet new install OdinsHand.Templates
dotnet new odinshand -n MyMod --modGuid com.me.mymod     # a harness plugin next to your mod
cd MyMod
dotnet restore
.\odins.ps1 profile     # once: an isolated BepInEx dev profile (it never touches r2modman, Gale, or your game folder)
.\odins.ps1 run         # build, launch Valheim, run every case, print the report
```

Close Valheim first. If your game is not in the default Steam path, set the `VALHEIM_DIR` environment variable.

### Test your own mod

1. In the generated `*.Harness.csproj`, reference your mod (the commented `ProjectReference`).
2. Add your mod's DLL to `plugins` in `odinshand.json`.
3. Set `[BepInDependency]` in `HarnessPlugin.cs` to your mod's GUID.
4. Replace the starter cases in `Cases.cs`.

The harness is a **separate BepInEx plugin**, so the DLL you ship carries no test code. Never ship it.

## Writing cases

A case is a method. It takes a `HarnessContext` and returns `void` (nothing to wait for) or `IEnumerator` (a coroutine, so
it can wait frames and seconds, which most things in Unity need).

```csharp
[Cases(Category = "staff")]
public class StaffCases
{
    [Case("staff:casts")]
    public IEnumerator Casts(HarnessContext ctx)
    {
        ctx.Protect();                                   // god mode for this case; restored afterwards
        var player = Player.m_localPlayer;
        var before = player.GetEitr();

        VirtualInput.Active = true;
        VirtualInput.Attack = true;
        yield return ctx.Seconds(1f);

        ctx.Check("the buff appeared", player.GetSEMan().HaveStatusEffect(MyHash));
        ctx.CheckApprox("it cost 10 Eitr", before - player.GetEitr(), 10f, 0.2f);
        yield return ctx.Screenshot("staff_after_cast");
    }

    [AfterEach] public void Reset(HarnessContext ctx) => VirtualInput.Clear();
}
```

| Attribute | Meaning |
|---|---|
| `[Cases]` | Class holding cases. One instance per run. |
| `[Case("name", TimeoutSeconds = 600)]` | A case. The name is used in the report and by `-Only`. The timeout is for cases that are slow on purpose; the default is 300 s. |
| `[Skip("reason")]` | Recorded as a skip, never a failure. |
| `[Only]` | If anything has it, only those cases run. For focusing while you work. |
| `[NoPlayer]` | Valid from the main menu. Without it a case is skipped when there is no local player. |
| `[BeforeEach]`, `[AfterEach]` | Per-case hooks. `[AfterEach]` runs even if the case threw. |

Prefer subclassing? `HarnessCase` and `HarnessRunner.Add(...)` still work; override `HarnessHost.AddCases` and turn off
`DiscoverCasesAutomatically`.

### What a case can do (`HarnessContext`)

* **Checks** (they record, they never throw, so one failure does not hide the rest): `Check`, `CheckNotNull`, `CheckEqual`,
  `CheckApprox`, `Skip`.
* **Waiting**: `Frames(n)`, `Seconds(s)`, `WaitUntil(condition, timeout, onResult)`.
* **Screenshots**: `Screenshot(name)`, `ClipFrame(clip, index)` for assembling a short clip outside the game.
* **Helpers that put the world back** (undone after the case, even on a throw): `Protect(ghost)`, `Spawn(prefab, pos)`,
  `Watch(method)` (records calls and their arguments), `OnCleanup(action)`.
* **Input**: `VirtualInput` feeds synthetic movement, attack and jump to the local player.

### What the runner does for you

* Skips (rather than fails) cases whose preconditions are not met.
* Isolates cases: one that throws is recorded and the run continues.
* Abandons a case that exceeds its time limit and records the game state at that moment.
* Waits out a death and respawn instead of failing everything after it.
* **Fails the run if the engine logged errors**, which catches the exception thrown inside somebody's `Awake`.
* **Fails a run that executed nothing**, so a filter typo cannot look like success.
* Refuses to run against the wrong character, or an unintended server.

## Telemetry: where things happened

Every run records where the player was, so a failure that only happens in one biome, or far from the origin, is visible in the report
and not lost. This matters most for mods with random spawn points.

* `report.json` has a `"telemetry"` block: `character`, `world`, `seed`, `gameVersion`, `day`; `spawns` (each time the local player spawned: x, y, z,
  biome, zone); `runStart` and `runEnd`; and `cases`, with the start and end position and the seconds each case took.
* Spawns and the run start/end also carry `nearestBiomes`: for each other biome, the distance in metres, compass bearing and exact point of the
  nearest sample (`BlackForest 504 m NE`). It looks out to 3,000 m, so a biome further away is not listed, and a biome narrower than
  the gap between 32 search rays can be missed. `BiomeSearch.FindNearest(position)` does the same for any point.
* `report.txt` and the log end with the same facts in words, for example `spawn #1: (-4.3, 76.8, 6.6) Meadows, zone 0,0`.
* In a case: `HarnessTelemetry.LastSpawn` is the most recent spawn and `LocationSnapshot.Capture()` is the player right now, so a case can
  assert things like "the player is still within 500 m of where it spawned".

## `odinshand.json`

| Key | Meaning |
|---|---|
| `name` | Short project name. Dev profile = `<parent of project>\<name>Dev-profile`. |
| `guid` | Your harness plugin's BepInEx GUID. The script writes `BepInEx/config/<guid>.cfg` with `[AutoStart]` and `[Harness]`. |
| `reportName` | Your `HarnessHost.ReportName`. Reports go to `%TEMP%\<reportName>-harness`. |
| `logTag` | Your harness plugin's display name (used to tidy the log lines that are printed). |
| `solution` | Solution or project to build (relative to the project). |
| `plugins` | DLLs (relative paths) copied into the profile's plugin folder. Include your mod, your harness and `OdinsHand.dll`. |
| `audit` | DLLs checked by `odins.ps1 audit`. |
| `jotunn` | Optional version (e.g. `"2.30.2"`): copies `Jotunn.dll` from the NuGet cache into the profile, for mods that need it. |
| `tests` | Optional unit-test project run before each deploy. |
| `character`, `world` | Disposable test save names (defaults `OdinsTester`, `OdinsHandTest`). |
| `gameDir`, `profileDir` | Optional overrides. |

Commands: `profile`, `run`, `deploy`, `audit`. Switches for `run`: `-Only <text>`, `-KeepOpen`, `-NoDeploy`, `-TimeoutMinutes`,
`-Server host:port -ServerPassword pw` (join a dedicated server instead of a local world).

In game, the harness also adds a `<reportName>.selftest [filter]` console command.

## Safety

Cases spawn things, move the clock and change state, so the harness is strict about where it runs:

* It uses a **named, disposable, local** character and world, created on demand. It never "picks the first one".
* After selecting the character it **reads back which profile the game will actually load** and stops if it is not the
  configured one. (An early version of this library skipped that step and could run against whoever a human last played;
  see the changelog.)
* It will not run on a joined server unless you configured `Server` on purpose.

## Lessons baked in (they cost real debugging time)

* **The publicizer lies at runtime.** Referencing the *publicized* game assembly lets private members compile, but the real
  assembly still enforces access: the JIT rejects the whole method. Reach private members through `AccessTools`, and run
  `odins.ps1 audit`: it lists every non-public game member your DLL references.
* **Never call `Player.SetControls` yourself.** Vanilla calls it every frame; the last call wins. `VirtualInput` rewrites the arguments instead.
* **A new character plays an intro cutscene** over a valid spawn, and it starts a moment *after* the spawn callback.
  `Kit.WaitForPlayerReady()` waits for it.
* **Highlighting a character is not loading it.** Only `OnCharacterStart` commits the profile the game loads.
* **Restore state in `Cleanup`, not on the last line of `Run`.** A case that throws halfway otherwise poisons the cases after it.
  `Protect`/`Spawn`/`Watch` do this for you.
* **A skipped case is information; a spurious failure is noise.** Use `[NoPlayer]`, `[Skip]` or `CanRun`/`SkipReason`.
* **Compare against a baseline measured just before the action**, not a constant: vanilla's ambient effects (Cold, Wet, Rested) move your numbers.
* **Immortal targets** (the training dummy) never lose health. Record `Character.Damage` calls with `Watch` and read the `HitData`.
* **Mono inlines tiny methods** (under about 20 bytes of IL), so a `Watch` on a trivial getter can miss calls.
* **A key press does not reach mods that read `UnityEngine.Input` directly**, only ones that go through the game's input layer.

## Limits

* Windows and PowerShell only, for now. BepInEx 5 only (Valheim's current loader), not BepInEx 6.
* Needs the real game and Steam, so it cannot run on a hosted CI machine. Run it locally before you tag a release.
* The dedicated-server scripts used in the author's own repos are not generalised yet. `-Server` joins an existing server.
* Tested on Valheim 1.0.17. A game update can break access to internals; `audit` will tell you which.

## Building from source

```powershell
dotnet build OdinsHand.sln -c Release          # needs Valheim installed (the game DLLs are referenced in place)
.\scripts\Run-Harness.ps1                        # runs the bundled Smoke example in the real game
.\tools\Pack-Release.ps1                         # builds the NuGet package and the template into .\artifacts
```

## License

MIT, see [LICENSE](LICENSE). Valheim is a trademark of Iron Gate AB; this project is unaffiliated. No game files are
included or redistributed: game DLLs are referenced in place from your own install.
