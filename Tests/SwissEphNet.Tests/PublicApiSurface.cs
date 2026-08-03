using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit.Sdk;

// CA1307 asks for the StringComparison overload of Replace and IndexOf. It cannot be taken
// here, and would change nothing if it could. This file is compiled into
// Tests/NetStandard20Smoke.Tests as well, which targets net462/net48 and therefore compiles
// against netstandard2.0-era surface, where string.Replace(string, string, StringComparison)
// and string.IndexOf(char, StringComparison) do not exist at all -- taking the analyzer's
// advice would not compile there. Every call the rule flags is on an overload that is already
// ordinal by its documented behaviour: Replace(string, string), Replace(char, char) and
// IndexOf(char) never consult the current culture. The rule is deliberately at warning
// severity repo-wide (see the root .editorconfig) because culture-sensitive comparison has
// caused real defects in this port, so these sites are silenced individually and explained
// rather than left to blend into the build output. The comparisons that decide this test's
// result all go through StringComparer.Ordinal or string.CompareOrdinal, which is the point:
// an approved list that reordered itself under a different current culture would be useless.
#pragma warning disable CA1307

namespace SwissEphNet.ApiApproval
{
    /// <summary>
    /// Renders the shipped library's externally visible API surface as one sorted line per
    /// type and per member, and compares it against a committed approved list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because of a defect that no behavioural test could have caught. Three
    /// extension classes (StringExtensions, ArrayExtensions, TypeExtensions) were public and
    /// sat in namespace SwissEphNet, the namespace every consumer must import to reach
    /// SwissEph. That put <c>Contains(this string, char)</c>, <c>GetPointer</c>,
    /// <c>GetTypeCode</c> and <c>GetAssembly</c> into the lexical scope of every consumer
    /// that wrote <c>using SwissEphNet;</c>. On netstandard2.0 and .NET Framework the BCL has
    /// no instance <c>string.Contains(char)</c>, so a consumer's own unrelated call bound to
    /// this library's extension method instead: measured, <c>((string)null).Contains('x')</c>
    /// threw NullReferenceException on net10.0 and returned false on net48, from the same
    /// source in the same package. It was also a hard compile break (CS0121) for any consumer
    /// who already had their own such helper. StringExtensions and TypeExtensions are
    /// internal now, with InternalsVisibleTo for the test assemblies and SweTest;
    /// ArrayExtensions stays public deliberately, because GetPointer&lt;T&gt; is the only
    /// supported way to build the CPointer&lt;T&gt; that several public APIs take.
    /// </para>
    /// <para>
    /// Making those two internal fixes the members that were already wrong. This file is what
    /// stops the next one from happening: any type or member that becomes externally visible
    /// fails the comparison until someone updates the approved list on purpose, in a diff a
    /// reviewer can read. The distinction between the two cases above is exactly the judgement
    /// the approved list forces someone to make, one line at a time.
    /// </para>
    /// <para>
    /// The approved lists are per target framework, keyed off the library assembly's own
    /// TargetFrameworkAttribute rather than off the test project's TFM, because the two are
    /// not the same thing: NetStandard20Smoke.Tests targets net462/net48 and resolves the
    /// netstandard2.0 asset. The three lists happen to be byte-identical today, and they are
    /// still kept as three files rather than one shared file: the surfaces are permitted to
    /// diverge (a member behind a <c>#if</c>, an inherited BCL interface that exists on one
    /// framework and not another, a compiler that emits delegate BeginInvoke/EndInvoke only
    /// where the runtime supports it), and forcing them equal would either block a legitimate
    /// per-framework difference or hide one. That they are identical is itself worth knowing,
    /// and a plain diff of the three files reports it.
    /// </para>
    /// <para>
    /// Every line is self-contained and fully qualified, so adding one member is a one-line
    /// diff, and the counts are derivable straight from the file: the number of lines with a
    /// given leading tag is the number of types, methods, properties and so on. Nothing in
    /// this repo needs to hand-count the public surface again.
    /// </para>
    /// <para>
    /// Ordering is entirely ordinal and total. Culture-sensitive comparison has produced real
    /// defects in this port (see the C.strcmp fix), and a list that reorders itself under a
    /// different current culture would make every diff unreadable and every failure suspect.
    /// The sort key is (declaring type full name, member-kind rank, whole line), all compared
    /// with StringComparer.Ordinal, and lines are unique within a type, so the ordering is
    /// total and reproducible on any machine under any culture.
    /// </para>
    /// </remarks>
    internal static class PublicApiSurface
    {
        /// <summary>Tag written at the start of a type line.</summary>
        private const string TypeTag = "TYPE  ";

