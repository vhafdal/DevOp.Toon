#nullable enable
using System;
using System.Text;

namespace DevOp.Toon
{
    /// <summary>
    /// Exception thrown when path expansion conflicts occur during TOON decoding.
    /// </summary>
    public sealed class ToonPathExpansionException : Exception
    {
        /// <summary>
        /// Gets the key or path segment where the expansion conflict occurred.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the full dotted path being expanded, when available.
        /// </summary>
        public string? FullPath { get; }

        /// <summary>
        /// Gets the node type that was expected at the conflict location.
        /// </summary>
        public string ExpectedType { get; }

        /// <summary>
        /// Gets the node type that was actually present at the conflict location.
        /// </summary>
        public string ActualType { get; }

        /// <summary>
        /// Gets the zero-based path depth where the conflict occurred, when available.
        /// </summary>
        public int? Depth { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToonPathExpansionException"/> class with conflict details.
        /// </summary>
        /// <param name="key">The key or path segment where expansion failed.</param>
        /// <param name="expectedType">The node type required to continue expansion.</param>
        /// <param name="actualType">The node type found at the conflict location.</param>
        /// <param name="fullPath">The full dotted path being expanded, when available.</param>
        /// <param name="depth">The zero-based path depth where the conflict occurred, when available.</param>
        /// <param name="inner">The exception that caused this error, when available.</param>
        public ToonPathExpansionException(
            string key,
            string expectedType,
            string actualType,
            string? fullPath = null,
            int? depth = null,
            Exception? inner = null)
            : base(BuildMessage(key, expectedType, actualType, fullPath, depth), inner)
        {
            Key = key;
            ExpectedType = expectedType;
            ActualType = actualType;
            FullPath = fullPath;
            Depth = depth;
        }

        /// <summary>
        /// Creates an exception for a path traversal conflict where expansion needed an object but found another node type.
        /// </summary>
        /// <param name="segment">The path segment that could not be traversed.</param>
        /// <param name="actualType">The node type found at the traversal location.</param>
        /// <param name="fullPath">The full dotted path being expanded, when available.</param>
        /// <param name="depth">The zero-based path depth where the conflict occurred, when available.</param>
        /// <returns>A path expansion exception describing the traversal conflict.</returns>
        public static ToonPathExpansionException TraversalConflict(
            string segment,
            string actualType,
            string? fullPath = null,
            int? depth = null)
            => new(segment, "object", actualType, fullPath, depth);

        /// <summary>
        /// Creates an exception for an assignment conflict where an expanded key would overwrite an incompatible node.
        /// </summary>
        /// <param name="key">The key being assigned.</param>
        /// <param name="expectedType">The node type required for the assignment.</param>
        /// <param name="actualType">The existing node type at the assignment location.</param>
        /// <param name="fullPath">The full dotted path being expanded, when available.</param>
        /// <param name="depth">The zero-based path depth where the conflict occurred, when available.</param>
        /// <returns>A path expansion exception describing the assignment conflict.</returns>
        public static ToonPathExpansionException AssignmentConflict(
            string key,
            string expectedType,
            string actualType,
            string? fullPath = null,
            int? depth = null)
            => new(key, expectedType, actualType, fullPath, depth);

        private static string BuildMessage(
            string key,
            string expectedType,
            string actualType,
            string? fullPath,
            int? depth)
        {
            var sb = new StringBuilder();
            sb.Append("[PathExpansion] Conflict at '").Append(key).Append("': ");
            sb.Append("expected ").Append(expectedType);
            sb.Append(" but found ").Append(actualType);

            if (!string.IsNullOrEmpty(fullPath))
            {
                sb.Append(" (in path '").Append(fullPath).Append("')");
            }

            if (depth is not null)
            {
                sb.Append(" (depth: ").Append(depth.Value).Append(")");
            }

            return sb.ToString();
        }
    }
}
