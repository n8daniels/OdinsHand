using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Small helpers every harness plugin needs. Nothing here knows about any particular mod.
    /// </summary>
    public static class Kit
    {
        /// <summary>
        /// Applies OdinsHand's own patches (currently the virtual-input hook). Call once from your harness plugin's Awake.
        /// Your own harness patches are applied separately with <c>harmony.PatchAll(typeof(YourPlugin).Assembly)</c>,
        /// because Harmony only patches the assembly it is pointed at.
        /// </summary>
        public static Harmony Install(string harmonyId)
        {
            var harmony = new Harmony(harmonyId + ".odinshand");
            harmony.PatchAll(typeof(Kit).Assembly);
            return harmony;
        }

        /// <summary>
        /// Waits until a local player exists and its intro cutscene (played by a brand-new character over a perfectly
        /// valid spawn - the cutscene, and the valkyrie flight, which is InIntro and not InCutscene) is over, then a short settle time. Yield this before running any case.
        ///
        /// It first waits <paramref name="startDelaySeconds"/>: the spawn callback fires before the intro cutscene has begun,
        /// so polling immediately sees "no cutscene" and declares the player ready a moment before the cutscene starts.
        /// </summary>
        public static IEnumerator WaitForPlayerReady(float timeoutSeconds = 60f, float settleSeconds = 2f, float startDelaySeconds = 3f)
        {
            if (startDelaySeconds > 0f) yield return new WaitForSeconds(startDelaySeconds);

            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var player = Player.m_localPlayer;
                if (player != null && !player.InCutscene() && !player.InIntro()) break;
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(settleSeconds);
        }

        /// <summary>Runs <paramref name="onSpawn"/> once, the first time the local player finishes spawning.</summary>
        public static void OnFirstSpawn(Action onSpawn) => SpawnWatcher.Register(onSpawn);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class SpawnWatcher
    {
        private static Action _once;

        internal static void Register(Action a) => _once = a;

        private static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                var a = _once;
                _once = null;
                a?.Invoke();
            }
            catch
            {
                // A dev tool must never be able to disturb a spawn.
            }
        }
    }
}
