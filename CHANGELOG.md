# Changelog

## 0.3.0

* **Dedicated-server runs.** `Run-ServerHarness.ps1` / `odins.ps1 server` starts a local dedicated server (your mod, with server config that can differ from the client's), joins it with the harness, runs the cases, stops the server and scans ITS logs for errors. `New-ServerProfile.ps1` builds the server profile. Configured by a `server` section in `odinshand.json`. Generalised from BubbleBabies' scripts.
* **The no-map setting is recorded** in each report (`telemetry.noMap`), so a run on a no-map server explains why map-driven cases could not run.
* **Conditions are recorded with every location:** weather, how far through the day, the Run skill and the active status effects (Wet, Cold, Rested, SoftDeath...). Found while chasing a mod's state-dependent test failures: a number that moves with the character's condition is invisible in a report that does not say what the condition was.
* **Readiness now waits for the player to land.** `Kit.WaitForPlayerReady` additionally requires the player on the ground (or swimming) for a second. Joining a server starts the valkyrie intro later than a local world does, and the old check passed in the moment before the flight began.
* **The compiled library no longer embeds the build machine's path** (it recorded the full `.pdb` path, including the Windows user name). Fixed with a path map.

* **Run history, kept small.** After each `run` the report and game log are saved as one zip (a few KB) under `%LOCALAPPDATA%\OdinsHand\runs\<project>`; only the newest `keepRuns` (default 5) are kept. Screenshots are not kept.
* **Bug-report bundle.** `New-BugReport.ps1` / `odins.ps1 bundle` zips the latest run for a GitHub issue, with usernames, paths, Steam ids, e-mail addresses and server addresses/passwords redacted. Nothing is uploaded. Only the newest 3 bundles are kept.
* **Reports now record the environment**: Odin's Hand and BepInEx versions and every loaded plugin (GUID and version), under `telemetry`.
* **`Clean-TestSaves.ps1` / `odins.ps1 clean`.** Removes the disposable test characters harnesses leave behind. Dry run by default; exact names from `odinshand.json` only; searches local and Steam-cloud saves; moves to a backup instead of deleting; verifies every other save is unchanged; refuses while Valheim is running. Worlds are not handled.
* **Distance to other biomes.** Each spawn, and the start and end of every run, records the nearest point of every other biome: distance in metres, compass bearing and the exact point (for example `BlackForest 504 m NE`). It samples the world generator on rings out to 3,000 m (40 m apart, 32 rays) and bisects to the edge, so it is accurate to a few metres along a ray and can miss a biome thinner than the 11-degree gap between rays; a biome further than 3 km is not listed. About a millisecond per search. `BiomeSearch.FindNearest(position)` is public for cases. Per-case positions do not include it, to keep them cheap.
* The Smoke example's movement case now tries each direction: the persistent test character can end a run facing an obstacle.
* **Location telemetry.** Every report now records where things happened: the spawn location of each spawn (position, biome, zone), where the run started and ended, and where each case started and ended and how long it took, plus the character, world, seed, game version and day. It is in `report.json` under `"telemetry"`, in `report.txt`, and in the log. Built for mods with random spawn points: a failure that only happens in one biome is otherwise invisible. `HarnessTelemetry.Spawns` / `LastSpawn` and `LocationSnapshot.Capture()` are public for cases to use.
* `Kit.WaitForPlayerReady` also waits out `Player.InIntro()` (the valkyrie flight of a new character, which is not `InCutscene`).
* `Deploy-Dev.ps1` retries a failed build on a single node: Jotunn 2.30 projects race on publicizing the same game DLL.

## 0.2.1

Distribution release: it can now be adopted by someone other than the author.

* **NuGet packages.** `OdinsHand` (the library, its XML docs, and the scripts under `tools/scripts`) and `OdinsHand.Templates`
  (`dotnet new odinshand -n MyMod`), which scaffolds a harness plugin with a one-command `odins.ps1`.
* **`HarnessHost`.** A ready-made harness plugin base class: config, auto-start, the safety checks, a
  `<reportName>.selftest` console command, run-on-first-spawn and quit-when-done. A harness plugin is now about ten lines
  (Waymark's went from 239 to 30).
* **Attribute cases.** `[Cases]`, `[Case]`, `[Skip]`, `[Only]`, `[NoPlayer]`, `[BeforeEach]`, `[AfterEach]`. Class-based
  `HarnessCase` still works.
* **Helpers that undo themselves** after the case, even on a throw: `ctx.Protect`, `ctx.Spawn`, `ctx.Watch`, `ctx.OnCleanup`.
* **Per-case time limit** (`HarnessCase.TimeoutSeconds`, `[Case(TimeoutSeconds = ...)]`, default 300 s). A case that hangs is
  abandoned and the error records what the game was doing at that moment.
* **`New-DevProfile` downloads BepInEx** (5.4.23.2) when `-CoreSource` is not given.
* **No Jotunn dependency.** The library never used it; `"jotunn"` in `odinshand.json` is now purely opt-in.
* **A run that executes zero cases now fails** instead of reporting "0 failed". A mistyped filter used to look like success.
* `Kit.WaitForPlayerReady` waits 3 s before polling for the intro cutscene (the spawn callback fires before it starts).

### Fixed: AutoStart could run against a real character

In 0.1.0 `AutoStart`'s local-world path selected the test character in the menu but never called `OnCharacterStart`, the call
that makes the game load it. The game instead loaded **whichever character a person last played on that machine**, and the
harness reported on that. The 0.1.0 "verified 6/6" run was against such a character. `AutoStart` now commits the character the
way vanilla does and reads back `Game.m_profileFilename`, stopping if it is not the configured one. `HarnessHost` additionally
refuses to run if the loaded character's name differs. If you used 0.1.0, upgrade; a harness that touched a real save is the
worst thing a test tool can do.

## 0.1.0

First public cut, extracted from the harness used to verify several of the author's Valheim mods.

* `OdinsHand` library: `HarnessRunner`, `HarnessCase`, `HarnessContext`, report writer, engine-error watcher.
* Public `AutoStart` (character / world / server from the main menu), `VirtualInput`, and `Kit` helpers.
* Generic scripts driven by `odinshand.json`: `New-DevProfile`, `Deploy-Dev`, `Run-Harness`, `Audit-Access` (+ `AccessAudit` tool).
* `examples/Smoke.Harness`: a harness plugin that needs no mod.
