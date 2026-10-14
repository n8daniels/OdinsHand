using System.Collections;
using OdinsHand;
using UnityEngine;

namespace MyMod.Harness
{
    /// <summary>
    /// Cases are plain methods taking a HarnessContext and returning void (no waiting) or IEnumerator (waits frames/seconds).
    /// They are skipped, not failed, when there is no local player. Anything done with ctx.Protect / Spawn / Watch is undone
    /// after the case, even if it threw.
    /// </summary>
    [Cases(Category = "mymod")]
    public class StarterCases
    {
        [Case("mymod:world-is-up")]
        public void WorldIsUp(HarnessContext ctx)
        {
            ctx.CheckNotNull("a local player exists", Player.m_localPlayer);
            ctx.Check("vanilla content is loaded", ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 100);
        }

        [Case("mymod:replace-me")]
        public IEnumerator ReplaceMe(HarnessContext ctx)
        {
            // Do something, wait for it to take effect, then check - most things in Unity are not true until a frame later.
            ctx.Protect();                          // god mode for this case, restored afterwards
            yield return ctx.Seconds(0.5f);
            ctx.Check("replace this with a check about your mod", true);
            yield return ctx.Screenshot("mymod_example");
        }
    }
}
