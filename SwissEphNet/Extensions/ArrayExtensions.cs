using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SwissEphNet
{
    /// <summary>
    /// Array extensions.
    /// </summary>
    /// <remarks>
    /// Stays public, unlike <see cref="StringExtensions"/> and <see cref="TypeExtensions"/>:
    /// <c>swe_houses_ex</c>, <c>swe_houses_ex2</c> and <c>swe_cotrans</c> all take a
    /// <see cref="CPointer{T}"/> parameter, so a consumer calling those APIs legitimately
    /// needs a way to construct one from their own array. <see cref="GetPointer{T}"/> is
    /// that constructor.
    /// </remarks>
    public static class ArrayExtensions
    {

        /// <summary>
        /// Make an CPointer from an array
        /// </summary>
        public static CPointer<T> GetPointer<T>(this T[] array, int index = 0) {
            return new CPointer<T>(array, index);
        }

    }
}