        /// <summary>
        /// Compares the library's current externally visible surface against the approved
        /// list embedded in the calling test assembly, and throws with an explicit
        /// added/removed listing if they differ.
        /// </summary>
        /// <param name="approvedDirectoryForMessage">
        /// Repo-relative directory holding the approved files, used only to tell a reader of
        /// a failure message which file to edit.
        /// </param>
        public static void Verify(string approvedDirectoryForMessage)
        {
            Assembly target = typeof(SwissEph).Assembly;
            string moniker = TargetFrameworkMoniker(target);
            string fileName = target.GetName().Name + "." + moniker + ".approved.txt";

            IList<string> actual = Render(target);

            Assembly host = typeof(PublicApiSurface).Assembly;
            string approvedText = ReadEmbedded(host, fileName);
            if (approvedText == null)
            {
                throw new XunitException(
                    "No approved public-API list is embedded for " + moniker + "." + Environment.NewLine +
                    "Expected an embedded resource named '" + fileName + "'." + Environment.NewLine +
                    "Embedded resources actually present: " +
                    string.Join(", ", host.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal).ToArray()) +
                    Environment.NewLine +
                    WriteReceived(fileName, actual, approvedDirectoryForMessage));
            }

            IList<string> approved = SplitLines(approvedText);
            if (approved.SequenceEqual(actual, StringComparer.Ordinal))
            {
                return;
            }

            var approvedSet = new HashSet<string>(approved, StringComparer.Ordinal);
            var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
            List<string> added = actual.Where(l => !approvedSet.Contains(l)).ToList();
            List<string> removed = approved.Where(l => !actualSet.Contains(l)).ToList();

            var message = new StringBuilder();
            message.Append("The library's externally visible API surface does not match the approved list for ")
                   .Append(moniker).Append('.').Append(Environment.NewLine);
            message.Append("Approved file: ").Append(approvedDirectoryForMessage).Append('/').Append(fileName)
                   .Append(" (").Append(approved.Count.ToString(CultureInfo.InvariantCulture))
                   .Append(" entries); current surface has ")
                   .Append(actual.Count.ToString(CultureInfo.InvariantCulture)).Append(" entries.")
                   .Append(Environment.NewLine);
            message.Append(Environment.NewLine);

            AppendSection(message, "ADDED (now externally visible, not in the approved list)", "+", added);
            AppendSection(message, "REMOVED (in the approved list, no longer externally visible)", "-", removed);

            if (added.Count == 0 && removed.Count == 0)
            {
                message.Append("No entry was added or removed: only the ORDER of the approved file differs from")
                       .Append(Environment.NewLine)
                       .Append("the ordinal ordering this test produces. Replace the approved file with the")
                       .Append(Environment.NewLine)
                       .Append("received file below rather than hand-sorting it.")
                       .Append(Environment.NewLine)
                       .Append(Environment.NewLine);
            }

            message.Append("An ADDED entry is a public-surface expansion. Confirm it is deliberate before")
                   .Append(Environment.NewLine)
                   .Append("approving it: an internal helper that turns public lands in the lexical scope of")
                   .Append(Environment.NewLine)
                   .Append("every consumer that writes 'using SwissEphNet;', which is how the extension-method")
                   .Append(Environment.NewLine)
                   .Append("injection this test exists to prevent reached shipped packages. A REMOVED entry is")
                   .Append(Environment.NewLine)
                   .Append("a breaking change for consumers.")
                   .Append(Environment.NewLine)
                   .Append(Environment.NewLine);
            message.Append(WriteReceived(fileName, actual, approvedDirectoryForMessage));

            throw new XunitException(message.ToString());
        }

        private static void AppendSection(StringBuilder message, string heading, string marker, IList<string> lines)
        {
            message.Append(heading).Append(": ")
                   .Append(lines.Count.ToString(CultureInfo.InvariantCulture))
                   .Append(Environment.NewLine);
            foreach (string line in lines)
            {
                message.Append("  ").Append(marker).Append(' ').Append(line).Append(Environment.NewLine);
            }
            message.Append(Environment.NewLine);
        }

