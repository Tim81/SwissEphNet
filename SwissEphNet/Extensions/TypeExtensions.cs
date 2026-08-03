using System;
using System.Reflection;

namespace SwissEphNet
{
    /// <summary>
    /// Extensions for <see cref="System.Type"/>.
    /// </summary>
    /// <remarks>
    /// Internal, not public: this is a leftover netstandard1.0 polyfill for members
    /// that already exist as ordinary instance members on every TFM this library now
    /// ships (netstandard2.0, net8.0, net10.0) -- <c>Type.GetTypeCode(t)</c> and
    /// <c>t.Assembly</c> both work directly. Unlike <see cref="ArrayExtensions.GetPointer"/>,
    /// there is no public API (<c>swe_houses_ex</c>, <c>swe_houses_ex2</c>, <c>swe_cotrans</c>)
    /// that requires a consumer to call these, so there is no reason to put them in
    /// scope for every <c>Type</c> a consumer touches via <c>using SwissEphNet;</c>.
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> grants
    /// the test assemblies access via SwissEphNet.csproj.
    /// </remarks>
    internal static class TypeExtensions
    {
        /// <summary>
        /// Returns the <see cref="System.TypeCode"/> of a type
        /// </summary>
        public static TypeCode GetTypeCode(this Type type)
        {
            return Type.GetTypeCode(type);
        }

        /// <summary>
        /// Returns the <see cref="System.Reflection.Assembly"/> of a type
        /// </summary>
        public static Assembly GetAssembly(this Type type)
        {
            return type?.Assembly;
        }

    }

}
