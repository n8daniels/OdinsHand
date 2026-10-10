using System.Collections;
using OdinsHand;
using UnityEngine;

namespace Smoke.Harness
{
    /// <summary>Cases need a live player; say so in CanRun so a run from the menu skips instead of failing.</summary>
    internal abstract class LiveCase : HarnessCase
    {
        public override bool CanRun(HarnessContext context) => Player.m_localPlayer != null && ObjectDB.instance != null;
        public override string SkipReason => "no live local player";
    }

    internal sealed class PlayerExistsCase : LiveCase
    {
        public override string Name => "smoke:player";

        public override IEnumerator Run(HarnessContext ctx)
        {
            var p = Player.m_localPlayer;
            ctx.CheckNotNull("a local player exists", p);
            ctx.Check("the player is alive", !p.IsDead());
            ctx.Check("vanilla content is loaded (ObjectDB has items)", ObjectDB.instance.m_items.Count > 100, $"{ObjectDB.instance.m_items.Count} items");
            yield break;
        }
    }

    /// <summary>Drives the player with synthetic input and measures that it actually moved.</summary>
    internal sealed class VirtualInputCase : LiveCase
    {
        public override string Name => "smoke:virtual-input";
        public override void Cleanup(HarnessContext context) => VirtualInput.Clear();

        public override IEnumerator Run(HarnessContext ctx)
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
    }

    internal sealed class ScreenshotCase : LiveCase
    {
        public override string Name => "smoke:screenshot";

        public override IEnumerator Run(HarnessContext ctx)
        {
            yield return ctx.Screenshot("smoke");
            ctx.Check("a screenshot was captured", string.IsNullOrEmpty(ctx.ScreenshotDirectory) || ctx.Screenshots.Count > 0);
        }
    }
}
