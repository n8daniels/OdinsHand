# Getting started with Odin's Hand

A step-by-step guide for putting Odin's Hand on a Valheim mod you are building. It assumes you can already build a BepInEx mod.
For the full reference (every option, every helper), see the [README](../README.md).

## What you get

One command launches the **real game**, loads a throwaway test character and world by itself, runs the test cases you wrote against
live game objects, takes screenshots, quits, and prints a pass/fail report:

```
> .\odins.ps1 run
Launching Valheim with the MyMod dev profile...
[harness] PASS  mymod:world-is-up   a local player exists
[harness] PASS  mymod:world-is-up   vanilla content is loaded
[harness] PASS  mymod:replace-me    replace this with a check about your mod
[harness] PASS  engine              no engine errors during the run
[harness] 4 passed, 0 failed, 4 checks total.
```

It never touches your real characters or your normal Gale / r2modman profiles, and the test code is a **separate plugin that you never ship**.

## What you need

| Need | Notes |
|---|---|
| Windows 10 or 11 | The scripts are PowerShell. Windows only for now. |
| Valheim from Steam | Steam must be **running** when you start a test run. Valheim must be **closed**. |
| .NET SDK 6 or newer | Check with `dotnet --version`. Get it from https://dotnet.microsoft.com/download |
| PowerShell 5.1 or newer | Already part of Windows. |
| A mod that is a BepInEx 5 plugin | Odin's Hand is itself a BepInEx plugin, so it only works with BepInEx 5 mods (not BepInEx 6). |
| Internet, first time only | To fetch NuGet packages and BepInEx. |

You do **not** need to install BepInEx: the setup step downloads it into a separate, isolated test profile.

If Valheim is not in the default Steam folder, set the `VALHEIM_DIR` environment variable to its folder.

## Quick start (about 10 minutes)

Open PowerShell in the folder that holds your mod's repo (put the new harness next to your mod, not inside the mod's source folder):

```powershell
dotnet new install OdinsHand.Templates
dotnet new odinshand -n MyMod --modGuid com.you.mymod       # use your mod's name and its BepInEx GUID
cd MyMod
dotnet restore
.\odins.ps1 profile -BepInExVersion 5.4.23.5                # once: builds the isolated test profile
.\odins.ps1 run                                             # builds, launches Valheim, runs the cases, prints the report
```

* `-BepInExVersion 5.4.23.5` is only needed with Odin's Hand 0.3.1 or earlier. Newer versions use it by default. An older BepInEx prints
  `Unable to start Unity log writer` (and, on a server console, a scary-looking "mono runtime is out of sync" message). The test still
  works, but update BepInEx to get rid of it.
* The first run takes a minute or two. Do not touch the game window while it runs. It quits by itself.
* If it prints the four `PASS` lines above, your setup works.

### What the template created

| File | What it is |
|---|---|
| `MyMod.Harness.csproj` | The harness plugin project. References the `OdinsHand` NuGet package. |
| `HarnessPlugin.cs` | About ten lines: a `HarnessHost` subclass that names the report. |
| `Cases.cs` | Your test cases. Two starter cases to replace. |
| `odinshand.json` | Settings for the scripts (see below). |
| `odins.ps1` | The one command you use: `profile`, `run`, `deploy`, `audit`, `clean`, `bundle`, `server`. |
| `build/`, `Directory.Build.props` | Where the game's DLLs are found. Normally you never edit these. |

The **test profile** is created *beside* your project folder (for example `C:\mods\MyModDev-profile`), outside your repo. It holds
BepInEx, your mod, and the harness, and nothing else.

## Point it at your mod

1. **Reference your mod** in `MyMod.Harness.csproj`, so your cases can use your mod's own classes (uncomment the line and fix the path):
   ```xml
   <ProjectReference Include="..\MyMod\src\MyMod\MyMod.csproj"><Private>false</Private></ProjectReference>
   ```
   If your mod targets `netstandard2.1`, make the harness target `netstandard2.1` too. Building worked for us without extra settings. If NuGet
   ever complains that the package is not compatible, add `<AssetTargetFallback>$(AssetTargetFallback);net472</AssetTargetFallback>`
   inside the `PropertyGroup` (a harmless warning, `NU1701`, then appears).
