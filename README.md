# Odin's Hand

**An in-game test harness for Valheim (BepInEx) mods.** One command launches the real game, loads a disposable test
character and world by itself, runs your test cases against live game objects, takes screenshots, quits, and prints a
pass/fail report. No clicking through menus, no "I think it works".

```
> .\scripts\Run-Harness.ps1
Launching Valheim with the Smoke dev profile...
[harness] PASS  smoke:player          a local player exists
[harness] PASS  smoke:virtual-input   walking forward moves the player  [2.3 m]
[harness] PASS  smoke:screenshot      a screenshot was captured
[harness] 6 passed, 0 failed, 6 checks total.
```

It was extracted from the harness that verified several of the author's Valheim mods
(thousands of in-game checks, single-player and against a dedicated server).

## What you get

| Piece | What it does |
|---|---|
| `OdinsHand` library (`src/OdinsHand`) | `HarnessRunner`, `HarnessCase`, `HarnessContext` (checks, waits, screenshots), a report writer, and a watcher that fails the run if the engine logs errors. |
| `AutoStart` | From the main menu: creates/loads a **named** test character, creates a local test world (fixed seed) or joins a server, and starts the game. Cannot wander into a real save. |
| `VirtualInput` | Feeds synthetic movement / attack / jump to the local player (the reliable way: it rewrites the arguments of vanilla's own `Player.SetControls` call). |
| `Kit` | `WaitForPlayerReady()` (waits out the new-character cutscene), `OnFirstSpawn(...)`, one-line Harmony install. |
| `scripts/` | `New-DevProfile`, `Deploy-Dev`, `Run-Harness`, `Audit-Access` — a standalone BepInEx profile so it never touches r2modman/Gale or your game folder. |
| `examples/Smoke.Harness` | A complete, working harness plugin (~150 lines) that needs no mod. Copy it. |

## Requirements

* Windows, Steam copy of Valheim (tested on 1.0.17), .NET SDK 6+ (`dotnet`), PowerShell 5.1+.
* A BepInEx 5.4.2x **core** folder (the one containing `BepInEx.Preloader.dll`) — from the BepInEx release zip or any working install.
* BepInEx 5 only (Valheim's current loader). Not for BepInEx 6.

## Quick start (try it on the included example)

```powershell
git clone https://github.com/n8daniels/OdinsHand
cd OdinsHand
# once: make the isolated dev profile (point -CoreSource at BepInEx\core from the BepInEx zip or another install)
.\scripts\New-DevProfile.ps1 -CoreSource C:\path\to\BepInEx\core
# every time:
.\scripts\Run-Harness.ps1
```

Steam must be running; close Valheim first. If your game is not in the default Steam path, set the `VALHEIM_DIR`
environment variable or `"gameDir"` in `odinshand.json`.

## Use it on your own mod

1. **Make a harness plugin** next to your mod: copy `examples/Smoke.Harness`, rename it, add your mod as a reference. It is a separate
   BepInEx plugin, so your shipped DLL carries no test code.
2. **Write cases.** A case is a coroutine so it can wait for frames and seconds:

   ```csharp
   internal sealed class MyStaffCase : HarnessCase
   {
       public override string Name => "mystaff:casts";
       public override bool CanRun(HarnessContext c) => Player.m_localPlayer != null;   // skipped, not failed, from the menu

       public override IEnumerator Run(HarnessContext ctx)
       {
           var p = Player.m_localPlayer;
           // ...give the player an item, equip it, press attack with VirtualInput...
           VirtualInput.Active = true; VirtualInput.Attack = true;
           yield return ctx.Seconds(1f);
           ctx.Check("the buff appeared", p.GetSEMan().HaveStatusEffect(MyHash));
           ctx.CheckApprox("it cost 10 Eitr", before - p.GetEitr(), 10f, 0.2f);
           yield return ctx.Screenshot("mystaff_after_cast");
       }

       public override void Cleanup(HarnessContext c) => VirtualInput.Clear();   // runs even if Run threw
   }
   ```
3. **Add `odinshand.json`** to your project root (see below) and run `.\path\to\OdinsHand\scripts\Run-Harness.ps1 -Project .`
   (or copy the `scripts` folder into your repo).

### `odinshand.json`

| Key | Meaning |
|---|---|
| `name` | Short project name. Dev profile = `<parent of project>\<name>Dev-profile`. |
| `guid` | Your harness plugin's BepInEx GUID. The script writes `BepInEx/config/<guid>.cfg` with `[AutoStart]` and `[Harness]`. |
| `reportName` | The name you pass to `new HarnessRunner("<reportName>", …)`. Reports go to `%TEMP%\<reportName>-harness`. |
| `logTag` | Your harness plugin's display name (used to tidy the log lines that are printed). |
| `solution` | Solution to build (relative to the project). |
| `plugins` | DLLs (relative paths) copied into the profile's plugin folder. Include your mod, your harness and `OdinsHand.dll`. |
| `audit` | DLLs checked by `Audit-Access.ps1`. |
| `jotunn` | Optional version (e.g. `"2.30.2"`): copies `Jotunn.dll` from the NuGet cache into the profile. |
| `tests` | Optional unit-test project run before each deploy. |
| `character`, `world` | Disposable test save names (defaults `OdinsTester`, `OdinsHandTest`). |
| `gameDir`, `profileDir` | Optional overrides. |

Script switches: `-Only <text>` (run cases whose name contains it), `-KeepOpen`, `-NoDeploy`, `-TimeoutMinutes`,
`-Server host:port -ServerPassword pw` (join a dedicated server instead of a local world).

## Lessons baked in (they cost real debugging time)

* **The publicizer lies at runtime.** Referencing the *publicized* game assembly lets private members compile, but the real
  assembly still enforces access: the JIT rejects the whole method. Reach private members through `AccessTools`, and run
  `scripts/Audit-Access.ps1` — it lists every non-public game member your DLL references.
* **Never call `Player.SetControls` yourself.** Vanilla calls it every frame; the last call wins. `VirtualInput` rewrites the arguments instead.
* **A new character plays an intro cutscene** over a valid spawn. `Kit.WaitForPlayerReady()` waits it out.
* **Restore state in `Cleanup`, not on the last line of `Run`** — a case that throws halfway otherwise poisons the cases after it.
* **A skipped case is information; a spurious failure is noise.** Use `CanRun` / `SkipReason`.
* **Compare against a baseline measured just before the action**, not a constant: vanilla's ambient effects (Cold, Wet, Rested…) move your numbers.
* **Immortal targets** (the training dummy) never lose health — record `Character.Damage` calls instead and read type/amount/knockback from the `HitData`.

## Status

0.1.0. The core is the code that verified the mods mentioned above; this repo repackages it under one namespace (`OdinsHand`) with
generic scripts. Windows + PowerShell only for now. Issues and pull requests welcome.

*Migrating from the old copy-pasted `Valheim.ModHarness`:* change `using Valheim.ModHarness;` to `using OdinsHand;`, and move
`AutoStart` / `VirtualInput` usage to the library versions (they are public here).

## License

MIT — see [LICENSE](LICENSE). Valheim is a trademark of Iron Gate AB; this project is unaffiliated. No game files are
included or redistributed: game DLLs are referenced in place from your own install.
