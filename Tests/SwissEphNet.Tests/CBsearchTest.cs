using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// C.bsearch probes the array in place. It must resolve duplicate keys to the same element
    /// the earlier List&lt;T&gt;.BinarySearch-based implementation did, because the first probed
    /// match wins and fixed star lookups can see repeated keys. The old algorithm is kept here
    /// as a private copy to compare against.
    /// </summary>
    public class CBsearchTest
    {
        struct Item
        {
            public int Key;
            public int Id;
        }

        // Private copy of the previous C.bsearch (ToArray + List.BinarySearch + BComparer).
        class OldComparer : IComparer<Item>
        {
            readonly int _key;
            public OldComparer(int key) { _key = key; }
            public int Compare(Item x, Item y)
            {
                bool xIsDefault = x.Equals(default(Item));
                bool yIsDefault = y.Equals(default(Item));
                if (yIsDefault && !xIsDefault)
                {
                    int c = _key.CompareTo(x.Key);
                    if (c != 0) return -c;
                    return c;
                }
                else if (xIsDefault && !yIsDefault)
                {
                    return _key.CompareTo(y.Key);
                }
                else
                    return -1;
            }
        }

        static int OldBsearch(int key, Item[] array, int n)
        {
            if (n <= 0) return -1;
            var list = new List<Item>(array.Take(n));
            int idx = list.BinarySearch(default(Item), new OldComparer(key));
            return idx >= 0 ? idx : -1;
        }

        static int NewBsearch(int key, Item[] array, int n)
        {
            var p = C.bsearch<int, Item>(key, array, n, (k, item) => k.CompareTo(item.Key));
            if (p.IsNull) return -1;
            return p[0].Id; // Id == index in the array
        }

        [Fact]
        public void Bsearch_MatchesPreviousImplementation_WithDuplicateKeys()
        {
            // Key patterns with runs of duplicates; all keys are non-zero so no element is default(Item).
            var patterns = new Func<int, int>[]
            {
                i => i + 1,           // all distinct
                i => 1,               // all equal
                i => i / 2 + 1,       // pairs
                i => i / 3 + 1,       // triples
                i => (i * i) / 7 + 1, // irregular runs
            };
            foreach (var pattern in patterns)
            {
                for (int length = 0; length <= 40; length++)
                {
                    var array = new Item[length + 3]; // spare tail, as after geometric growth
                    for (int i = 0; i < array.Length; i++)
                        array[i] = new Item { Key = pattern(i), Id = i };
                    for (int n = 0; n <= length; n++)
                    {
                        int maxKey = n > 0 ? pattern(n - 1) : 0;
                        for (int key = 0; key <= maxKey + 2; key++)
                        {
                            Assert.Equal(OldBsearch(key, array, n), NewBsearch(key, array, n));
                        }
                    }
                }
            }
        }

        [Fact]
        public void Bsearch_NullOrEmpty_ReturnsNull()
        {
            Assert.True(C.bsearch<int, Item>(1, new CPointer<Item>(), 0, (k, item) => k.CompareTo(item.Key)).IsNull);
            Assert.True(C.bsearch<int, Item>(1, new CPointer<Item>(), 5, (k, item) => k.CompareTo(item.Key)).IsNull);
            Assert.True(C.bsearch<int, Item>(1, new Item[3], 0, (k, item) => k.CompareTo(item.Key)).IsNull);
        }
    }
}
