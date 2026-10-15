using System.Collections;
using OdinsHand;
using UnityEngine;

namespace Smoke.Harness
{
    /// <summary>
    /// Cases are plain methods. They take the HarnessContext and return void or IEnumerator. They are skipped, not failed,
    /// when there is no local player. Anything done through ctx.Protect / Spawn / Watch is undone after the case.
    /// </summary>
    [Cases(Category = "smoke")]
    public class SmokeCases
    {
        [Case("smoke:player")]
        public void PlayerExists(HarnessContext ctx)
        {
            var p = Player.m_localPlayer;
            ctx.CheckNotNull("a local player exists", p);
            ctx.Check("the player is alive", !p.IsDead());
            ctx.Check("vanilla content is loaded (ObjectDB has items)", ObjectDB.instance.m_items.Count > 100, $"{ObjectDB.instance.m_items.Count} items");
        }

        [Case("smoke:virtual-input")]
        public IEnumerator VirtualInputMoves(HarnessContext ctx)
        {
            // The test character is persistent, so each run resumes where the last one ended - possibly facing a tree or a
            // rock. Try each direction in turn and pass if synthetic input moves the player in any of them.
            var p = Player.m_localPlayer;
            var best = 0f;
            var how = "";
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var start = p.transform.position;
                VirtualInput.Active = true;
                VirtualInput.MoveDir = dir;
                yield return ctx.Seconds(1.5f);
                VirtualInput.Clear();
                var moved = Vector3.Distance(start, p.transform.position);
                if (moved > best) { best = moved; how = dir.ToString(); }
                if (moved > 1f) break;
            }
            ctx.Check("synthetic movement input moves the player", best > 1f, $"{best:0.0} m ({how})");
        }

        [AfterEach]
        public void StopInput(HarnessContext ctx) => VirtualInput.Clear();

        [Case("smoke:helpers")]
        public IEnumerator ProtectSpawnWatch(HarnessContext ctx)
        {
            ctx.Protect();
            ctx.Check("god mode is on while the case runs", Player.m_localPlayer.InGodMode());

            // Record calls to a real game method and read what they carried. (Tiny getters like GetHealth are inlined by
            // Mono and can be missed; Damage is not tiny.)
            var damage = ctx.Watch(HarmonyLib.AccessTools.Method(typeof(Character), nameof(Character.Damage)));
            var hit = new HitData();
            hit.m_damage.m_blunt = 1f;
            Player.m_localPlayer.Damage(hit);
            yield return ctx.Frames(1);
            ctx.Check("Watch recorded the Damage call", damage.Count == 1, $"{damage.Count} call(s)");
            if (damage.Count == 1)
                ctx.CheckApprox("...and the hit it carried", ((HitData)damage.Calls[0].Args[0]).m_damage.m_blunt, 1f);

            var boar = ctx.Spawn("Boar", ctx.InFrontOfPlayer(8f));
            yield return ctx.Frames(2);
            ctx.Check("a spawned boar exists", boar != null);
        }

        [Case("smoke:helpers-undone")]
        public void HelpersWereUndone(HarnessContext ctx)
        {
            // Runs after the case above: its god mode must have been restored.
            ctx.Check("god mode was restored after the previous case", !Player.m_localPlayer.InGodMode());
        }

        [Case("smoke:telemetry")]
        public void LocationIsRecorded(HarnessContext ctx)
        {
            // The report records where the character spawned and where the run happened, for mods with random spawns.
            var spawn = HarnessTelemetry.LastSpawn;
            ctx.CheckNotNull("the spawn location was recorded", spawn);
            if (spawn != null) ctx.Check("the spawn has a biome and a zone", !string.IsNullOrEmpty(spawn.Biome), spawn.ToString());

            var here = LocationSnapshot.Capture();
            ctx.CheckNotNull("the current location can be captured", here);
            if (spawn != null && here != null)
                ctx.Check("the player is still near where it spawned", Vector3.Distance(spawn.Position, here.Position) < 500f,
                    $"{Vector3.Distance(spawn.Position, here.Position):0} m from the spawn");
        }

        [Case("smoke:biome-distances")]
        public void BiomeDistances(HarnessContext ctx)
        {
            var spawn = HarnessTelemetry.LastSpawn;
            ctx.CheckNotNull("the spawn was recorded", spawn);
            if (spawn == null) return;

            ctx.Check("the spawn lists other biomes", spawn.OtherBiomes.Count > 0, $"{spawn.OtherBiomes.Count} found within {BiomeSearch.MaxMeters:0} m");
            ctx.Check("the spawn's own biome is not listed", !spawn.OtherBiomes.Exists(b => b.Biome == spawn.Biome), spawn.Biome);
            ctx.Log("[telemetry] nearest from spawn: " + string.Join(", ", spawn.OtherBiomes.ConvertAll(b => b.ToString()).ToArray()));

            // Every reported point must really be that biome, and must be as far away as it claims.
            var wg = WorldGenerator.instance;
            var bad = 0;
            foreach (var b in spawn.OtherBiomes)
            {
                var actual = wg.GetBiome(b.X, b.Z).ToString();
                var dist = Vector2.Distance(new Vector2(spawn.X, spawn.Z), new Vector2(b.X, b.Z));
                if (actual != b.Biome || Mathf.Abs(dist - b.Meters) > 1f) { bad++; ctx.Log($"[telemetry] mismatch: {b} -> world says {actual} at {dist:0.0} m"); }
            }
            ctx.Check("every reported point is that biome, at the reported distance", bad == 0, bad == 0 ? null : $"{bad} wrong");

            // Cost: it runs a few times per run, so it should be milliseconds, not a hitch you could feel every frame.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var again = BiomeSearch.FindNearest(spawn.Position);
            watch.Stop();
            ctx.Check("a search is quick enough to run a few times per suite", watch.ElapsedMilliseconds < 500, $"{watch.ElapsedMilliseconds} ms for {again.Count} biome(s)");
            ctx.Check("searching again gives the same answer", again.Count == spawn.OtherBiomes.Count);
        }

        [Case("smoke:bepinex-log")]
        public IEnumerator BepInExLogErrorsAreCaught(HarnessContext ctx)
        {
            // A plugin's Logger.LogError goes to BepInEx's log, not Unity's. The harness listens to that logger and ties each error to
            // the case that was running. A case that provokes one on purpose declares it, so it does not count against the run.
            var source = BepInEx.Logging.Logger.CreateLogSource("OdinsHandSelfTest");
            try
            {
                ctx.ExpectLogError("deliberate self-test error");
                source.LogError("deliberate self-test error: this is provoked by the Smoke example on purpose");
            }
            finally { BepInEx.Logging.Logger.Sources.Remove(source); }
            yield return ctx.Frames(1);

            var seen = ctx.LogProblems.Find(p => p.Message.Contains("deliberate self-test error"));
            ctx.CheckNotNull("a BepInEx logger error was captured", seen);
            if (seen == null) yield break;
            ctx.CheckEqual("...and tied to the case that was running", seen.Case, "smoke:bepinex-log");
            ctx.CheckEqual("...from the plugin that logged it", seen.Source, "OdinsHandSelfTest");
            ctx.Check("...and counted as expected, so it does not count against the run", seen.Expected);
        }

        [Case("smoke:screenshot")]
        public IEnumerator Screenshot(HarnessContext ctx)
        {
            yield return ctx.Screenshot("smoke");
            ctx.Check("a screenshot was captured", string.IsNullOrEmpty(ctx.ScreenshotDirectory) || ctx.Screenshots.Count > 0);
        }
    }
}