2. **Add your mod's DLL to `odinshand.json`**, so it is copied into the test profile. Paths are relative to the folder holding `odinshand.json`:
   ```json
   "plugins": [
     "bin/Release/MyMod.Harness.dll",
     "bin/Release/OdinsHand.dll",
     "../MyMod/src/MyMod/bin/Release/MyMod.dll"
   ],
   ```
   Also set `"solution"` to the solution that builds both your mod and the harness (a `.sln`), or leave it as the harness `.csproj` if the
   project reference builds your mod for you.
3. **Set your mod's real GUID** in the `[BepInDependency]` line of `HarnessPlugin.cs`. It is a soft dependency by default; make it
   `HardDependency` if the harness must not load without your mod.
4. **If your mod needs Jötunn**, add `"jotunn": "2.30.2"` (your Jötunn version) to `odinshand.json`, run `dotnet restore`, then
   `.\odins.ps1 profile` again. It copies `Jotunn.dll` into the test profile.
5. **If your mod's own build copies itself into your game or Gale profile**, make sure that does not point at anything you care about while
   you test. Odin's Hand never needs it: it deploys by itself to the test profile.

## Write your first real case

A case is a method that takes a `HarnessContext` and returns `void` (nothing to wait for) or `IEnumerator` (it can wait for frames and seconds,
which most things in Unity need). Edit `Cases.cs`:

```csharp
using System.Collections;
using OdinsHand;
using UnityEngine;

namespace MyMod.Harness
{
    [Cases(Category = "mymod")]
    public class MyCases
    {
        [Case("mymod:item-exists")]
        public void ItemExists(HarnessContext ctx)
        {
            var prefab = ObjectDB.instance.GetItemPrefab("MyModSword");
            ctx.CheckNotNull("the sword is registered", prefab);
        }

        [Case("mymod:effect-applies")]
        public IEnumerator EffectApplies(HarnessContext ctx)
        {
            ctx.Protect();                                    // god mode for this case, put back afterwards
            var player = Player.m_localPlayer;

            player.GetSEMan().AddStatusEffect("SE_MyMod".GetStableHashCode());   // your mod's effect
            yield return ctx.Seconds(1f);                     // give the game time to apply it

            ctx.Check("the effect is active", player.GetSEMan().HaveStatusEffect("SE_MyMod".GetStableHashCode()));
            yield return ctx.Screenshot("mymod_effect");     // a picture, saved with the report
        }
    }
}
```

### What a case can use

| Need | Use |
|---|---|
| Record a result (never throws, so one failure does not hide the rest) | `ctx.Check(what, condition, detail)`, `CheckEqual`, `CheckApprox`, `CheckNotNull` |
| Say a case cannot run here | `ctx.Skip(what, reason)` or the `[Skip("reason")]` attribute |
| Wait | `yield return ctx.Seconds(n)`, `ctx.Frames(n)`, `ctx.WaitUntil(condition, timeout, onResult)` |
| A screenshot | `yield return ctx.Screenshot("name")` |
| Keep the player alive | `ctx.Protect()` (restored after the case, even if it threw) |
| Spawn something, cleaned up afterwards | `ctx.Spawn("Boar", ctx.InFrontOfPlayer(5f))` |
| Record calls to a game or mod method | `ctx.Watch(method)` |
| Undo anything else | `ctx.OnCleanup(() => ...)` |
| Move the player | `VirtualInput.Active = true; VirtualInput.MoveDir = Vector3.forward;` (then `VirtualInput.Clear()`) |
| An error your case provokes on purpose | `ctx.ExpectLogError("text")` |

Attributes: `[Cases]` (the class), `[Case("name")]`, `[Skip("why")]`, `[Only]` (run just this while you work on it), `[NoPlayer]` (valid
from the main menu), `[BeforeEach]`, `[AfterEach]`, and `[Case("name", TimeoutSeconds = 600)]` for a case that is slow on purpose.
A case is skipped, not failed, when there is no local player.

