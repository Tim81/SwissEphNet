using System;
using System.Reflection;

namespace SwissEphNet
{
    /// <summary>
    /// Extensions for <see cref="System.Type"/>
    /// </summary>
    public static class TypeExtensions
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
