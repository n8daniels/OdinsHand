using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace OdinsHand
{
    /// <summary>One error (or fatal) that a plugin or BepInEx wrote to BepInEx's log during a run.</summary>
    public sealed class LoggedProblem
    {
        /// <summary>The case that was running when it was logged.</summary>
        public string Case;
        /// <summary>The log source: usually the plugin's name, or "BepInEx".</summary>
        public string Source;
        public string Level;
        public string Message;
        /// <summary>True when a case declared it (<see cref="HarnessContext.ExpectLogError"/>), so it does not count against the run.</summary>
        public bool Expected;
    }

    /// <summary>
    /// Listens to BepInEx's own logger while a run is in progress.
    ///
    /// <see cref="UnityErrorWatch"/> only sees what goes through Unity's log. A plugin's <c>Logger.LogError(...)</c> goes to BepInEx's
    /// log instead, so a run could pass with real errors sitting in <c>LogOutput.log</c>. This catches them as they happen and records
    /// which case was running. Warnings are only counted. Unity's own messages (source "Unity Log") are skipped here, because
    /// <see cref="UnityErrorWatch"/> already counts them. Errors from before the run starts (a plugin that failed to load) happen too
    /// early to be heard; <c>Run-Harness.ps1</c> scans the whole log for those.
    /// </summary>
    public sealed class BepInExLogWatch : ILogListener
    {
        private const int MaxKept = 200;
        private static readonly string[] Benign = { "Unable to start Unity log writer" };   // a harmless BepInEx start-up message

        private readonly Func<string> _currentCase;
        private readonly object _gate = new object();
        private readonly List<LoggedProblem> _problems = new List<LoggedProblem>();
        private readonly List<string> _expected = new List<string>();
        private bool _started;

        public BepInExLogWatch(Func<string> currentCase) { _currentCase = currentCase; }

        /// <summary>Warnings seen (counted, not kept).</summary>
        public int WarningCount { get; private set; }

        /// <summary>Errors that were dropped because more than the limit were logged.</summary>
        public int Dropped { get; private set; }

        public void Start()
        {
            if (_started) return;
            _started = true;
            BepInEx.Logging.Logger.Listeners.Add(this);
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            BepInEx.Logging.Logger.Listeners.Remove(this);
        }

        /// <summary>Errors containing <paramref name="contains"/> that are logged from now until the case ends are expected.</summary>
        public void Expect(string contains)
        {
            if (string.IsNullOrEmpty(contains)) return;
            lock (_gate) _expected.Add(contains);
        }

        public void ClearExpectations() { lock (_gate) _expected.Clear(); }

        public List<LoggedProblem> Problems { get { lock (_gate) return new List<LoggedProblem>(_problems); } }

        public int UnexpectedCount { get { lock (_gate) { var n = 0; foreach (var p in _problems) if (!p.Expected) n++; return n; } } }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                var level = eventArgs.Level;
                var isError = (level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                var isWarning = (level & LogLevel.Warning) != 0;
                if (!isError && !isWarning) return;

                var source = eventArgs.Source != null ? eventArgs.Source.SourceName : "?";
                if (source == "Unity Log") return;

                var message = eventArgs.Data != null ? eventArgs.Data.ToString() : "";
                foreach (var b in Benign) if (message.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return;

                lock (_gate)
                {
                    if (!isError) { WarningCount++; return; }

                    var expected = false;
                    foreach (var e in _expected) if (message.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0) { expected = true; break; }

                    if (_problems.Count >= MaxKept) { Dropped++; return; }
                    var current = _currentCase != null ? _currentCase() : null;
                    _problems.Add(new LoggedProblem
                    {
                        Case = string.IsNullOrEmpty(current) ? "(outside any case)" : current,
                        Source = source,
                        Level = (level & LogLevel.Fatal) != 0 ? "Fatal" : "Error",
                        Message = message.Length > 500 ? message.Substring(0, 500) + "..." : message,
                        Expected = expected,
                    });
                }
            }
            catch
            {
                // A log listener must never throw into the logger it is listening to.
            }
        }

        public void Dispose() => Stop();
    }
}
