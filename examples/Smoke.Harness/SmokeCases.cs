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
            var p = Player.m_localPlayer;
            var start = p.transform.position;
            VirtualInput.Active = true;
            VirtualInput.MoveDir = Vector3.forward;
            yield return ctx.Seconds(1.5f);
            VirtualInput.Clear();
            var moved = Vector3.Distance(start, p.transform.position);
            ctx.Check("walking forward moves the player", moved > 1f, $"{moved:0.0} m");
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

        [Case("smoke:screenshot")]
        public IEnumerator Screenshot(HarnessContext ctx)
        {
            yield return ctx.Screenshot("smoke");
            ctx.Check("a screenshot was captured", string.IsNullOrEmpty(ctx.ScreenshotDirectory) || ctx.Screenshots.Count > 0);
        }
    }
}
