using System;

namespace OdinsHand
{
    /// <summary>
    /// Marks a class whose <see cref="CaseAttribute"/> methods are harness cases. The class needs a public
    /// parameterless constructor; one instance is created per run and shared by its cases.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CasesAttribute : Attribute
    {
        /// <summary>Report category for every case in the class. Defaults to the class name.</summary>
        public string Category { get; set; }
    }

    /// <summary>
    /// Marks a method as a case. The method takes a <see cref="HarnessContext"/> and returns either
    /// <c>void</c> (a check that needs no waiting) or <c>IEnumerator</c> (a coroutine that can wait frames and seconds).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CaseAttribute : Attribute
    {
        /// <summary>Case name for the report and for <c>-Only</c>. Defaults to <c>Class.Method</c>.</summary>
        public string Name { get; }

        /// <summary>Per-case time limit in seconds; 0 = the runner's default. For cases that are slow on purpose.</summary>
        public float TimeoutSeconds { get; set; }

        public CaseAttribute(string name = null) { Name = name; }
    }

    /// <summary>Skips the case (recorded as a skip, never a failure), with the reason shown in the report.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class SkipAttribute : Attribute
    {
        public string Reason { get; }
        public SkipAttribute(string reason = "marked [Skip]") { Reason = reason; }
    }

    /// <summary>If any case in the run carries this, only those cases run. For focusing on one thing while you work.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class OnlyAttribute : Attribute { }

    /// <summary>
    /// By default a case is skipped (not failed) when there is no local player, because almost every case needs one.
    /// Put this on the cases that are valid from the main menu.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class NoPlayerAttribute : Attribute { }

    /// <summary>Runs before every case in its class. Same signature rules as <see cref="CaseAttribute"/>.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BeforeEachAttribute : Attribute { }

    /// <summary>Runs after every case in its class, even when the case failed or threw. Must be synchronous (<c>void</c>).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AfterEachAttribute : Attribute { }
}
