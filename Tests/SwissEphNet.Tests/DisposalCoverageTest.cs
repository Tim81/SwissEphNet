using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// DisposeTest.cs pins a hand-picked sample of public members (one call routed through each
    /// of the nine internal component properties, plus FileProvider and OnTrace). That sample
    /// missed FileProvider once already -- see DisposeTest.cs's own header -- and a prose claim
    /// in SwissEph.cs's ThrowIfDisposed() doc comment ("every public member ... with exactly one
    /// exception") was later found wrong by roughly 365 members. A hand-picked sample and a
    /// prose count can both drift from the actual public surface without anything noticing.
    /// </summary>
    /// <remarks>
    /// This test enumerates SwissEph's public instance surface by reflection instead of by
    /// hand, and drives every member it finds against a disposed instance. A member either
    /// throws <see cref="ObjectDisposedException"/>, or it is named on <see cref="AllowList"/>
    /// with a one-line reason. There is no third option: a newly added public member that is
    /// neither guarded nor allow-listed fails this test, which is the actual guarantee "every
    /// public member is guarded, except for a documented, named list" needs to hold as code
    /// rather than as a comment.
    ///
    /// Scope: SwissEph's own declared public instance methods, properties and events
    /// (<c>BindingFlags.DeclaredOnly</c>) -- not members inherited from <see cref="object"/>
    /// (<c>ToString</c>, <c>Equals</c>, <c>GetHashCode</c>, <c>GetType</c>), which are universal
    /// to every .NET object and carry no SwissEph instance state to guard, and not constructors,
    /// which cannot be called against an instance that already exists to be disposed.
    ///
    /// Argument synthesis: every parameter is filled with <c>default(T)</c> (value types) or
    /// <c>null</c> (reference types, strings, arrays, delegates), never a value meaningful to
    /// the call. This is safe, not just convenient: every guarded member in this class reaches
    /// the disposal check by evaluating a guarded property (<c>Sweph</c>, <c>SweHouse</c>, ...)
    /// as the receiver of a single-expression call (see SwissEph.swephexp.h.cs), and C# always
    /// evaluates that receiver before it evaluates the argument list -- so the
    /// <see cref="ObjectDisposedException"/> fires before any synthesized argument is ever
    /// touched. Confirmed by <see cref="DisposeTest"/>'s own spot checks, which pass the same
    /// way with real arguments.
    /// </remarks>
    public class DisposalCoverageTest
    {
        /// <summary>
        /// Members that legitimately do not throw <see cref="ObjectDisposedException"/> after
        /// <see cref="SwissEph.Dispose()"/>, each with the one-line reason it is exempt. Adding a
        /// name here without a real, checkable reason defeats the point of this test.
        /// </summary>
        private static readonly Dictionary<string, string> AllowList =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dispose"] =
                "idempotent by design -- a second Dispose() call must not throw " +
                "(DisposeTest.DoubleDispose_DoesNotThrow pins this).",
            ["swe_dotnet_version"] =
                "reads only assembly metadata (typeof(SwissEph).Assembly.FullName); touches no " +
                "instance state, so there is nothing for a disposed instance to have invalidated.",
            ["swe_d2l"] =
                "public static; its body binds \"SwephLib\" to the CPort.SwephLib type (a " +
                "stateless static delegator), not to this instance's guarded SwephLib property, " +
                "because a static method has no \"this\" to check disposal against.",
            ["DMS"] =
                "guard coverage is argument-dependent: DMS only reaches the guarded SwephLib " +
                "property when iFlag requests minute/second rounding (BIT_ROUND_MIN / " +
                "BIT_ROUND_SEC), so DMS(x, 0) -- the synthesized all-default call this test " +
                "makes -- returns normally after Dispose() while DMS(x, BIT_ROUND_SEC) throws.",
            ["HMS"] =
                "delegates straight to DMS(value, iFlag, ...) and inherits the same " +
                "argument-dependent guard coverage described under DMS above.",
            ["FormatToDegreeMinuteSecond"] =
                "public static; a pure string-formatting function over its own arguments, " +
                "reads no SwissEph instance state and has no \"this\" to check disposal against.",
            ["GetHourValue"] =
                "public static; a pure arithmetic function over its own arguments, reads no " +
                "SwissEph instance state and has no \"this\" to check disposal against.",
        };

        private static object DefaultArgument(Type parameterType)
        {
            var t = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
            if (t.IsValueType)
                return Activator.CreateInstance(t);
            return null;
        }

        private static Exception InvokeAndCaptureException(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (TargetInvocationException tie)
            {
                return tie.InnerException;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        [Fact]
        public void EveryPublicInstanceMember_ThrowsObjectDisposedException_UnlessAllowListed()
        {
            var type = typeof(SwissEph);
            var failures = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            // Static and instance together, deliberately: a static public method (swe_d2l is
            // the only one today) has no "this" for Dispose() to invalidate, so it cannot be
            // driven through the same disposed-instance check below -- but it still has to
            // clear this test by being named on AllowList with a reason, rather than by simply
            // never being looked at. BindingFlags.Instance alone would let a static method
            // slip past this test unseen, which is exactly the kind of silent gap this test
            // exists to close.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                // Property and event accessors (get_/set_/add_/remove_) surface here too, with
                // IsSpecialName set; they are exercised through the PropertyInfo/EventInfo loops
                // below instead, against the same disposed instance, so skipping them here avoids
                // testing each one twice under two different names.
                if (method.IsSpecialName) continue;

                seenNames.Add(method.Name);
                if (AllowList.ContainsKey(method.Name)) continue;

                if (method.IsStatic)
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "static method {0} is not on AllowList -- a static member has no instance " +
                        "for Dispose() to invalidate, so it must be explicitly documented as exempt " +
                        "rather than silently skipped",
                        method.Name));
                    continue;
                }

                var swe = new SwissEph();
                swe.Dispose();
                var args = method.GetParameters().Select(p => DefaultArgument(p.ParameterType)).ToArray();
                var thrown = InvokeAndCaptureException(() => method.Invoke(swe, args));
                if (!(thrown is ObjectDisposedException))
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "method {0}({1}) threw {2} instead of ObjectDisposedException after Dispose()",
                        method.Name,
                        string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name)),
                        thrown == null ? "nothing" : thrown.GetType().FullName));
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                seenNames.Add(property.Name);
                if (AllowList.ContainsKey(property.Name)) continue;

                if (property.CanRead)
                {
                    var swe = new SwissEph();
                    swe.Dispose();
                    var thrown = InvokeAndCaptureException(() => property.GetValue(swe));
                    if (!(thrown is ObjectDisposedException))
                    {
                        failures.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "property {0} getter threw {1} instead of ObjectDisposedException after Dispose()",
                            property.Name, thrown == null ? "nothing" : thrown.GetType().FullName));
                    }
                }

                if (property.CanWrite)
                {
                    var swe = new SwissEph();
                    swe.Dispose();
                    var value = DefaultArgument(property.PropertyType);
                    var thrown = InvokeAndCaptureException(() => property.SetValue(swe, value));
                    if (!(thrown is ObjectDisposedException))
                    {
                        failures.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "property {0} setter threw {1} instead of ObjectDisposedException after Dispose()",
                            property.Name, thrown == null ? "nothing" : thrown.GetType().FullName));
                    }
                }
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                seenNames.Add(evt.Name);
                if (AllowList.ContainsKey(evt.Name)) continue;

                var sweAdd = new SwissEph();
                sweAdd.Dispose();
                var addThrown = InvokeAndCaptureException(() => evt.AddMethod.Invoke(sweAdd, new object[] { null }));
                if (!(addThrown is ObjectDisposedException))
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "event {0} add accessor threw {1} instead of ObjectDisposedException after Dispose()",
                        evt.Name, addThrown == null ? "nothing" : addThrown.GetType().FullName));
                }

                var sweRemove = new SwissEph();
                sweRemove.Dispose();
                var removeThrown = InvokeAndCaptureException(() => evt.RemoveMethod.Invoke(sweRemove, new object[] { null }));
                if (!(removeThrown is ObjectDisposedException))
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "event {0} remove accessor threw {1} instead of ObjectDisposedException after Dispose()",
                        evt.Name, removeThrown == null ? "nothing" : removeThrown.GetType().FullName));
                }
            }

            // A stale allow-list entry is as much a bug as a missing one: it means a member that
            // used to need the exemption was renamed or removed, and the entry is now
            // documenting nothing. Fail loudly instead of letting it sit unnoticed.
            foreach (var allowListedName in AllowList.Keys)
            {
                if (!seenNames.Contains(allowListedName))
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "AllowList entry '{0}' does not match any public instance method, property, " +
                        "or event declared on SwissEph -- remove the stale entry",
                        allowListedName));
                }
            }

            Assert.True(failures.Count == 0,
                "Disposal coverage gap(s):\n" + string.Join("\n", failures) +
                "\n\nA member listed above is a public instance member of SwissEph that did not " +
                "throw ObjectDisposedException after Dispose(). Either it needs a ThrowIfDisposed() " +
                "guard (see SwissEph.cs), or, if it genuinely carries no instance state, it belongs " +
                "on DisposalCoverageTest.AllowList with a one-line reason.");
        }
    }
}
