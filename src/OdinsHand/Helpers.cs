using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Helpers that change the world and put it back. Anything registered here is undone after the case, whether it
    /// passed, failed or threw - so a case cannot poison the ones after it by forgetting to tidy up.
    /// </summary>
    public sealed partial class HarnessContext
    {
        private readonly List<Action> _cleanups = new List<Action>();

        /// <summary>Registers an action to run after the current case, in reverse order, even if the case threw.</summary>
        public void OnCleanup(Action undo)
        {
            if (undo != null) _cleanups.Add(undo);
        }

        internal void RunCleanups()
        {
            for (var i = _cleanups.Count - 1; i >= 0; i--)
            {
                try { _cleanups[i](); }
                catch (Exception e) { Error("cleanup", e); }
            }
            _cleanups.Clear();
        }

        /// <summary>
        /// Makes the local player unkillable (and, optionally, unseen by monsters) for the rest of the case, then restores
        /// whatever it was before. Without this a stray boar can end the character mid-suite.
        /// </summary>
        public void Protect(bool ghost = false)
        {
            var player = Player.m_localPlayer;
            if (player == null) { Skip("protect", "no local player"); return; }

            var wasGod = player.InGodMode();
            var wasGhost = player.InGhostMode();
            player.SetGodMode(true);
            if (ghost) player.SetGhostMode(true);

            OnCleanup(() =>
            {
                var p = Player.m_localPlayer;
                if (p == null) return;
                p.SetGodMode(wasGod);
                p.SetGhostMode(wasGhost);
            });
        }

        /// <summary>
        /// Spawns a prefab (by its vanilla name, e.g. <c>"Boar"</c>) and removes it again after the case.
        /// Returns null, and records a skip, when the prefab does not exist.
        /// </summary>
        public GameObject Spawn(string prefab, Vector3 position, Quaternion? rotation = null)
        {
            var scene = ZNetScene.instance;
            var source = scene != null ? scene.GetPrefab(prefab) : null;
            if (source == null) { Skip("spawn " + prefab, "prefab not found or no scene"); return null; }

            var instance = UnityEngine.Object.Instantiate(source, position, rotation ?? Quaternion.identity);
            OnCleanup(() =>
            {
                if (instance == null) return;
                var view = instance.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && ZNetScene.instance != null) ZNetScene.instance.Destroy(instance);
                else UnityEngine.Object.Destroy(instance);
            });
            return instance;
        }

        /// <summary>A position a little way in front of the local player, for placing things to interact with.</summary>
        public Vector3 InFrontOfPlayer(float metres = 3f)
        {
            var t = Player.m_localPlayer != null ? Player.m_localPlayer.transform : null;
            return t == null ? Vector3.zero : t.position + t.forward * metres;
        }

        /// <summary>
        /// Starts recording calls to a game or mod method. Read <see cref="CallRecorder.Count"/> and
        /// <see cref="CallRecorder.Calls"/> afterwards; the patch is removed after the case.
        ///
        /// Useful when the observable effect is hidden: an immortal target never loses health, but you can still record the
        /// <c>Character.Damage</c> calls and read the hit's type, amount and knockback. Mono inlines very small methods
        /// (under about 20 bytes of IL), so a watch on a trivial getter can miss calls.
        /// </summary>
        public CallRecorder Watch(MethodBase method)
        {
            var recorder = CallRecorder.Start(method);
            OnCleanup(recorder.Stop);
            return recorder;
        }
    }

    /// <summary>The calls seen by <see cref="HarnessContext.Watch"/>.</summary>
    public sealed class CallRecorder
    {
        /// <summary>One recorded call: the instance (null for static methods) and the arguments it received.</summary>
        public sealed class Call
        {
            public object Instance;
            public object[] Args;
        }

        private static readonly Dictionary<MethodBase, CallRecorder> Active = new Dictionary<MethodBase, CallRecorder>();
        private static readonly MethodInfo PrefixMethod = AccessTools.Method(typeof(CallRecorder), nameof(Record));

        private readonly MethodBase _method;
        private readonly Harmony _harmony;

        public List<Call> Calls { get; } = new List<Call>();
        public int Count => Calls.Count;

        private CallRecorder(MethodBase method)
        {
            _method = method;
            _harmony = new Harmony("odinshand.watch." + method.DeclaringType?.FullName + "." + method.Name + "." + Guid.NewGuid().ToString("N"));
        }

        internal static CallRecorder Start(MethodBase method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            var recorder = new CallRecorder(method);
            Active[method] = recorder;
            recorder._harmony.Patch(method, prefix: new HarmonyMethod(PrefixMethod));
            return recorder;
        }

        internal void Stop()
        {
            _harmony.UnpatchSelf();
            if (Active.TryGetValue(_method, out var r) && r == this) Active.Remove(_method);
        }

        private static void Record(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (Active.TryGetValue(__originalMethod, out var recorder))
                recorder.Calls.Add(new Call { Instance = __instance, Args = __args });
        }
    }
}