        /// <summary>
        /// Writes the current surface next to the test binaries so a deliberate approval is a
        /// file copy rather than a hand transcription, and returns the instruction text naming it.
        /// </summary>
        private static string WriteReceived(string fileName, IList<string> actual, string approvedDirectoryForMessage)
        {
            string receivedName = fileName.Replace(".approved.txt", ".received.txt");
            string path;
            try
            {
                path = Path.Combine(AppContext.BaseDirectory, receivedName);
                // UTF-8 with BOM and CRLF, matching the repo .editorconfig defaults for
                // this file's extension, so an approved copy of it lands with no churn.
                File.WriteAllText(path, string.Join("\r\n", actual.ToArray()) + "\r\n", new UTF8Encoding(true));
            }
            catch (IOException ex)
            {
                return "Could not write the received surface: " + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return "Could not write the received surface: " + ex.Message;
            }

            return "If, and only if, the change above is intended, copy the received file over the" + Environment.NewLine +
                   "approved one and commit that diff:" + Environment.NewLine +
                   "  received: " + path + Environment.NewLine +
                   "  approved: " + approvedDirectoryForMessage + "/" + fileName;
        }

        private static string ReadEmbedded(Assembly host, string resourceName)
        {
            using (Stream stream = host.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }
                // detectEncodingFromByteOrderMarks strips the BOM the .editorconfig defaults
                // put on the file; SplitLines below absorbs CRLF vs LF, which .gitattributes
                // 'text=auto' leaves dependent on the checking-out machine's core.autocrlf.
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static IList<string> SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n')
                       .Split('\n')
                       .Where(l => l.Length > 0)
                       .ToList();
        }

        // ---------------------------------------------------------------------------------
        // Surface rendering
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Produces the sorted surface listing for an assembly.
        /// </summary>
        public static IList<string> Render(Assembly assembly)
        {
            var entries = new List<Entry>();
            foreach (Type type in GetTypes(assembly))
            {
                if (!IsExternallyVisible(type) || IsCompilerGenerated(type))
                {
                    continue;
                }

                string typeKey = type.FullName ?? type.Name;
                entries.Add(new Entry(typeKey, 0, TypeTag + DescribeType(type)));

                foreach (MemberInfo member in type.GetMembers(DeclaredMembers))
                {
                    string line = DescribeMember(member);
                    if (line != null)
                    {
                        entries.Add(new Entry(typeKey, KindRank(member), line));
                    }
                }
            }

            entries.Sort(Entry.Compare);
            return entries.Select(e => e.Line).ToList();
        }

