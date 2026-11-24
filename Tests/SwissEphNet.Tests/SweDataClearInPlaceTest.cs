using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_data.ClearInPlace, the port's counterpart of C's memset(&amp;swed, 0, sizeof(struct swe_data))
    /// (sweph.c:1257 at e308765d). Guards against a field being forgotten when swe_data grows: every
    /// instance field, public or not, is dirtied through reflection, ClearInPlace is called, and the result
    /// must equal a freshly constructed swe_data field by field while every array and nested object is the
    /// same reference as before (a pointer taken into swed must stay valid, as in the C).
    /// </summary>
    public class SweDataClearInPlaceTest
    {
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type SweDataType()
        {
            var swephType = typeof(SwissEph).GetProperty("Sweph", BindingFlags.NonPublic | BindingFlags.Instance).PropertyType;
            return swephType.GetNestedType("swe_data", BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static FieldInfo[] Fields(Type t) => t.GetFields(AllInstance);

        // Reference types with no parameterless constructor are left as they are; the test names them.
        private static readonly List<string> NotDirtied = new List<string>();

        private static object Dirty(object value, Type type, string path)
        {
            if (type == typeof(bool)) return true;
            if (type == typeof(string)) return "dirty";
            if (type.IsEnum) return Enum.ToObject(type, 1);
            if (type.IsPrimitive) return Convert.ChangeType(7, type, System.Globalization.CultureInfo.InvariantCulture);
            if (type.IsArray)
            {
                var arr = (Array)value;
                var et = type.GetElementType();
                if (arr == null)
                    arr = Array.CreateInstance(et, 2);
                var idx = new int[arr.Rank];
                for (int i = 0; i < arr.Length; i++)
                {
                    int rem = i;
                    for (int d = arr.Rank - 1; d >= 0; d--)
                    {
                        int len = arr.GetLength(d);
                        idx[d] = rem % len;
                        rem /= len;
                    }
                    arr.SetValue(Dirty(arr.GetValue(idx), et, path + "[]"), idx);
                }
                return arr;
            }
            if (type.IsValueType)
            {
                object boxed = value ?? Activator.CreateInstance(type);
                foreach (var f in Fields(type))
                    f.SetValue(boxed, Dirty(f.GetValue(boxed), f.FieldType, path + "." + f.Name));
                return boxed;
            }
            if (value == null)
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    NotDirtied.Add(path);
                    return null;
                }
                value = Activator.CreateInstance(type);
            }
            foreach (var f in Fields(type))
                f.SetValue(value, Dirty(f.GetValue(value), f.FieldType, path + "." + f.Name));
            return value;
        }

        private static void AssertSame(object expected, object actual, string path, int depth)
        {
            Assert.True(depth < 6, path + ": too deep");
            if (expected == null || actual == null)
            {
                Assert.True(expected == null && actual == null,
                    $"{path}: expected {expected ?? "null"}, actual {actual ?? "null"}");
                return;
            }
            var t = expected.GetType();
            Assert.Equal(t, actual.GetType());
            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
            {
                Assert.True(expected.Equals(actual), $"{path}: expected '{expected}', actual '{actual}'");
                return;
            }
            if (t.IsArray)
            {
                var a = (Array)expected;
                var b = (Array)actual;
                Assert.True(a.Length == b.Length && a.Rank == b.Rank, $"{path}: array shape differs");
                var ea = a.GetEnumerator();
                var eb = b.GetEnumerator();
                int i = 0;
                while (ea.MoveNext() && eb.MoveNext())
                    AssertSame(ea.Current, eb.Current, path + "[" + i++ + "]", depth + 1);
                return;
            }
            foreach (var f in Fields(t))
                AssertSame(f.GetValue(expected), f.GetValue(actual), path + "." + f.Name, depth + 1);
        }

        // Collects the arrays and class instances (not strings) reachable from the fields of o.
        private static void Identities(object o, string path, List<(string, object)> into, int depth)
        {
            if (o == null || depth > 4) return;
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return;
            if (!t.IsValueType) into.Add((path, o));
            if (t.IsArray)
            {
                if (t.GetElementType().IsPrimitive) return;
                int i = 0;
                foreach (var e in (Array)o) Identities(e, path + "[" + i++ + "]", into, depth + 1);
                return;
            }
            foreach (var f in Fields(t))
                Identities(f.GetValue(o), path + "." + f.Name, into, depth + 1);
        }

        [Fact]
        public void ClearInPlace_EqualsAFreshSweData_AndKeepsEveryArrayAndNestedObject()
        {
            var type = SweDataType();
            Assert.NotNull(type);
            var clear = type.GetMethod("ClearInPlace", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(clear);

            object fresh = Activator.CreateInstance(type);
            object dirty = Activator.CreateInstance(type);
            NotDirtied.Clear();
            foreach (var f in Fields(type))
                f.SetValue(dirty, Dirty(f.GetValue(dirty), f.FieldType, f.Name));

            // The dirtying must really have changed things, or the comparison below proves nothing.
            Assert.ThrowsAny<Exception>(() => AssertSame(fresh, dirty, "swe_data", 0));

            // Only the arrays and objects a fresh swe_data holds count: dirtying also allocated some that
            // the cleared object correctly no longer has (fixed_stars, the CPointer arrays of dpsi/deps).
            var freshIds = new List<(string, object)>();
            Identities(fresh, "swed", freshIds, 0);
            var wanted = new HashSet<string>();
            foreach (var id in freshIds) wanted.Add(id.Item1);
            Assert.NotEmpty(wanted);

            var before = new List<(string, object)>();
            Identities(dirty, "swed", before, 0);
            before.RemoveAll(x => !wanted.Contains(x.Item1));
            Assert.Equal(wanted.Count, before.Count);

            clear.Invoke(dirty, null);

            AssertSame(fresh, dirty, "swe_data", 0);

            var after = new List<(string, object)>();
            Identities(dirty, "swed", after, 0);
            after.RemoveAll(x => !wanted.Contains(x.Item1));
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Item1, after[i].Item1);
                Assert.True(ReferenceEquals(before[i].Item2, after[i].Item2), before[i].Item1 + " was replaced");
            }

            // Whatever could not be dirtied (no parameterless constructor) is named here, so a new field
            // of such a type shows up as a failure instead of going unguarded.
            var distinct = new SortedSet<string>(NotDirtied, StringComparer.Ordinal);
            Assert.Equal(new[] { "<fidat>k__BackingField[].fptr", "fixfp" }, new List<string>(distinct).ToArray());
        }
    }
}
