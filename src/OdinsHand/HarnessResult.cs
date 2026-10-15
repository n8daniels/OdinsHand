using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OdinsHand
{
    public enum HarnessOutcome
    {
        Pass,
        Fail,

        /// <summary>Could not be checked. Information, not a failure.</summary>
        Skip,

        /// <summary>Threw unexpectedly.</summary>
        Error,

        /// <summary>Worth reading but not a failure (for example errors in BepInEx's log when failing on them is off).</summary>
        Warn
    }

    public sealed class HarnessResult
    {
        public string Case;
        public string What;
        public HarnessOutcome Outcome;
        public string Detail;
    }

    /// <summary>
    /// Renders results as a human-readable table and as JSON.
    ///
    /// The JSON exists so a build script or an assistant reading the file afterwards can
    /// tell pass from fail without scraping a log - which is the difference between "run
    /// it and paste me the output" and "run it, I will read the report myself".
    ///
    /// Hand-rolled rather than pulling in a serialiser: a mod harness must not add a
    /// dependency that can conflict with whatever else the player has installed.
    /// </summary>
    public static class HarnessReport
    {
        public static string ToTable(IEnumerable<HarnessResult> results, out int passed, out int failed)
        {
            passed = 0;
            failed = 0;

            var builder = new StringBuilder();

            foreach (var r in results)
            {
                if (r.Outcome == HarnessOutcome.Pass) passed++;
                else if (r.Outcome == HarnessOutcome.Fail || r.Outcome == HarnessOutcome.Error) failed++;

                builder.Append(Label(r.Outcome).PadRight(6))
                       .Append((r.Case ?? "-").PadRight(22))
                       .Append(r.What);

                if (!string.IsNullOrEmpty(r.Detail)) builder.Append("  [").Append(r.Detail).Append(']');
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static string ToJson(IEnumerable<HarnessResult> results, string harnessName, IEnumerable<string> screenshots, HarnessTelemetry telemetry = null, BepInExLogWatch logWatch = null)
        {
            var list = new List<HarnessResult>(results);
            int passed = 0, failed = 0, errors = 0, skipped = 0, warnings = 0;
            foreach (var r in list)
            {
                if (r.Outcome == HarnessOutcome.Pass) passed++;
                else if (r.Outcome == HarnessOutcome.Fail) failed++;
                else if (r.Outcome == HarnessOutcome.Skip) skipped++;
                else if (r.Outcome == HarnessOutcome.Warn) warnings++;
                else errors++;
            }

            var builder = new StringBuilder();
            builder.Append("{\"harness\":").Append(Quote(harnessName));

            // Totals first, so a script can decide pass/fail without walking the results.
            builder.Append(",\"passed\":").Append(passed)
                   .Append(",\"failed\":").Append(failed)
                   .Append(",\"errors\":").Append(errors)
                   .Append(",\"skipped\":").Append(skipped)
                   .Append(",\"warnings\":").Append(warnings)
                   .Append(",\"ok\":").Append(failed == 0 && errors == 0 ? "true" : "false");

            builder.Append(",\"results\":[");
            var first = true;
            foreach (var r in list)
            {
                if (!first) builder.Append(',');
                first = false;

                builder.Append("{\"case\":").Append(Quote(r.Case))
                       .Append(",\"what\":").Append(Quote(r.What))
                       .Append(",\"outcome\":").Append(Quote(Label(r.Outcome)))
                       .Append(",\"detail\":").Append(Quote(r.Detail))
                       .Append('}');
            }
            builder.Append(']');

            builder.Append(",\"screenshots\":[");
            first = true;
            foreach (var shot in screenshots)
            {
                if (!first) builder.Append(',');
                first = false;
                builder.Append(Quote(shot));
            }
            builder.Append(']');

            if (telemetry != null)
            {
                builder.Append(",\"telemetry\":");
                telemetry.AppendJson(builder);
            }
            if (logWatch != null)
            {
                builder.Append(",\"bepInExLog\":{\"warnings\":").Append(logWatch.WarningCount).Append(",\"dropped\":").Append(logWatch.Dropped).Append(",\"errors\":[");
                var firstProblem = true;
                foreach (var p in logWatch.Problems)
                {
                    if (!firstProblem) builder.Append(',');
                    firstProblem = false;
                    builder.Append("{\"case\":").Append(Quote(p.Case)).Append(",\"source\":").Append(Quote(p.Source))
                           .Append(",\"level\":").Append(Quote(p.Level)).Append(",\"expected\":").Append(p.Expected ? "true" : "false")
                           .Append(",\"message\":").Append(Quote(p.Message)).Append('}');
                }
                builder.Append("]}");
            }

            builder.Append('}');

            return builder.ToString();
        }

        private static string Label(HarnessOutcome outcome)
        {
            switch (outcome)
            {
                case HarnessOutcome.Pass: return "PASS";
                case HarnessOutcome.Fail: return "FAIL";
                case HarnessOutcome.Skip: return "SKIP";
                case HarnessOutcome.Warn: return "WARN";
                default: return "ERROR";
            }
        }

        internal static string Quote(string value)
        {
            if (value == null) return "null";

            var builder = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }
    }
}