        private const BindingFlags DeclaredMembers =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        private static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Better a partial listing than no test at all; anything genuinely missing
                // shows up as a REMOVED entry rather than being silently skipped.
                return ex.Types.Where(t => t != null);
            }
        }

        /// <summary>
        /// True when the type can be named from outside the assembly. Deliberately not
        /// Type.IsVisible, which reports false for a protected nested type even though such a
        /// type is reachable by anyone deriving from its declaring type, and so is part of
        /// the surface this test is guarding.
        /// </summary>
        private static bool IsExternallyVisible(Type type)
        {
            if (!type.IsNested)
            {
                return type.IsPublic;
            }
            if (!IsExternallyVisible(type.DeclaringType))
            {
                return false;
            }
            return type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;
        }

        private static bool IsCompilerGenerated(MemberInfo member)
        {
            return member.IsDefined(typeof(CompilerGeneratedAttribute), false)
                || member.Name.IndexOf('<') >= 0;
        }

        private static int KindRank(MemberInfo member)
        {
            if (member is FieldInfo) return 1;
            if (member is ConstructorInfo) return 2;
            if (member is PropertyInfo) return 3;
            if (member is EventInfo) return 4;
            return 5;
        }

        private static string DescribeType(Type type)
        {
            var sb = new StringBuilder();
            sb.Append(TypeAccessibility(type)).Append(' ');

            if (type.IsEnum)
            {
                sb.Append("enum ").Append(TypeName(type))
                  .Append(" : ").Append(TypeName(Enum.GetUnderlyingType(type)));
                return sb.ToString();
            }
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                sb.Append("delegate ").Append(TypeName(type));
                return sb.ToString();
            }
            if (type.IsInterface)
            {
                sb.Append("interface ").Append(TypeName(type));
                AppendBases(sb, type, null);
                return sb.ToString();
            }
            if (type.IsValueType)
            {
                sb.Append("struct ").Append(TypeName(type));
                AppendBases(sb, type, null);
                return sb.ToString();
            }

            if (type.IsAbstract && type.IsSealed) sb.Append("static ");
            else if (type.IsAbstract) sb.Append("abstract ");
            else if (type.IsSealed) sb.Append("sealed ");
            sb.Append("class ").Append(TypeName(type));
            AppendBases(sb, type, type.BaseType == typeof(object) ? null : type.BaseType);
            return sb.ToString();
        }

        /// <summary>
        /// Appends the base type and the full implemented-interface set. The full set, not
        /// just the directly declared one: reflection cannot distinguish the two reliably,
        /// and the full set is what a consumer can actually bind to.
        /// </summary>
        private static void AppendBases(StringBuilder sb, Type type, Type baseType)
        {
            var parts = new List<string>();
            if (baseType != null)
            {
                parts.Add(TypeName(baseType));
            }
            parts.AddRange(type.GetInterfaces()
                               .Where(IsExternallyVisibleInterface)
                               .Select(TypeName)
                               .OrderBy(n => n, StringComparer.Ordinal));
            if (parts.Count > 0)
            {
                sb.Append(" : ").Append(string.Join(", ", parts.ToArray()));
            }
        }

        private static bool IsExternallyVisibleInterface(Type type)
        {
            // An interface from another assembly is always nameable; one from this assembly
            // only counts if it is itself part of the approved surface.
            return type.Assembly != typeof(SwissEph).Assembly || IsExternallyVisible(type);
        }

        private static string DescribeMember(MemberInfo member)
        {
            var field = member as FieldInfo;
            if (field != null)
            {
                return DescribeField(field);
            }

            var ctor = member as ConstructorInfo;
            if (ctor != null)
            {
                if (ctor.IsStatic || !IsExternallyVisible(ctor)) return null;
                return "CTOR  " + Accessibility(ctor) + " " +
                       TypeName(ctor.DeclaringType) + "." + ctor.DeclaringType.Name.Split('`')[0] +
                       Parameters(ctor.GetParameters());
            }

            var property = member as PropertyInfo;
            if (property != null)
            {
                return DescribeProperty(property);
            }

            var evt = member as EventInfo;
            if (evt != null)
            {
                MethodInfo add = evt.GetAddMethod(true);
                if (add == null || !IsExternallyVisible(add)) return null;
                return "EVENT " + Accessibility(add) + (add.IsStatic ? " static" : "") + " " +
                       TypeName(evt.EventHandlerType) + " " +
                       TypeName(evt.DeclaringType) + "." + evt.Name;
            }

            var method = member as MethodInfo;
            if (method != null)
            {
                return DescribeMethod(method);
            }

            // Nested types arrive here as members too; they are enumerated in their own right
            // by Render, which is what gives them their own TYPE line and member listing.
            return null;
        }

        private static string DescribeField(FieldInfo field)
        {
            if (!IsExternallyVisible(field) || IsCompilerGenerated(field)) return null;

            var sb = new StringBuilder("FIELD ");
            sb.Append(Accessibility(field));
            if (field.IsLiteral) sb.Append(" const");
            else if (field.IsStatic && field.IsInitOnly) sb.Append(" static readonly");
            else if (field.IsStatic) sb.Append(" static");
            else if (field.IsInitOnly) sb.Append(" readonly");
            sb.Append(' ').Append(TypeName(field.FieldType)).Append(' ')
              .Append(TypeName(field.DeclaringType)).Append('.').Append(field.Name);
            if (field.IsLiteral)
            {
                // Constant values are part of the contract, not just the shape: a consumer
                // compiled against SE_SUN = 0 keeps the literal 0 baked into their assembly,
                // so a changed value is a silent break that no signature-only list catches.
                sb.Append(" = ").Append(FormatConstant(field.GetRawConstantValue()));
            }
            return sb.ToString();
        }

        private static string DescribeProperty(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod(true);
            MethodInfo setter = property.GetSetMethod(true);
            bool getVisible = getter != null && IsExternallyVisible(getter);
            bool setVisible = setter != null && IsExternallyVisible(setter);
            if (!getVisible && !setVisible) return null;

            MethodInfo representative = getVisible ? getter : setter;
            var sb = new StringBuilder("PROP  ");
            sb.Append(Accessibility(representative));
            if (representative.IsStatic) sb.Append(" static");
            sb.Append(' ').Append(TypeName(property.PropertyType)).Append(' ')
              .Append(TypeName(property.DeclaringType)).Append('.').Append(property.Name);

            ParameterInfo[] indexers = property.GetIndexParameters();
            if (indexers.Length > 0)
            {
                sb.Append(Parameters(indexers).Replace('(', '[').Replace(')', ']'));
            }

            sb.Append(" {");
            if (getVisible) sb.Append(' ').Append(AccessorPrefix(getter, representative)).Append("get;");
            if (setVisible) sb.Append(' ').Append(AccessorPrefix(setter, representative)).Append("set;");
            sb.Append(" }");
            return sb.ToString();
        }

        /// <summary>
        /// Renders an accessor's own accessibility only when it differs from the property's,
        /// the way the C# declaration does.
        /// </summary>
        private static string AccessorPrefix(MethodInfo accessor, MethodInfo representative)
        {
            string own = Accessibility(accessor);
            return own == Accessibility(representative) ? string.Empty : own + " ";
        }

        private static string DescribeMethod(MethodInfo method)
        {
            if (!IsExternallyVisible(method)) return null;
            // Property and event accessors are reported through their property or event.
            // Operators are IsSpecialName too and must NOT be filtered out with them.
            if (method.IsSpecialName && (
                    method.Name.StartsWith("get_", StringComparison.Ordinal) ||
                    method.Name.StartsWith("set_", StringComparison.Ordinal) ||
                    method.Name.StartsWith("add_", StringComparison.Ordinal) ||
                    method.Name.StartsWith("remove_", StringComparison.Ordinal) ||
                    method.Name.StartsWith("raise_", StringComparison.Ordinal)))
            {
                return null;
            }
            if (IsCompilerGenerated(method)) return null;

            var sb = new StringBuilder("METHOD ");
            sb.Append(Accessibility(method));
            if (method.IsStatic) sb.Append(" static");
            else if (method.IsAbstract) sb.Append(" abstract");
            else if (method.IsVirtual && !method.IsFinal) sb.Append(" virtual");
            if (method.IsDefined(typeof(ExtensionAttribute), false)) sb.Append(" extension");
            sb.Append(' ').Append(TypeName(method.ReturnType)).Append(' ')
              .Append(TypeName(method.DeclaringType)).Append('.').Append(method.Name);
            if (method.IsGenericMethodDefinition)
            {
                sb.Append('<')
                  .Append(string.Join(", ", method.GetGenericArguments().Select(a => a.Name).ToArray()))
                  .Append('>');
            }
            sb.Append(Parameters(method.GetParameters()));
            return sb.ToString();
        }

        private static string Parameters(ParameterInfo[] parameters)
        {
            var parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo p = parameters[i];
                var sb = new StringBuilder();
                if (p.IsDefined(typeof(ParamArrayAttribute), false)) sb.Append("params ");
                if (p.ParameterType.IsByRef)
                {
                    sb.Append(p.IsOut ? "out " : "ref ");
                    sb.Append(TypeName(p.ParameterType.GetElementType()));
                }
                else
                {
                    sb.Append(TypeName(p.ParameterType));
                }
                sb.Append(' ').Append(p.Name);
                if (p.IsOptional && (p.Attributes & ParameterAttributes.HasDefault) != 0)
                {
                    sb.Append(" = ").Append(FormatConstant(p.RawDefaultValue));
                }
                parts[i] = sb.ToString();
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        private static bool IsExternallyVisible(MethodBase method)
        {
            return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
        }

        private static bool IsExternallyVisible(FieldInfo field)
        {
            return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
        }

        private static string TypeAccessibility(Type type)
        {
            if (!type.IsNested) return "public";
            if (type.IsNestedFamily) return "protected";
            if (type.IsNestedFamORAssem) return "protected internal";
            return "public";
        }

        private static string Accessibility(MethodBase method)
        {
            if (method.IsFamily) return "protected";
            if (method.IsFamilyOrAssembly) return "protected internal";
            return "public";
        }

        private static string Accessibility(FieldInfo field)
        {
            if (field.IsFamily) return "protected";
            if (field.IsFamilyOrAssembly) return "protected internal";
            return "public";
        }

        /// <summary>
        /// Renders a type the way C# names it, so the list reads as source rather than as
        /// reflection output: generics as Name&lt;T&gt; instead of Name`1, nested types with a
        /// dot instead of a plus, arrays and by-ref forms spelled out.
        /// </summary>
        private static string TypeName(Type type)
        {
            if (type.IsByRef) return "ref " + TypeName(type.GetElementType());
            if (type.IsPointer) return TypeName(type.GetElementType()) + "*";
            if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                return TypeName(type.GetElementType()) + "[" + new string(',', rank - 1) + "]";
            }
            if (type.IsGenericParameter) return type.Name;

            string name;
            if (type.IsNested)
            {
                name = TypeName(type.DeclaringType) + "." + StripArity(type.Name);
            }
            else
            {
                name = string.IsNullOrEmpty(type.Namespace)
                    ? StripArity(type.Name)
                    : type.Namespace + "." + StripArity(type.Name);
            }

            if (type.IsGenericType)
            {
                Type[] args = type.GetGenericArguments();
                // A nested generic reports its declaring types' arguments too; only the ones
                // this type itself introduces belong in its own angle brackets.
                int own = Arity(type.Name);
                if (own > 0)
                {
                    name += "<" + string.Join(", ", args.Skip(args.Length - own).Select(TypeName).ToArray()) + ">";
                }
            }
            return name;
        }

        private static string StripArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        private static int Arity(string name)
        {
            int tick = name.IndexOf('`');
            if (tick < 0) return 0;
            int arity;
            return int.TryParse(name.Substring(tick + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out arity)
                ? arity : 0;
        }

        /// <summary>
        /// Formats a constant or default value invariantly. Every branch is culture-independent
        /// on purpose: a list that renders 1.5 as "1,5" under one culture would fail this test
        /// on a machine whose only difference is its regional settings.
        /// </summary>
        private static string FormatConstant(object value)
        {
            if (value == null) return "null";

            var s = value as string;
            if (s != null)
            {
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                               .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
            }
            if (value is char)
            {
                return "'" + ((int)(char)value).ToString("X4", CultureInfo.InvariantCulture) + "'";
            }
            if (value is bool)
            {
                return ((bool)value) ? "true" : "false";
            }
            if (value is double)
            {
                return ((double)value).ToString("G17", CultureInfo.InvariantCulture);
            }
            if (value is float)
            {
                return ((float)value).ToString("G9", CultureInfo.InvariantCulture);
            }
            if (value is decimal)
            {
                return ((decimal)value).ToString(CultureInfo.InvariantCulture);
            }
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------------------
        // Target framework
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Derives the short moniker of the asset actually loaded, from the assembly itself.
        /// Reading it off the library rather than off the test project's own TFM is what makes
        /// NetStandard20Smoke.Tests pick the netstandard2.0 list while targeting net462/net48.
        /// </summary>
        public static string TargetFrameworkMoniker(Assembly assembly)
        {
            var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            if (attribute == null || string.IsNullOrEmpty(attribute.FrameworkName))
            {
                throw new XunitException(
                    "Assembly " + assembly.GetName().Name + " carries no TargetFrameworkAttribute, so the " +
                    "approved public-API list for it cannot be selected. This test cannot run without it.");
            }

            var framework = new FrameworkName(attribute.FrameworkName);
            Version v = framework.Version;
            switch (framework.Identifier)
            {
                case ".NETStandard":
                    return "netstandard" + v.Major.ToString(CultureInfo.InvariantCulture) + "." +
                           v.Minor.ToString(CultureInfo.InvariantCulture);
                case ".NETCoreApp":
                    return (v.Major >= 5 ? "net" : "netcoreapp") +
                           v.Major.ToString(CultureInfo.InvariantCulture) + "." +
                           v.Minor.ToString(CultureInfo.InvariantCulture);
                case ".NETFramework":
                    return "net" + v.Major.ToString(CultureInfo.InvariantCulture) +
                           v.Minor.ToString(CultureInfo.InvariantCulture) +
                           (v.Build > 0 ? v.Build.ToString(CultureInfo.InvariantCulture) : string.Empty);
                default:
                    throw new XunitException(
                        "Unrecognised target framework '" + attribute.FrameworkName + "' on assembly " +
                        assembly.GetName().Name + ". Add a moniker mapping for it in PublicApiSurface " +
                        "and commit an approved list for it.");
            }
        }

        /// <summary>
        /// One rendered line plus the keys that give the listing its total, ordinal ordering.
        /// </summary>
        private sealed class Entry
        {
            public Entry(string typeKey, int kindRank, string line)
            {
                TypeKey = typeKey;
                KindRank = kindRank;
                Line = line;
            }

            public string TypeKey { get; private set; }
            public int KindRank { get; private set; }
            public string Line { get; private set; }

            public static int Compare(Entry a, Entry b)
            {
                int c = string.CompareOrdinal(a.TypeKey, b.TypeKey);
                if (c != 0) return c;
                c = a.KindRank.CompareTo(b.KindRank);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Line, b.Line);
            }
        }
    }
}

#pragma warning restore CA1307
