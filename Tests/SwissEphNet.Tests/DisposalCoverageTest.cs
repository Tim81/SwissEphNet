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
    /// This test enumerates SwissEph's public surface by reflection instead of by hand, and
    /// drives every member it finds against a disposed instance. A member either throws
    /// <see cref="ObjectDisposedException"/>, or it is named on <see cref="AllowList"/> with a
    /// one-line reason. There is no unnamed third way for a member to escape this test: a newly
    /// added public member that is neither guarded nor allow-listed fails it, which is the actual
    /// guarantee "every public member is guarded, except for a documented, named list" needs to
    /// hold as code rather than as a comment.
    ///
    /// There genuinely is a third *category*, though, distinct from "throws" and "named on
    /// AllowList with a per-member reason": <c>const</c> fields. SwissEph declares 353 of them
    /// (the <c>SE_*</c> flag and body constants) -- a compile-time literal is baked into every
    /// caller's IL at build time, so there is no runtime field access left for Dispose() to have
    /// invalidated even in principle, and writing 353 near-identical AllowList reasons would
    /// document nothing a reader couldn't already tell from <c>const</c> itself. The fields loop
    /// below bulk-exempts them by declaration kind. Every *non-const* field -- static or
    /// instance -- still needs its own AllowList entry, the same as a static method, because
    /// nothing about being a field makes an entry's absence any less of a silent gap.
    ///
    /// Scope: SwissEph's own declared public methods, properties, events and fields
    /// (<c>BindingFlags.DeclaredOnly</c>, <c>Static</c> and <c>Instance</c> together) -- not
    /// members inherited from <see cref="object"/> (<c>ToString</c>, <c>Equals</c>,
    /// <c>GetHashCode</c>, <c>GetType</c>), which are universal to every .NET object and carry no
    /// SwissEph instance state to guard, and not constructors, which cannot be called against an
    /// instance that already exists to be disposed.
    ///
    /// Two shapes reflection can enumerate but this sweep cannot meaningfully drive: an indexer
    /// (its accessors need synthesized index arguments this test does not attempt to invent) and
    /// an open generic method (there is no type argument to infer <c>T</c> from). Both fail this
    /// test loudly with a message that says so, rather than being silently skipped or allowed to
    /// surface a raw <c>TargetParameterCountException</c> or <see cref="InvalidOperationException"/>
    /// that says nothing about disposal. Neither shape exists on SwissEph today; a future one
    /// needs hand-written coverage, not an AllowList entry -- these two are not exemptable, only
    /// diagnosable.
    ///
    /// Operators and conversion operators (<c>op_Addition</c>, <c>op_Implicit</c>, ...) are
    /// ordinary static methods under the hood, marked <c>IsSpecialName</c> for the same reason a
    /// property or event accessor is. This sweep tells the two apart by collecting every real
    /// accessor MethodInfo from the property and event loops first: a special-name method not in
    /// that set is an operator, and falls through to the same static-method handling as
    /// everything else in <see cref="AllowList"/> -- named exemption or failure, never silence.
    ///
    /// An explicit interface implementation (<c>void IDisposable.Dispose()</c> instead of
    /// <c>public void Dispose()</c>) compiles to a <em>private</em> method, invisible to
    /// <c>GetMethods(BindingFlags.Public)</c> no matter which flags are combined with it -- that
    /// is what "explicit" means at the CLR level, not a gap this sweep's flags could ever close by
    /// themselves. Closing it needs a different query: walk <see cref="Type.GetInterfaces"/> and,
    /// for each interface, <see cref="Type.GetInterfaceMap"/> to find which of the class's methods
    /// backs each interface method. Where that backing method is public, the method loop above
    /// already exercises it under its own name and this loop skips it to avoid double-testing.
    /// Where it is not public, the only way to reach it at all is to invoke the *interface's own*
    /// <see cref="MethodInfo"/> -- reflection dispatches that correctly to the private
    /// implementation, the same way a cast to the interface would at compile time.
    ///
    /// Argument synthesis: every parameter is filled with <c>default(T)</c> (value types) or
    /// <c>null</c> (reference types, strings, arrays, delegates), never a value meaningful to
    /// the call. This is safe, not just convenient: every guarded member in this class reaches
    /// the disposal check by evaluating a guarded property (<c>Sweph</c>, <c>SweHouse</c>, ...)
    /// as the receiver of a single-expression call (see SwissEph.swephexp.h.cs), and C# always
    /// evaluates that receiver before it evaluates the argument list -- so the
    /// <see cref="ObjectDisposedException"/> fires before any synthesized argument is ever
    /// touched. Confirmed by <see cref="DisposeTest"/>'s own spot checks, which pass the same
    /// way with real arguments. Static members are never invoked this way at all (see below);
    /// synthesized arguments are only ever handed to an instance call.
    ///
    /// Snapshot, independently recounted against <c>typeof(SwissEph)</c> at the time this comment
    /// was last touched -- re-run the count rather than editing these numbers by hand if the
    /// public surface changes: 486 public members declared (117 non-special instance methods, 3
    /// static methods, 4 special-name methods backing the one property and one event, 1 instance
    /// property, 1 instance event, 0 static properties or events, 358 static fields, 0 instance
    /// fields, 1 nested type, 1 constructor). Of those 486, this test's method/property/event
    /// loops together enumerate 122 member-shaped things across 115 distinct names, and 365 of
    /// the 486 do not throw <see cref="ObjectDisposedException"/> after Dispose() (the 119 that do
    /// throw, plus the 1 nested type and the 1 constructor, are not part of either bucket: a type
    /// declaration and a constructor call are not "used against a disposed instance" in any sense
    /// this test can exercise).
    /// </remarks>
    public class DisposalCoverageTest
    {
        /// <summary>
        /// Members that legitimately do not throw <see cref="ObjectDisposedException"/> after
        /// <see cref="SwissEph.Dispose()"/>, each with the one-line reason it is exempt. Adding a
        /// name here without a real, checkable reason defeats the point of this test.
        ///
        /// Keyed by signature, not by bare name: <c>"Method:Name(ParamType,ParamType)"</c> for a
        /// method (an empty parameter list is still <c>()</c>), <c>"Property:Name"</c>,
        /// <c>"Event:Name"</c>, <c>"Field:Name"</c>, or
        /// <c>"Interface:Full.Interface.Name.MethodName(ParamType,ParamType)"</c> for an explicit
        /// interface implementation. A bare method name used to exempt every overload of that
        /// name at once, silently -- a second overload nobody looked at would clear this test
        /// just by sharing a name with one that had already been reasoned about. The
        /// <c>MethodKey</c>/<c>PropertyKey</c>/<c>EventKey</c>/<c>FieldKey</c> helpers below build
        /// these keys the same way the sweep does, so an entry here can never silently mismatch
        /// the member it is meant to exempt.
        ///
        /// Each <c>ParamType</c> is <see cref="Type.ToString"/>, namespace-qualified (e.g.
        /// <c>System.Double</c>, not <c>Double</c>) -- a narrower recurrence of the same bare-name
        /// defeat this class exists to close: <c>Type.Name</c> is the bare, unqualified
        /// name, which stringifies identically for two overloads distinguished only by a generic
        /// argument (<c>List&lt;int&gt;</c> vs <c>List&lt;string&gt;</c> both give <c>List`1</c>,
        /// likewise <c>Nullable&lt;int&gt;</c> vs <c>Nullable&lt;double&gt;</c>), by two
        /// same-named types in different namespaces, or by two same-named types nested under
        /// different parents. None of those collide today, but a bare-<c>Name</c> key would have
        /// let a future one clear this test unseen, exactly like the original bare-method-name
        /// defeat.
        /// </summary>
        private static readonly Dictionary<string, string> AllowList =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Method:Dispose()"] =
                "idempotent by design -- a second Dispose() call must not throw " +
                "(DisposeTest.DoubleDispose_DoesNotThrow pins this).",
            ["Method:swe_dotnet_version()"] =
                "reads only assembly metadata (typeof(SwissEph).Assembly.FullName); touches no " +
                "instance state, so there is nothing for a disposed instance to have invalidated.",
            ["Method:swe_d2l(System.Double)"] =
                "public static; its body binds \"SwephLib\" to the CPort.SwephLib type (a " +
                "stateless static delegator), not to this instance's guarded SwephLib property, " +
                "because a static method has no \"this\" to check disposal against.",
            ["Method:DMS(System.Double,System.Int32,System.Boolean)"] =
                "guard coverage is argument-dependent: DMS only reaches the guarded SwephLib " +
                "property when iFlag requests minute/second rounding (BIT_ROUND_MIN / " +
                "BIT_ROUND_SEC), so DMS(x, 0) -- the synthesized all-default call this test " +
                "makes -- returns normally after Dispose() while DMS(x, BIT_ROUND_SEC) throws.",
            ["Method:HMS(System.Double,System.Int32,System.Boolean)"] =
                "delegates straight to DMS(value, iFlag, ...) and inherits the same " +
                "argument-dependent guard coverage described under DMS above.",
            ["Method:FormatToDegreeMinuteSecond(System.Double,System.String)"] =
                "public static; a pure string-formatting function over its own arguments, " +
                "reads no SwissEph instance state and has no \"this\" to check disposal against.",
            ["Method:GetHourValue(System.Int32,System.Int32,System.Int32)"] =
                "public static; a pure arithmetic function over its own arguments, reads no " +
                "SwissEph instance state and has no \"this\" to check disposal against.",
            ["Field:DefaultEncoding"] =
                "public static, non-const; the process-wide default read once into a new " +
                "instance's own state at construction time, not this instance's own state -- " +
                "disposing one instance leaves every other instance's already-copied encoding, " +
                "and this field itself, untouched.",
            ["Field:DefaultFileProvider"] =
                "public static, non-const; same reasoning as DefaultEncoding above -- read once " +
                "into FileProvider at construction time, so it carries no per-instance state of " +
                "its own for Dispose() to invalidate.",
            ["Field:PATH_SEPARATOR"] =
                "public static, non-const; a process-wide path-separator array used while " +
                "searching the ephemeris path, not any one instance's state.",
            ["Field:DIR_GLUE"] =
                "public static, non-const; a process-wide directory-glue character used while " +
                "building file paths, not any one instance's state.",
            ["Field:SIMULATE_VICTORVB"] =
                "public static readonly; a process-wide compatibility flag, not any one " +
                "instance's state.",
        };

        private static string MethodKey(MethodInfo method)
        {
            // ParameterType.ToString(), not ParameterType.Name: Name is the bare, unqualified
            // type name ("List`1", "Nullable`1"), which two overloads distinguished only by a
            // generic argument -- List<int> vs List<string>, Nullable<int> vs Nullable<double>,
            // or two same-named types in different namespaces or under different nesting parents
            // -- stringify identically under. That collapses a signature-keyed exemption back
            // into the exact bare-name defeat this class's own remarks describe fixing: a second
            // overload nobody looked at clears this test by sharing a key with one that had
            // already been reasoned about. ToString() is namespace-qualified and never null (
            // unlike FullName, which is null for an open generic type parameter), so it tells
            // every overload this sweep can encounter apart.
            return "Method:" + method.Name + "(" +
                string.Join(",", method.GetParameters().Select(p => p.ParameterType.ToString())) + ")";
        }

        private static string PropertyKey(PropertyInfo property)
        {
            return "Property:" + property.Name;
        }

        private static string EventKey(EventInfo evt)
        {
            return "Event:" + evt.Name;
        }

        private static string FieldKey(FieldInfo field)
        {
            return "Field:" + field.Name;
        }

        private static string InterfaceMethodKey(Type iface, MethodInfo interfaceMethod)
        {
            // Same ToString()-not-Name reasoning as MethodKey above.
            return "Interface:" + iface.FullName + "." + interfaceMethod.Name + "(" +
                string.Join(",", interfaceMethod.GetParameters().Select(p => p.ParameterType.ToString())) + ")";
        }

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
            var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

            const BindingFlags declaredPublic =
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            // Collected up front so the method loop below can tell a property/event accessor
            // apart from an operator: both are IsSpecialName, but only the accessor is already
            // exercised by the property/event loops that follow.
            var accessorMethods = new HashSet<MethodInfo>();
            foreach (var p in type.GetProperties(declaredPublic))
            {
                if (p.GetMethod != null) accessorMethods.Add(p.GetMethod);
                if (p.SetMethod != null) accessorMethods.Add(p.SetMethod);
            }
            foreach (var e in type.GetEvents(declaredPublic))
            {
                if (e.AddMethod != null) accessorMethods.Add(e.AddMethod);
                if (e.RemoveMethod != null) accessorMethods.Add(e.RemoveMethod);
            }

            // Static and instance together, deliberately: a static public method (or operator --
            // see the class remarks) has no "this" for Dispose() to invalidate, so it cannot be
            // driven through the same disposed-instance check below -- but it still has to
            // clear this test by being named on AllowList with a reason, rather than by simply
            // never being looked at. BindingFlags.Instance alone would let a static method
            // slip past this test unseen, which is exactly the kind of silent gap this test
            // exists to close.
            foreach (var method in type.GetMethods(declaredPublic))
            {
                if (method.IsSpecialName)
                {
                    // Property and event accessors (get_/set_/add_/remove_) surface here too;
                    // they are exercised through the PropertyInfo/EventInfo loops below instead,
                    // against the same disposed instance, so skipping them here avoids testing
                    // each one twice under two different names. Anything IsSpecialName that is
                    // not one of those collected accessors is an operator or conversion operator
                    // (op_Addition, op_Implicit, ...) -- it falls through to the ordinary
                    // handling below instead of being silently skipped, because C# requires every
                    // operator to be static, so it lands in the static-method branch just like
                    // any other static method.
                    if (accessorMethods.Contains(method)) continue;
                }

                if (method.IsGenericMethodDefinition)
                {
                    // MethodInfo.Invoke on an open generic method throws InvalidOperationException
                    // regardless of disposal state -- that exception says "you called this wrong",
                    // not "this member ignores Dispose()". This sweep does not infer a type
                    // argument to close the generic method with, so it fails loudly with a message
                    // that says so, rather than letting the misleading InvalidOperationException
                    // stand in as if it meant something about disposal.
                    seenSignatures.Add(MethodKey(method));
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "method {0} is a generic method definition -- this sweep does not " +
                        "synthesize type arguments and cannot invoke an open generic method; " +
                        "give it hand-written disposal coverage instead of relying on this sweep",
                        method.Name));
                    continue;
                }

                var key = MethodKey(method);
                seenSignatures.Add(key);
                if (AllowList.ContainsKey(key)) continue;

                if (method.IsStatic)
                {
                    var kind = method.IsSpecialName ? "operator" : "static method";
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} is not on AllowList -- a static member has no instance " +
                        "for Dispose() to invalidate, so it must be explicitly documented as exempt " +
                        "rather than silently skipped",
                        kind, method.Name));
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

            foreach (var property in type.GetProperties(declaredPublic))
            {
                var key = PropertyKey(property);
                seenSignatures.Add(key);

                if (property.GetIndexParameters().Length > 0)
                {
                    // An indexer's accessors need synthesized index arguments this test does not
                    // attempt to invent; calling GetValue()/SetValue() with none throws
                    // TargetParameterCountException, which says "wrong argument count", not
                    // anything about disposal. Fail loudly with a message that says what is
                    // actually true instead of letting that exception stand in for it.
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "property {0} is an indexer -- this sweep does not synthesize index " +
                        "arguments and cannot drive an indexer through Dispose(); give it " +
                        "hand-written disposal coverage instead of relying on this sweep",
                        property.Name));
                    continue;
                }

                if (AllowList.ContainsKey(key)) continue;

                var accessor = property.GetMethod ?? property.SetMethod;
                if (accessor != null && accessor.IsStatic)
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "static property {0} is not on AllowList -- a static member has no " +
                        "instance for Dispose() to invalidate, so it must be explicitly " +
                        "documented as exempt rather than silently skipped",
                        property.Name));
                    continue;
                }

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

            foreach (var evt in type.GetEvents(declaredPublic))
            {
                var key = EventKey(evt);
                seenSignatures.Add(key);
                if (AllowList.ContainsKey(key)) continue;

                var accessor = evt.AddMethod ?? evt.RemoveMethod;
                if (accessor != null && accessor.IsStatic)
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "static event {0} is not on AllowList -- a static member has no " +
                        "instance for Dispose() to invalidate, so it must be explicitly " +
                        "documented as exempt rather than silently skipped",
                        evt.Name));
                    continue;
                }

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

            // Fields have no accessor method of their own, so nothing can ever guard one with a
            // ThrowIfDisposed() call the way a property or method body does -- a field either
            // needs no guard (it carries no per-instance state) or it is a real disposal gap that
            // needs converting into a guarded property, never something this sweep can fix by
            // invoking harder. const fields are bulk-exempt below by declaration kind (see the
            // class remarks for why); every other field, static or instance, needs its own
            // AllowList entry the same as a static method.
            foreach (var field in type.GetFields(declaredPublic))
            {
                var key = FieldKey(field);
                seenSignatures.Add(key);

                if (field.IsLiteral) continue;

                if (AllowList.ContainsKey(key)) continue;

                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} field {1} is not on AllowList -- a field has no accessor method for " +
                    "Dispose() to guard, so it must be explicitly documented as exempt rather " +
                    "than silently skipped",
                    field.IsStatic ? "static" : "instance", field.Name));
            }

            // An explicit interface implementation (void IDisposable.Dispose() instead of public
            // void Dispose()) compiles to a private method that GetMethods(Public) can never see,
            // no matter which flags accompany it -- the method loop above cannot reach it under
            // any name. GetInterfaceMap finds which method backs each interface member; where
            // that method is already public, the method loop above already exercises it under its
            // own name and this loop skips it to avoid testing it twice. Where it is not public,
            // invoking the *interface's* MethodInfo (not the class's) is the only way to reach it
            // through reflection at all -- the same dispatch a compile-time cast would perform.
            foreach (var iface in type.GetInterfaces())
            {
                var map = type.GetInterfaceMap(iface);
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    var targetMethod = map.TargetMethods[i];
                    if (targetMethod.IsPublic) continue;

                    var interfaceMethod = map.InterfaceMethods[i];
                    var key = InterfaceMethodKey(iface, interfaceMethod);
                    seenSignatures.Add(key);
                    if (AllowList.ContainsKey(key)) continue;

                    var swe = new SwissEph();
                    swe.Dispose();
                    var args = interfaceMethod.GetParameters().Select(p => DefaultArgument(p.ParameterType)).ToArray();
                    var thrown = InvokeAndCaptureException(() => interfaceMethod.Invoke(swe, args));
                    if (!(thrown is ObjectDisposedException))
                    {
                        failures.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "explicit interface implementation {0}.{1} threw {2} instead of " +
                            "ObjectDisposedException after Dispose()",
                            iface.FullName, interfaceMethod.Name,
                            thrown == null ? "nothing" : thrown.GetType().FullName));
                    }
                }
            }

            // A stale allow-list entry is as much a bug as a missing one: it means a member that
            // used to need the exemption was renamed or removed, and the entry is now
            // documenting nothing. Fail loudly instead of letting it sit unnoticed.
            foreach (var allowListedKey in AllowList.Keys)
            {
                if (!seenSignatures.Contains(allowListedKey))
                {
                    failures.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "AllowList entry '{0}' does not match any public method, property, " +
                        "event, or field declared on SwissEph -- remove the stale entry",
                        allowListedKey));
                }
            }

            Assert.True(failures.Count == 0,
                "Disposal coverage gap(s):\n" + string.Join("\n", failures) +
                "\n\nA member listed above is a public member of SwissEph that did not " +
                "throw ObjectDisposedException after Dispose(). Either it needs a ThrowIfDisposed() " +
                "guard (see SwissEph.cs), or, if it genuinely carries no instance state, it belongs " +
                "on DisposalCoverageTest.AllowList with a one-line reason.");
        }
    }
}
