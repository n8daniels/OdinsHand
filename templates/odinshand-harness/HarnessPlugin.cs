using BepInEx;
using OdinsHand;

namespace MyMod.Harness
{
    /// <summary>
    /// Development-only companion plugin that verifies the mod inside a live game. HarnessHost (Odin's Hand) provides the
    /// config, the disposable character and world, the safety checks, the console command and the report; cases are found
    /// by attribute in this assembly (see Cases.cs).
    /// </summary>
    [BepInPlugin("com.example.mymod.harness", "MyMod Harness (dev)", "1.0.0")]
    [BepInDependency("com.example.mymod", BepInDependency.DependencyFlags.SoftDependency)]
    public class HarnessPlugin : HarnessHost
    {
        // The report goes to %TEMP%\mymod-harness, and the console command is mymod.selftest.
        protected override string ReportName => "mymod";
    }
}
