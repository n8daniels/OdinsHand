using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OdinsHand
{
    /// <summary>Finds <see cref="CasesAttribute"/> classes and turns their <see cref="CaseAttribute"/> methods into cases.</summary>
    public static class AttributeCases
    {
        /// <summary>Discovers every attribute-declared case in <paramref name="assembly"/>.</summary>
        public static IEnumerable<HarnessCase> Discover(Assembly assembly)
        {
            var found = new List<HarnessCase>();
            foreach (var type in SafeTypes(assembly))
            {
                var classAttr = type.GetCustomAttribute<CasesAttribute>();
                if (classAttr == null || type.IsAbstract) continue;

                object instance;
                try { instance = Activator.CreateInstance(type); }
                catch (Exception e)
                {
                    found.Add(new BrokenCase(type.Name, "could not construct " + type.FullName + ": " + e.Message));
                    continue;
                }

                var category = classAttr.Category ?? type.Name;
                var all = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .OrderBy(m => m.MetadataToken).ToArray();   // declaration order, so a suite reads top to bottom
                var before = all.Where(m => m.GetCustomAttribute<BeforeEachAttribute>() != null).ToList();
                var after = all.Where(m => m.GetCustomAttribute<AfterEachAttribute>() != null).ToList();

                foreach (var method in all)
                {
                    var caseAttr = method.GetCustomAttribute<CaseAttribute>();
                    if (caseAttr == null) continue;

                    var problem = Validate(method);
                    if (problem != null)
                    {
                        found.Add(new BrokenCase(type.Name + "." + method.Name, problem));
                        continue;
                    }

                    var skip = method.GetCustomAttribute<SkipAttribute>() ?? type.GetCustomAttribute<SkipAttribute>();
                    var only = method.GetCustomAttribute<OnlyAttribute>() != null || type.GetCustomAttribute<OnlyAttribute>() != null;
                    var noPlayer = method.GetCustomAttribute<NoPlayerAttribute>() != null || type.GetCustomAttribute<NoPlayerAttribute>() != null;

                    found.Add(new MethodCase(
                        caseAttr.Name ?? type.Name + "." + method.Name, category, instance, method,
                        before, after, skip?.Reason, only, noPlayer));
                }
            }

            // [Only] anywhere means "just these".
            if (found.OfType<MethodCase>().Any(c => c.IsOnly))
                return found.Where(c => !(c is MethodCase m) || m.IsOnly).ToList();
            return found;
        }

        private static string Validate(MethodInfo method)
        {
            var ps = method.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(HarnessContext))
                return "a [Case] method must take exactly one HarnessContext parameter";
            if (method.ReturnType != typeof(void) && method.ReturnType != typeof(IEnumerator))
                return "a [Case] method must return void or IEnumerator";
            return null;
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        private static IEnumerator Invoke(object target, MethodInfo method, HarnessContext ctx)
        {
            object result;
            try { result = method.Invoke(target, new object[] { ctx }); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            if (result is IEnumerator routine)
                while (routine.MoveNext()) yield return routine.Current;
        }

        private sealed class MethodCase : HarnessCase
        {
            private readonly object _target;
            private readonly MethodInfo _method;
            private readonly List<MethodInfo> _before, _after;
            private readonly string _skip;
            private readonly bool _noPlayer;

            public MethodCase(string name, string category, object target, MethodInfo method,
                List<MethodInfo> before, List<MethodInfo> after, string skip, bool only, bool noPlayer)
            {
                Name = name; Category = category; _target = target; _method = method;
                _before = before; _after = after; _skip = skip; IsOnly = only; _noPlayer = noPlayer;
            }

            public override string Name { get; }
            public override string Category { get; }
            public bool IsOnly { get; }

            public override bool CanRun(HarnessContext context) =>
                _skip == null && (_noPlayer || Player.m_localPlayer != null);

            public override string SkipReason => _skip ?? "needs a local player (add [NoPlayer] if this case is valid from the menu)";

            public override IEnumerator Run(HarnessContext context)
            {
                // Flattened by hand: the runner steps this enumerator inside a try/catch, and an exception thrown
                // from a nested enumerator handed to Unity would escape it and kill the whole run.
                foreach (var b in _before)
                {
                    var hook = Invoke(_target, b, context);
                    while (hook.MoveNext()) yield return hook.Current;
                }
                var body = Invoke(_target, _method, context);
                while (body.MoveNext()) yield return body.Current;
            }

            public override void Cleanup(HarnessContext context)
            {
                foreach (var a in _after)
                {
                    try { a.Invoke(_target, new object[] { context }); }
                    catch (Exception e) { context.Error("[AfterEach] " + a.Name, e.InnerException ?? e); }
                }
            }
        }

        private sealed class BrokenCase : HarnessCase
        {
            private readonly string _why;
            public BrokenCase(string name, string why) { Name = name; _why = why; }
            public override string Name { get; }
            public override IEnumerator Run(HarnessContext context)
            {
                context.Check("case is declared correctly", false, _why);
                yield break;
            }
        }
    }
}
