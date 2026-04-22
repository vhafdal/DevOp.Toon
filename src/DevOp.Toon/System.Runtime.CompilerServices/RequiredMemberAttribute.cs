#if NETSTANDARD2_0

using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marks a type or member as required for target frameworks that do not provide this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class RequiredMemberAttribute : Attribute { }
}

#endif
