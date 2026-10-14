using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// The harness plugin, ready-made. Derive from it, add the <c>[BepInPlugin]</c> attribute, name your report and
    /// say where the cases come from - that is the whole plugin:
    ///
    /// <code>
    /// [BepInPlugin("com.me.mymod.harness", "MyMod Harness (dev)", "1.0.0")]
    /// [BepInDependency("com.me.mymod")]
    /// public class MyHarness : HarnessHost
    /// {
    ///     protected override string ReportName =&gt; "mymod";
    /// }
    /// </code>
    ///
    /// With nothing else overridden it finds every <see cref="CasesAttribute"/> class in your assembly. It provides the
    /// <c>[Harness]</c> and <c>[AutoStart]</c> config that <c>scripts/Run-Harness.ps1</c> writes, runs the cases when the
    /// test character first spawns, refuses to run against the wrong character or an unintended server, adds a
    /// <c>&lt;reportName&gt;.selftest</c> console command, and quits the game when it is done if asked to.
    ///
    /// Do not define <c>Awake</c> or <c>OnDestroy</c> on the derived class (Unity would hide this class's); use
    /// <see cref="OnHostAwake"/> and <see cref="OnHostDestroy"/>.
    /// </summary>
    public abstract class HarnessHost : BaseUnityPlugin
    {
        /// <summary>The name given to <see cref="HarnessRunner"/>. Reports go to <c>%TEMP%\&lt;ReportName&gt;-harness</c>.</summary>
        protected abstract string ReportName { get; }

        /// <summary>Add cases by hand, in addition to the attribute-declared ones found in your assembly.</summary>
        protected virtual void AddCases(HarnessRunner runner) { }

        /// <summary>Called once after the run, before the game quits. Tidy anything the whole suite touched.</summary>
        protected virtual void OnRunFinished(HarnessRunner runner) { }

        /// <summary>Your own start-up work. Runs after OdinsHand is installed and config is bound.</summary>
        protected virtual void OnHostAwake() { }

        protected virtual void OnHostDestroy() { }

        /// <summary>Set to false to scan for attribute cases yourself instead.</summary>
        protected virtual bool DiscoverCasesAutomatically => true;

        internal static HarnessHost Current { get; private set; }

        protected ConfigEntry<bool> RunOnSpawn { get; private set; }
        protected ConfigEntry<string> OnlyCases { get; private set; }
        protected ConfigEntry<bool> CaptureScreenshots { get; private set; }
        protected ConfigEntry<string> Character { get; private set; }
        protected ConfigEntry<string> World { get; private set; }
        protected ConfigEntry<string> Server { get; private set; }
        protected ConfigEntry<string> ServerPassword { get; private set; }
        protected ConfigEntry<bool> QuitWhenDone { get; private set; }

        private Harmony _harmony;
        private bool _started;
        private bool _running;

        private void Awake()
        {
            try
            {
                Current = this;

                RunOnSpawn = Config.Bind("Harness", "RunOnSpawn", false,
                    "Run the cases the first time a character spawns, then write a report.");
                OnlyCases = Config.Bind("Harness", "OnlyCases", "",
                    "Run only cases whose name contains this text. Empty = all.");
                CaptureScreenshots = Config.Bind("Harness", "CaptureScreenshots", true,
                    "Capture screenshots into the report directory.");
                Character = Config.Bind("AutoStart", "Character", "",
                    "Disposable test character to load from the main menu (created if missing). Empty = stay on the menu.");
                World = Config.Bind("AutoStart", "World", "",
                    "Disposable local test world (created if missing, fixed seed).");
                Server = Config.Bind("AutoStart", "Server", "",
                    "host:port to join instead of loading a world. Cases then run against that server.");
                ServerPassword = Config.Bind("AutoStart", "ServerPassword", "",
                    "Password for that server.");
                QuitWhenDone = Config.Bind("AutoStart", "QuitWhenDone", false,
                    "Quit the game when the run finishes, so a verification is one unattended command.");

                _harmony = Kit.Install(Info.Metadata.GUID);
                _harmony.PatchAll(GetType().Assembly);
                Kit.OnFirstSpawn(() => { if (RunOnSpawn.Value) RunSuite(null); });

                OnHostAwake();

                Logger.LogInfo($"{Info.Metadata.Name} loaded (Odin's Hand). This assembly must not be shipped.");

                if (!string.IsNullOrEmpty(Character.Value))
                    StartCoroutine(AutoStart.Run(Character.Value, World.Value, Logger.LogInfo, Server.Value, ServerPassword.Value));
            }
            catch (Exception e)
            {
                Logger.LogError("harness start-up failed: " + e);
            }
        }

        private void OnDestroy()
        {
            OnHostDestroy();
            _harmony?.UnpatchSelf();
            if (Current == this) Current = null;
        }

        /// <summary>Starts a run, from the console command or the first-spawn trigger. Ignored if one is already in progress.</summary>
        public void RunSuite(string filter)
        {
            if (_running) { Logger.LogInfo("harness already running."); return; }
            if (_started && filter == null) return;
            _started = true;
            _running = true;
            if (string.IsNullOrEmpty(filter) && !string.IsNullOrEmpty(OnlyCases.Value)) filter = OnlyCases.Value;
            StartCoroutine(RunAfterReady(filter));
        }

        private IEnumerator RunAfterReady(string filter)
        {
            yield return Kit.WaitForPlayerReady();

            if (!SafeToRun(out var refusal))
            {
                _running = false;
                Logger.LogWarning("harness refusing to run: " + refusal);
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Info.Metadata.Name + ": " + refusal);
                yield break;
            }

            var runner = new HarnessRunner(ReportName, Logger.LogInfo) { CaptureScreenshots = CaptureScreenshots.Value };
            AddCases(runner);
            if (DiscoverCasesAutomatically) runner.AddRange(AttributeCases.Discover(GetType().Assembly));

            yield return runner.Run(filter);

            try { OnRunFinished(runner); }
            catch (Exception e) { Logger.LogError("OnRunFinished failed: " + e.Message); }
            _running = false;

            if (QuitWhenDone.Value)
            {
                yield return new WaitForSeconds(2f);
                Logger.LogInfo("harness finished; quitting as configured.");
                Application.Quit();
            }
        }

        /// <summary>
        /// Refuses to run anywhere it could damage something that matters. Cases spawn things, change the clock and
        /// rewrite state; that is fine in a throwaway world and not fine in anyone's real one.
        ///
        /// Two rules, neither with an override:
        ///   * Not on a server you did not ask for. Joining a server is allowed only when <c>Server</c> is configured.
        ///   * The configured character, or nothing. A run that merely highlighted a character in the menu without
        ///     loading it once reported ninety-six confident passes about someone's real save.
        /// </summary>
        protected virtual bool SafeToRun(out string reason)
        {
            reason = null;

            if (ZNet.instance == null) { reason = "no network session"; return false; }

            if (!ZNet.instance.IsServer() && string.IsNullOrEmpty(Server.Value))
            {
                reason = "this is a joined server, but no Server was configured for the harness";
                return false;
            }

            var wanted = Character.Value;
            if (string.IsNullOrEmpty(wanted)) return true;

            var loaded = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            var actual = loaded != null ? loaded.GetName() : null;
            if (!string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"loaded character is '{actual}', not the configured '{wanted}'";
                return false;
            }

            return true;
        }
    }

    /// <summary>Registers <c>&lt;reportName&gt;.selftest [filter]</c> once the terminal exists.</summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class SelfTestCommand
    {
        private static bool _registered;

        private static void Postfix()
        {
            if (_registered || HarnessHost.Current == null) return;
            _registered = true;

            var host = HarnessHost.Current;
            var name = host.Info.Metadata.Name;
            var command = host.Info.Metadata.GUID.Split('.').Length > 1 ? host.Info.Metadata.GUID : name;

            // Vanilla's own ConsoleCommand rather than a mod framework's, so the harness never introduces a dependency.
            // Not a cheat command: vanilla asks cheat commands to confirm first, and a prompt is the one thing an
            // unattended run cannot answer.
            new Terminal.ConsoleCommand(command + ".selftest",
                command + ".selftest [filter] - run the in-game harness and write a report.",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (HarnessHost.Current == null || Player.m_localPlayer == null)
                    {
                        args.Context.AddString(name + ": not available (no local player).");
                        return;
                    }
                    args.Context.AddString(name + ": running; results go to the log and a report file.");
                    HarnessHost.Current.RunSuite(args.Length >= 2 ? args[1] : "");
                }),
                isCheat: false);
        }
    }
}
