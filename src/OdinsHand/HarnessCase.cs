using System.Collections;

namespace OdinsHand
{
    /// <summary>
    /// One scenario the harness runs inside a live game.
    ///
    /// A case is a coroutine so it can wait frames and seconds - most things worth
    /// checking in Unity are not true until a frame or two after you ask for them.
    /// </summary>
    public abstract class HarnessCase
    {
        /// <summary>Short name, used in the report and to run one case by name.</summary>
        public abstract string Name { get; }

        /// <summary>Optional group, so a report can be read per subsystem.</summary>
        public virtual string Category => "general";

        /// <summary>
        /// True when this case can run right now. A case that needs a loaded world, a
        /// specific biome or an item the player does not have should say so rather than
        /// failing - a skipped case is information, a spurious failure is noise.
        /// </summary>
        public virtual bool CanRun(HarnessContext context) => true;

        /// <summary>Reason shown in the report when <see cref="CanRun"/> is false.</summary>
        public virtual string SkipReason => "preconditions not met";

        public abstract IEnumerator Run(HarnessContext context);

        /// <summary>
        /// Undoes anything the case changed about the world, run whether it finished,
        /// failed or threw.
        ///
        /// Restoring state on the last line of Run() only works if the last line is
        /// reached. One case threw halfway through, so the line putting a form's cooldown
        /// back never ran, the zero it had set for testing survived into the rest of the
        /// session, and a later case duly reported a cooldown of 0 as a real failure. The
        /// misleading result cost more than the original error did.
        ///
        /// Cleanup is not a coroutine on purpose: it has to run even when the case's
        /// enumerator has been abandoned mid-step, and that rules out yielding.
        /// </summary>
        public virtual void Cleanup(HarnessContext context) { }
    }
}
