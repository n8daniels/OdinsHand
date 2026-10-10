using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using OdinsHand;
using UnityEngine;

namespace Smoke.Harness
{
    /// <summary>
    /// A complete harness plugin in ~80 lines. The config section names ([AutoStart], [Harness]) are the ones
    /// scripts/Run-Harness.ps1 writes for you, so keep them when you copy this.
    /// </summary>
    [BepInPlugin(Guid, "Smoke Harness (dev)", "0.1.0")]
    public class SmokePlugin : BaseUnityPlugin
    {
        public const string Guid = "odinshand.smoke.harness";

        private ConfigEntry<bool> _runOnSpawn;
        private ConfigEntry<string> _only;
        private ConfigEntry<bool> _screenshots;
        private ConfigEntry<string> _character;
        private ConfigEntry<string> _world;
        private ConfigEntry<string> _server;
        private ConfigEntry<string> _serverPassword;
        private ConfigEntry<bool> _quitWhenDone;

        private Harmony _harmony;

        private void Awake()
        {
            _runOnSpawn = Config.Bind("Harness", "RunOnSpawn", false, "Run the cases the first time a character spawns, then write a report.");
            _only = Config.Bind("Harness", "OnlyCases", "", "Run only cases whose name contains this text. Empty = all.");
            _screenshots = Config.Bind("Harness", "CaptureScreenshots", true, "Capture screenshots into the report directory.");
            _character = Config.Bind("AutoStart", "Character", "", "Disposable test character to load from the main menu (created if missing). Empty = stay on the menu.");
            _world = Config.Bind("AutoStart", "World", "", "Disposable local test world (created if missing, fixed seed).");
            _server = Config.Bind("AutoStart", "Server", "", "host:port to join instead of loading a world.");
            _serverPassword = Config.Bind("AutoStart", "ServerPassword", "", "Password for that server.");
            _quitWhenDone = Config.Bind("AutoStart", "QuitWhenDone", false, "Quit the game when the run finishes, so a verification is one unattended command.");

            _harmony = Kit.Install(Guid);                       // OdinsHand's own patches (virtual input)
            _harmony.PatchAll(typeof(SmokePlugin).Assembly);    // this plugin's patches, if it ever has any

            Kit.OnFirstSpawn(() =>
            {
                if (_runOnSpawn.Value) StartCoroutine(RunAll());
            });

            if (!string.IsNullOrEmpty(_character.Value))
                StartCoroutine(AutoStart.Run(_character.Value, _world.Value, Logger.LogInfo, _server.Value, _serverPassword.Value));
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private IEnumerator RunAll()
        {
            yield return Kit.WaitForPlayerReady();

            // The runner's name decides the report folder: %TEMP%\<name>-harness  (scripts/Run-Harness.ps1 reads it from odinshand.json).
            var harness = new HarnessRunner("smoke", Logger.LogInfo) { CaptureScreenshots = _screenshots.Value };
            harness.Add(new PlayerExistsCase())
                   .Add(new VirtualInputCase())
                   .Add(new ScreenshotCase());
            yield return harness.Run(string.IsNullOrEmpty(_only.Value) ? null : _only.Value);

            if (_quitWhenDone.Value)
            {
                yield return new WaitForSeconds(2f);
                Logger.LogInfo("harness finished; quitting as configured.");
                Application.Quit();
            }
        }
    }
}
