#if NETSTANDARD2_0
using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Identifies a compiler feature required by a member for target frameworks that do not provide this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompilerFeatureRequiredAttribute"/> class.
        /// </summary>
        /// <param name="featureName">The compiler feature name required by the annotated member.</param>
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        /// <summary>
        /// Gets the compiler feature name required by the annotated member.
        /// </summary>
        public string FeatureName { get; }
    }
}

#endif