**Private game members:** the game assembly is "publicized" at compile time, but the real game still enforces access at runtime, so reaching a
private member compiles and then crashes. Use `HarmonyLib.AccessTools` for private members, and run `.\odins.ps1 audit` to list every
non-public game member your DLLs touch.

## Everyday commands

```powershell
.\odins.ps1 run                      # the whole suite
.\odins.ps1 run -Only effect         # only cases whose name contains "effect"
.\odins.ps1 run -KeepOpen            # leave the game open afterwards
.\odins.ps1 run -NoDeploy            # do not rebuild first
.\odins.ps1 run -TimeoutMinutes 20   # a long suite
.\odins.ps1 deploy                   # build and copy into the test profile, without running
.\odins.ps1 audit                    # non-public game members your DLLs reference
.\odins.ps1 bundle                   # a small redacted zip of the last run, for a bug report
.\odins.ps1 clean                    # DRY RUN: lists the test characters it could remove (add -Apply to move them to a backup)
.\odins.ps1 server                   # the same cases against a local dedicated server (needs a "server" section, see the README)
```

### `odinshand.json`

| Key | Meaning |
|---|---|
| `name` | Short project name. The test profile is `<parent folder>\<name>Dev-profile`. |
| `guid` | Your harness plugin's BepInEx GUID. |
| `reportName` | Matches `ReportName` in `HarnessPlugin.cs`. Reports go to `%TEMP%\<reportName>-harness`. |
| `solution` | What to build before each run. |
| `plugins` | The DLLs copied into the test profile: your mod, the harness, and `OdinsHand.dll`. |
| `character`, `world` | The disposable test save names. Created locally on first use. |
| `jotunn` | Optional Jötunn version to install into the test profile. |
| `failOnLogErrors` | `true` makes an unexpected error in BepInEx's log fail the run. Default: a warning. |
| `keepRuns` | How many past runs to keep as small zips (default 5). |

## Reading the results

