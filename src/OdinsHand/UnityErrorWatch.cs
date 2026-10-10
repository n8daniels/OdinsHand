using System;
using System.Collections.Generic;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Watches Unity's own log for exceptions and errors while a run is in progress.
    ///
    /// This exists because a whole class of failure was invisible to this harness. Every
    /// check looked at what the mod logged through BepInEx, and Unity's exceptions do not
    /// go there - they go to Player.log. A stale plugin was throwing inside Awake and
    /// leaving a broken object that made ZNetScene throw once per frame, 1,276 times in one
    /// session, while the report said 330 passed and 0 failed. Both were true, and only one
    /// was the whole picture.
    ///
    /// So the engine's opinion is now part of the result. Anything the harness itself
    /// causes is worth failing on, and anything it merely witnesses is worth reporting.
    /// </summary>
    public sealed class UnityErrorWatch : IDisposable
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private readonly List<string> _order = new List<string>();
        private bool _attached;

        /// <summary>Distinct errors seen, most frequent first, with their counts.</summary>
        public IEnumerable<string> Summary
        {
            get
            {
                var lines = new List<string>();
                foreach (var key in _order)
                    lines.Add(_counts[key] > 1 ? $"{key}  (x{_counts[key]})" : key);

                lines.Sort((a, b) => Count(b).CompareTo(Count(a)));
                return lines;
            }
        }

        public int TotalCount
        {
            get
            {
                var total = 0;
                foreach (var pair in _counts) total += pair.Value;
                return total;
            }
        }

        public int DistinctCount => _order.Count;

        public void Start()
        {
            if (_attached) return;
            Application.logMessageReceived += OnLog;
            _attached = true;
        }

        public void Dispose()
        {
            if (!_attached) return;
            Application.logMessageReceived -= OnLog;
            _attached = false;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;

            // Collapsed by message and top frame. An exception thrown every frame would
            // otherwise bury the report it is supposed to appear in.
            var frame = FirstFrame(stackTrace);
            var key = string.IsNullOrEmpty(frame) ? condition : condition + "  at " + frame;

            if (_counts.ContainsKey(key))
            {
                _counts[key]++;
                return;
            }

            _counts[key] = 1;
            _order.Add(key);
        }

        private int Count(string line)
        {
            var marker = line.LastIndexOf("  (x", StringComparison.Ordinal);
            if (marker < 0) return 1;

            return int.TryParse(line.Substring(marker + 4).TrimEnd(')'), out var n) ? n : 1;
        }

        private static string FirstFrame(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return null;

            foreach (var line in stackTrace.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                // Strip the IL offset and assembly hash, which differ between runs and
                // would otherwise make the same error look like a new one each time.
                var at = trimmed.IndexOf(" [0x", StringComparison.Ordinal);
                return at > 0 ? trimmed.Substring(0, at) : trimmed;
            }

            return null;
        }
    }
}
