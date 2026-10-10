# Changelog

## 0.1.0

First public cut, extracted from the harness used to verify several of the author's Valheim mods.

* `OdinsHand` library: `HarnessRunner`, `HarnessCase`, `HarnessContext`, report writer, engine-error watcher.
* Public `AutoStart` (character / world / server from the main menu), `VirtualInput`, and `Kit` helpers.
* Generic scripts driven by `odinshand.json`: `New-DevProfile`, `Deploy-Dev`, `Run-Harness`, `Audit-Access` (+ `AccessAudit` tool).
* `examples/Smoke.Harness`: a working harness plugin that needs no mod; verified end to end (6/6) on Valheim 1.0.17.
* Not yet generalised: the dedicated-server scripts (they exist in the author's mod repos and will be folded in).
