#if NETSTANDARD2_0

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Indicates that a constructor initializes all required members for target frameworks that do not provide this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = true)]
    public sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
