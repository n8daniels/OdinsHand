# MyMod.Harness

Development-only in-game tests for MyMod, built on [Odin's Hand](https://github.com/n8daniels/OdinsHand).

```powershell
dotnet restore
.\odins.ps1 profile     # once: isolated BepInEx dev profile (downloads BepInEx)
.\odins.ps1 run         # build, launch Valheim, run every case, print pass/fail
```

Steam must be running and Valheim closed. The run uses a disposable character (`MyModTester`) and world (`MyModTest`),
both created locally, so it cannot touch a real save.

## Make it test your mod

1. Reference your mod in `MyMod.Harness.csproj` (the commented `ProjectReference`) so cases can reach its types.
2. Add your mod's DLL to `plugins` in `odinshand.json`, so it is deployed into the dev profile.
3. Replace the starter cases in `Cases.cs`.
4. Set `[BepInDependency]` in `HarnessPlugin.cs` to your mod's real GUID.

Never ship this assembly.
