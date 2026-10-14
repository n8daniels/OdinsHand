using BepInEx;
using OdinsHand;

namespace Smoke.Harness
{
    /// <summary>
    /// A complete harness plugin. HarnessHost provides the config, auto-start, safety checks, console command and
    /// report; cases are found by attribute (see SmokeCases.cs). Keep the GUID's ".harness" suffix convention.
    /// </summary>
    [BepInPlugin("odinshand.smoke.harness", "Smoke Harness (dev)", "0.2.0")]
    public class SmokePlugin : HarnessHost
    {
        // Reports go to %TEMP%\smoke-harness (scripts/Run-Harness.ps1 reads the same name from odinshand.json).
        protected override string ReportName => "smoke";
    }
}
