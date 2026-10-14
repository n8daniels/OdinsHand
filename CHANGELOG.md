# Changelog

## Unreleased

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
