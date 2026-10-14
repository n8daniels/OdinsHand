using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// What a case is given to work with: assertions, waits, and screenshots.
    ///
    /// Assertions record rather than throw. A case that fails one check should keep going
    /// and report the rest - the whole point is to learn as much as possible from one
    /// session, not to stop at the first surprise.
    /// </summary>
    public sealed partial class HarnessContext
    {
        private readonly List<HarnessResult> _results;
        private readonly Action<string> _log;

        internal HarnessContext(List<HarnessResult> results, Action<string> log, string screenshotDirectory)
        {
            _results = results;
            _log = log ?? (_ => { });
            ScreenshotDirectory = screenshotDirectory;
        }

        /// <summary>Case currently running. Set by the runner.</summary>
        public string CurrentCase { get; internal set; }

        public string ScreenshotDirectory { get; }

        /// <summary>Screenshots written this run, in order.</summary>
        public List<string> Screenshots { get; } = new List<string>();

        public void Log(string message) => _log(message);

        // --- assertions -----------------------------------------------------

        /// <summary>Records a pass/fail. Never throws.</summary>
        public bool Check(string what, bool condition, string detail = null)
        {
            _results.Add(new HarnessResult
            {
                Case = CurrentCase,
                What = what,
                Outcome = condition ? HarnessOutcome.Pass : HarnessOutcome.Fail,
                Detail = detail
            });
            return condition;
        }

        public bool CheckNotNull(string what, object value) =>
            Check(what, value != null, value == null ? "was null" : null);

        /// <summary>Float comparison with a tolerance, reporting both values when it fails.</summary>
        public bool CheckApprox(string what, float actual, float expected, float tolerance = 0.01f) =>
            Check(what, Mathf.Abs(actual - expected) <= tolerance,
                Mathf.Abs(actual - expected) <= tolerance ? null : $"expected ~{expected}, got {actual}");

        public bool CheckEqual<T>(string what, T actual, T expected) =>
            Check(what, Equals(actual, expected),
                Equals(actual, expected) ? null : $"expected '{expected}', got '{actual}'");

        /// <summary>Records that something could not be checked, which is not a failure.</summary>
        public void Skip(string what, string reason)
        {
            _results.Add(new HarnessResult
            {
                Case = CurrentCase,
                What = what,
                Outcome = HarnessOutcome.Skip,
                Detail = reason
            });
        }

        /// <summary>Records an unexpected exception against the current case.</summary>
        public void Error(string what, Exception exception)
        {
            _results.Add(new HarnessResult
            {
                Case = CurrentCase,
                What = what,
                Outcome = HarnessOutcome.Error,
                Detail = $"{exception.GetType().Name}: {exception.Message}"
            });
        }

        // --- waiting --------------------------------------------------------

        /// <summary>
        /// Waits a number of frames. Unity state - renderers, animators, colliders - is
        /// frequently not settled in the same frame it was changed, and a check run too
        /// early reports a failure that is really just impatience.
        /// </summary>
        public IEnumerator Frames(int count = 2)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        public IEnumerator Seconds(float seconds) => WaitSeconds(seconds);

        private static IEnumerator WaitSeconds(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        /// <summary>
        /// Waits until a condition holds or a timeout elapses. Returns whether it held,
        /// so a case can distinguish "took a moment" from "never happened".
        /// </summary>
        public IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, Action<bool> onResult)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                bool ok;
                try { ok = condition(); }
                catch { ok = false; }

                if (ok) { onResult?.Invoke(true); yield break; }
                yield return null;
            }

            onResult?.Invoke(false);
        }

        // --- screenshots ----------------------------------------------------

        /// <summary>
        /// Captures the screen to the report directory.
        ///
        /// This is the one thing the harness cannot judge for itself, and the reason it
        /// bothers: a screenshot can be looked at later, whereas a live session cannot.
        /// </summary>
        /// <summary>
        /// Captures one frame of a named clip, into its own numbered sequence.
        ///
        /// Screenshots prove a form rendered; they cannot show it ARRIVING. A
        /// transformation is a thing that happens over about a second - the puff, the body
        /// changing, the creature settling - and a single frame either catches the middle
        /// of it or misses it entirely.
        ///
        /// Frames land in clips/&lt;clip&gt;/000.png and are assembled outside the game, so
        /// nothing here depends on a video encoder being present.
        /// </summary>
        public IEnumerator ClipFrame(string clip, int index)
        {
            if (string.IsNullOrEmpty(ScreenshotDirectory)) yield break;

            string path = null;
            try
            {
                var dir = Path.Combine(Path.Combine(ScreenshotDirectory, "clips"), Sanitise(clip));
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, index.ToString("000") + ".png");
                ScreenCapture.CaptureScreenshot(path);
            }
            catch (Exception e)
            {
                Error("clip frame " + clip, e);
                yield break;
            }

            // Same contract as Screenshot: the capture completes at end of frame and gives
            // no callback, so the wait is the only way to know the file exists.
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.12f);
        }

        public IEnumerator Screenshot(string name)
        {
            if (string.IsNullOrEmpty(ScreenshotDirectory)) yield break;

            string path = null;
            try
            {
                Directory.CreateDirectory(ScreenshotDirectory);
                path = Path.Combine(ScreenshotDirectory, Sanitise(name) + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Screenshots.Add(path);
            }
            catch (Exception e)
            {
                Error("screenshot " + name, e);
                yield break;
            }

            // CaptureScreenshot completes at end of frame and gives no callback; this is
            // the documented way to be sure the file exists before moving on.
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.4f);

            if (path != null && !File.Exists(path))
                Skip("screenshot " + name, "capture produced no file");
        }

        private static string Sanitise(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }
    }
}