* **Console:** one line per check, then totals. `PASS`, `FAIL`, `SKIP` (could not be checked), `ERROR` (a case threw), and `WARN` (worth reading,
  not a failure: for example errors written to BepInEx's log).
* **`%TEMP%\<reportName>-harness\report.json`:** the same results as data, with totals (`passed`, `failed`, `warnings`, `ok`), the Odin's Hand,
  BepInEx and game versions, every loaded plugin, and where the player was (spawn, run start and end, each case, nearest other biomes, weather and
  status effects). `report.txt` is the readable version, and the screenshots sit beside them.
* **The last 5 runs** are kept as small zips under `%LOCALAPPDATA%\OdinsHand\runs\<name>`.
* **Errors in BepInEx's log** (including plugins that failed to load) are listed after the run and saved as `bepinex-log-problems.txt`.

A run that executes **zero** cases fails on purpose, so a mistyped `-Only` cannot look like success.

## Safety

* The harness uses a **named, disposable, local** test character and world (`MyModTester`, `MyModTest`). It does not pick "the first character".
* After choosing the character it checks which profile the game will actually load and **refuses to run** if it is not the configured one.
* It will not run on a joined server unless you asked for one.
* **Test characters pile up.** `.\odins.ps1 clean` removes them safely: dry run by default, exact names only, moves files to a backup instead of
  deleting, checks every other save is unchanged, and refuses while Valheim is running.
* **Never ship the harness.** It is a separate plugin: only your mod's DLL goes into your package. Add a guard to your packaging script so a
  mistake cannot slip through:
  ```powershell
  $leaked = Get-ChildItem $staging -Recurse -File | Where-Object { $_.Name -match 'Harness|OdinsHand' }
  if ($leaked) { throw "Test files in the package: $($leaked.Name -join ', ')" }
  ```

## When something goes wrong

| You see | What it means |
|---|---|
| `Valheim is already running` | Close the game first. The run needs a clean main menu. |
| Nothing happens, or the run times out | Steam must be running. Check the log line it prints at the end. |
| `No odinshand.json` | Run the command from your harness project's folder. |
| `No dev profile at ...` | Run `.\odins.ps1 profile` once. |
| `harness refusing to run: loaded character is 'X', not the configured 'Y'` | It stopped to protect a real save. Do not override it. Run `clean`, close the game, retry. |
| `Unable to start Unity log writer` | The profile's BepInEx is too old for this Valheim. Re-run `.\odins.ps1 profile -BepInExVersion 5.4.23.5`. |
| `Could not publicize ...` during the build (Jötunn 2.30) | A parallel-build race. The scripts retry on a single node by themselves. |
| A check passes sometimes and fails sometimes | Compare against a baseline measured just before the action, not a constant. A **new** test character also settles over its first run or two. |
| Map-driven cases fail or skip | A no-map world (or character) cannot open the map. The report says `noMap: true`. |
| "A case exceeded its time limit" | Give a slow case `[Case("name", TimeoutSeconds = 600)]`. |
| The build says a game member is not accessible at runtime | Use `AccessTools` for it (see above), and run `audit`. |

## Using it with an AI coding agent (Claude Code, Codex, and similar)

Odin's Hand is plain command-line tooling with text and JSON output, and it makes **no calls to any AI or online service**, so a coding agent
can drive it like any other build tool. The whole edit, run, read-the-report, fix loop works from the agent's terminal.

What to know:

* **It must run on your own Windows machine**, where Valheim and Steam are. A cloud or sandboxed agent that cannot launch the game cannot run it. It can
  still write the cases, which you then run.
* The agent should read **`report.json`** (`ok`, `failed`, `warnings`, and `results`) rather than scraping the console, and treat `WARN` lines as worth
  reading.
* Runs take minutes and open a game window. Tell the agent to run the whole suite rarely, and to use `-Only` while iterating.
* Keep the agent away from your saves. The only thing it should ever remove is a test character, through `clean`.

Paste this into **`CLAUDE.md`** (Claude Code) or **`AGENTS.md`** (Codex and other agents) at the root of your mod's repo:

```markdown
## In-game tests (Odin's Hand)

This mod has an in-game test harness in `MyMod/` (a separate, dev-only plugin; never ship it). Run it from that folder in PowerShell:

- `.\odins.ps1 run` builds, launches Valheim, runs every case, and prints a report. Steam must be running and Valheim closed.
- `.\odins.ps1 run -Only <text>` runs only cases whose name contains the text. Prefer this while iterating.
- Read `%TEMP%\mymod-harness\report.json` (`ok`, `failed`, `warnings`, `results`, and `telemetry` for where the player was). `WARN` lines are errors
  written to BepInEx's log: read them. Do not scrape the console.
- Cases are methods in `Cases.cs` with `[Case("name")]`, taking a `HarnessContext`. Use `ctx.Check...`, `yield return ctx.Seconds(n)`,
  `ctx.Protect()`, `ctx.Spawn(...)`. Private game members need `AccessTools`; run `.\odins.ps1 audit`.
- Never delete or edit Valheim save files by hand. The only allowed cleanup is `.\odins.ps1 clean` (a dry run first; show me the list before `-Apply`).
- The harness refuses to run against a character other than the configured test character. If it refuses, stop and tell me; never work around it.
- A full run takes minutes and opens a game window. Run it sparingly, and never while Valheim is already open.
- Never put the harness or `OdinsHand.dll` in the release package.
```

I have used this workflow with Claude Code throughout building Odin's Hand. I have not tried Codex, but nothing in the tool is specific to either: anything
that can run PowerShell on your machine and read a JSON file can drive it.

## Getting help

* **Bug reports:** run `.\odins.ps1 bundle` right after the run that showed the problem. It writes one small zip (a few KB) with the report, the
  game log, your config and a system summary. Your Windows user name, machine name, Steam ids, e-mail addresses and server addresses are
  redacted, screenshots are left out, and **nothing is uploaded**. Open the zip and read it, then attach it to an issue at
  https://github.com/n8daniels/OdinsHand/issues.
* **Everything else:** the [README](../README.md) covers attributes, dedicated-server runs, telemetry, run history and the cleanup tool in detail.

Odin's Hand is MIT-licensed. Valheim is a trademark of Iron Gate AB; this project is unaffiliated, and no game files are included.
