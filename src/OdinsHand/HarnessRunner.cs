using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Runs a set of cases inside a live game and writes a report.
    ///
    /// Usage from a plugin:
    ///
    ///     var harness = new HarnessRunner("MyMod", Logger.LogInfo);
    ///     harness.Add(new MyCase());
    ///     StartCoroutine(harness.Run());
    ///
    /// Then read the report file. Nothing here is specific to any mod.
    /// </summary>
    public sealed class HarnessRunner
    {
        private readonly List<HarnessCase> _cases = new List<HarnessCase>();
        private readonly List<HarnessResult> _results = new List<HarnessResult>();
        private readonly Action<string> _log;
        private readonly string _name;

        /// <summary>Where the report and screenshots are written.</summary>
        public string OutputDirectory { get; set; }

        /// <summary>Whether cases may capture screenshots.</summary>
        public bool CaptureScreenshots { get; set; } = true;

        public bool IsRunning { get; private set; }

        /// <summary>Where the player was and where each case happened. Filled in by <see cref="Run"/>.</summary>
        public HarnessTelemetry Telemetry { get; private set; } = new HarnessTelemetry();

        /// <summary>
        /// When true, an unexpected error in BepInEx's log during the run fails it. When false (the default) it is reported as a WARN.
        /// </summary>
        public bool FailOnLogErrors { get; set; }

        private BepInExLogWatch _logWatch;

        /// <summary>
        /// Longest a single case may run before it is abandoned and recorded as an error, in seconds. A case that waits
        /// forever for something that will never happen would otherwise hold the whole run until the outer timeout.
        /// 0 disables it.
        /// </summary>
        public float CaseTimeoutSeconds { get; set; } = 300f;

        public HarnessRunner(string name, Action<string> log)
        {
            _name = string.IsNullOrEmpty(name) ? "harness" : name;
            _log = log ?? (_ => { });

            // Temp rather than the game folder: a harness must not litter someone's
            // install, and temp is somewhere a build script can reliably find.
            OutputDirectory = Path.Combine(Path.GetTempPath(), _name + "-harness");
        }

        public HarnessRunner Add(HarnessCase testCase)
        {
            if (testCase != null) _cases.Add(testCase);
            return this;
        }

        public HarnessRunner AddRange(IEnumerable<HarnessCase> cases)
        {
            if (cases != null) foreach (var c in cases) Add(c);
            return this;
        }

        /// <summary>
        /// Runs every case, or only those whose name contains <paramref name="filter"/>.
        ///
        /// Individual cases are isolated: one throwing is recorded and the run continues,
        /// because the value of an in-game run is learning as much as possible from the
        /// one session someone paid for by logging in.
        /// </summary>
        public IEnumerator Run(string filter = null)
        {
            if (IsRunning)
            {
                _log("[harness] already running.");
                yield break;
            }

            IsRunning = true;
            _results.Clear();
            Telemetry = new HarnessTelemetry();
            Telemetry.CaptureContext();
            Telemetry.RunStart = LocationSnapshot.Capture(biomes: true);

            var context = new HarnessContext(_results, _log,
                CaptureScreenshots ? OutputDirectory : null);

            _log($"[harness] {_name}: starting {_cases.Count} case(s).");

            // The engine's own errors count too. Everything here used to be judged on what
            // the mod chose to log, which is exactly the half that cannot report a
            // NullReferenceException thrown inside somebody's Awake.
            var errors = new UnityErrorWatch();
            errors.Start();

            // BepInEx's own logger, which UnityErrorWatch cannot hear: a plugin's Logger.LogError goes there.
            _logWatch = new BepInExLogWatch(() => context.CurrentCase);
            context.LogWatch = _logWatch;
            _logWatch.Start();

            var selected = 0;
            foreach (var testCase in _cases)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    testCase.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                selected++;

                context.CurrentCase = testCase.Name;

                // A death is recoverable; treating it as the end of the run is not. When
                // the player dies mid-suite every later case fails its own precondition
                // and reports "no local player", so one bad moment reads in the report as
                // sixty broken features. Waiting for the respawn turns that back into the
                // single incident it actually was.
                if (Player.m_localPlayer == null)
                    yield return WaitForPlayer(context);

                var runnable = false;
                try { runnable = testCase.CanRun(context); }
                catch (Exception e) { context.Error("CanRun", e); }

                if (!runnable)
                {
                    context.Skip(testCase.Name, testCase.SkipReason);
                    continue;
                }

                _log($"[harness] -> {testCase.Name}");
                var caseTelemetry = new CaseTelemetry { Case = testCase.Name, Start = LocationSnapshot.Capture() };
                var caseStarted = UnityEngine.Time.realtimeSinceStartup;
                yield return RunIsolated(testCase, context);
                caseTelemetry.End = LocationSnapshot.Capture();
                caseTelemetry.Seconds = UnityEngine.Time.realtimeSinceStartup - caseStarted;
                Telemetry.Cases.Add(caseTelemetry);

                // Always, and outside RunIsolated, which exits by yield break down several
                // paths and so cannot be relied on to reach a trailing statement.
                try { testCase.Cleanup(context); }
                catch (Exception e) { context.Error("cleaning up after " + testCase.Name, e); }
                context.RunCleanups();
                _logWatch.ClearExpectations();
            }

            errors.Dispose();
            _logWatch.Stop();

            // A run that executed nothing must not read as a pass. "0 failures" from zero cases is the quietest way for a
            // harness to lie: a filter typo, or cases the plugin never found, look identical to success.
            context.CurrentCase = "harness";
            if (selected == 0)
            {
                context.Check("at least one case ran", false,
                    _cases.Count == 0
                        ? "no cases were registered. With attributes: is [Cases] on a public class in the harness assembly, with [Case] methods taking a HarnessContext?"
                        : $"the filter '{filter}' matched none of the {_cases.Count} registered case(s)");
            }

            context.CurrentCase = "bepinex-log";
            var unexpected = _logWatch.Problems.FindAll(p => !p.Expected);
            if (unexpected.Count > 0)
            {
                var shown = new System.Text.StringBuilder();
                for (var i = 0; i < unexpected.Count && i < 3; i++)
                {
                    if (i > 0) shown.Append(" | ");
                    shown.Append('[').Append(unexpected[i].Case).Append("] ").Append(unexpected[i].Source).Append(": ").Append(unexpected[i].Message.Length > 120 ? unexpected[i].Message.Substring(0, 120) + "..." : unexpected[i].Message);
                }
                var detail = $"{unexpected.Count} error(s) in BepInEx's log: {shown}" + (unexpected.Count > 3 ? " ..." : "");
                if (FailOnLogErrors) context.Check("no unexpected errors in BepInEx's log during the run", false, detail);
                else context.Warn("errors were written to BepInEx's log during the run", detail);
            }
            else if (FailOnLogErrors) context.Check("no unexpected errors in BepInEx's log during the run", true);

            context.CurrentCase = "engine";
            foreach (var line in errors.Summary) context.Log("[unity] " + line);

            context.Check("no engine errors during the run", errors.TotalCount == 0,
                errors.TotalCount == 0
                    ? null
                    : $"{errors.TotalCount} Unity error(s) in {errors.DistinctCount} distinct place(s) - see the [unity] lines above");

            Telemetry.RunEnd = LocationSnapshot.Capture(biomes: true);
            IsRunning = false;
            Report(context);
        }

        /// <summary>
        /// Waits for a local player to exist again, up to a limit.
        ///
        /// Valheim destroys the player object on death and builds a new one on respawn, so
        /// there is a window with no local player at all. Cases that need one skip during
        /// it; this closes the window rather than letting it swallow the rest of the run.
        /// </summary>
        private IEnumerator WaitForPlayer(HarnessContext context)
        {
            const float limitSeconds = 45f;
            var deadline = UnityEngine.Time.realtimeSinceStartup + limitSeconds;
            var waited = false;

            while (UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                if (Player.m_localPlayer != null)
                {
                    if (waited) _log("[harness] player is back; carrying on");
                    yield break;
                }

                if (!waited)
                {
                    waited = true;
                    _log("[harness] no local player - waiting for a respawn");
                }

                yield return null;
            }

            _log($"[harness] no player after {limitSeconds:0}s; remaining cases will skip");
        }

        /// <summary>
        /// Drives one case, catching exceptions around each step.
        ///
        /// C# forbids yielding inside a try/catch, so the enumerator is stepped by hand -
        /// which is the only way to keep one bad case from ending the whole run.
        /// </summary>
        private IEnumerator RunIsolated(HarnessCase testCase, HarnessContext context)
        {
            IEnumerator routine;
            try
            {
                routine = testCase.Run(context);
            }
            catch (Exception e)
            {
                context.Error("starting the case", e);
                yield break;
            }

            if (routine == null) yield break;

            var limit = testCase.TimeoutSeconds > 0f ? testCase.TimeoutSeconds : CaseTimeoutSeconds;
            var started = UnityEngine.Time.realtimeSinceStartup;
            while (true)
            {
                if (limit > 0f && UnityEngine.Time.realtimeSinceStartup - started > limit)
                {
                    context.Error("running the case", new TimeoutException(
                        $"exceeded {limit:0}s and was abandoned. Game state then: {GameState.Describe()}"));
                    yield break;
                }

                object current;
                try
                {
                    if (!routine.MoveNext()) yield break;
                    current = routine.Current;
                }
                catch (Exception e)
                {
                    context.Error("running the case", e);
                    yield break;
                }

                yield return current;
            }
        }

        private void Report(HarnessContext context)
        {
            var table = HarnessReport.ToTable(_results, out var passed, out var failed);

            _log("[harness] ===== RESULTS =====");
            foreach (var line in table.Split('\n'))
                if (!string.IsNullOrEmpty(line.Trim())) _log("[harness] " + line.TrimEnd());

            foreach (var line in Telemetry.Describe()) _log("[harness] " + line);
            if (_logWatch != null)
            {
                foreach (var p in _logWatch.Problems) _log($"[harness] bepinex-log {(p.Expected ? "(expected) " : "")}{p.Level} in [{p.Case}] from {p.Source}: {p.Message}");
                if (_logWatch.WarningCount > 0) _log($"[harness] bepinex-log: {_logWatch.WarningCount} warning(s) were also logged (counted, not listed)");
            }

            _log($"[harness] {passed} passed, {failed} failed, {_results.Count} checks total.");

            try
            {
                Directory.CreateDirectory(OutputDirectory);

                var jsonPath = Path.Combine(OutputDirectory, "report.json");
                File.WriteAllText(jsonPath, HarnessReport.ToJson(_results, _name, context.Screenshots, Telemetry, _logWatch));

                var textPath = Path.Combine(OutputDirectory, "report.txt");
                File.WriteAllText(textPath, table + Environment.NewLine + string.Join(Environment.NewLine, new List<string>(Telemetry.Describe()).ToArray()) + Environment.NewLine);

                _log($"[harness] report written to {jsonPath}");
            }
            catch (Exception e)
            {
                // The log already carries the results; a failed write must not lose them.
                _log($"[harness] could not write the report file: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
