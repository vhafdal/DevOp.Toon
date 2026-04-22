#nullable enable
using System.Collections.Generic;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Expands eligible dotted root keys in a native object into nested native objects while preserving quoted dotted keys as literals.
/// </summary>
internal static class NativePathExpansion
{
    /// <summary>
    /// Expands unquoted dotted keys such as <c>a.b.c</c> into nested object nodes.
    /// </summary>
    /// <param name="obj">The root object whose properties should be expanded.</param>
    /// <param name="strict">Whether conflicts should throw instead of being overwritten or merged.</param>
    /// <param name="quotedKeys">Root keys that were quoted in source and must remain literal.</param>
    /// <returns>A cloned object graph with path expansion applied.</returns>
    public static NativeObjectNode ExpandPaths(NativeObjectNode obj, bool strict, HashSet<string>? quotedKeys = null)
    {
        var result = new NativeObjectNode();

        foreach (var kvp in obj)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            bool wasQuoted = quotedKeys != null && quotedKeys.Contains(key);

            if (!wasQuoted && key.Contains(Constants.DOT))
            {
                var segments = key.Split(Constants.DOT);
                bool expandable = true;
                foreach (var segment in segments)
                {
                    if (!ValidationShared.IsIdentifierSegment(segment))
                    {
                        expandable = false;
                        break;
                    }
                }
                if (expandable)
                    SetNestedValue(result, segments, value, strict);
                else
                    SetValue(result, key, value, strict);
            }
            else
            {
                SetValue(result, key, value, strict);
            }
        }

        return result;
    }

    private static void SetNestedValue(NativeObjectNode target, string[] segments, NativeNode? value, bool strict)
    {
        var current = target;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (current.ContainsKey(segment))
            {
                var existing = current[segment];
                if (existing is NativeObjectNode existingObj)
                {
                    current = existingObj;
                }
                else
                {
                    if (strict)
                    {
                        throw ToonPathExpansionException.TraversalConflict(
                            segment,
                            GetTypeName(existing),
                            string.Join(".", segments),
                            i
                        );
                    }

                    var newObj = new NativeObjectNode();
                    current[segment] = newObj;
                    current = newObj;
                }
            }
            else
            {
                var newObj = new NativeObjectNode();
                current[segment] = newObj;
                current = newObj;
            }
        }

        SetValue(current, segments[segments.Length - 1], value, strict);
    }

    private static void SetValue(NativeObjectNode target, string key, NativeNode? value, bool strict)
    {
        if (target.ContainsKey(key))
        {
            var existing = target[key];

            bool conflict = false;
            if (value is NativeObjectNode && existing is not NativeObjectNode)
                conflict = true;
            else if (value is not NativeObjectNode && existing is NativeObjectNode)
                conflict = true;

            if (conflict)
            {
                if (strict)
                {
                    throw ToonPathExpansionException.AssignmentConflict(
                        key,
                        GetTypeName(value),
                        GetTypeName(existing)
                    );
                }
            }

            if (value is NativeObjectNode valueObj && existing is NativeObjectNode existingObj)
            {
                DeepMerge(existingObj, valueObj, strict);
                return;
            }
        }

        target[key] = CloneNode(value);
    }

    private static void DeepMerge(NativeObjectNode target, NativeObjectNode source, bool strict)
    {
        foreach (var kvp in source)
        {
            SetValue(target, kvp.Key, kvp.Value, strict);
        }
    }

    private static NativeNode? CloneNode(NativeNode? node)
    {
        if (node is null)
            return null;

        if (node is NativePrimitiveNode primitive)
            return new NativePrimitiveNode(primitive.Value);

        if (node is NativeArrayNode array)
        {
            var clone = new NativeArrayNode();
            foreach (var item in array)
            {
                clone.Add(CloneNode(item));
            }

            return clone;
        }

        if (node is NativeObjectNode obj)
        {
            var clone = new NativeObjectNode();
            foreach (var kvp in obj)
            {
                clone.Add(kvp.Key, CloneNode(kvp.Value));
            }

            return clone;
        }

        return null;
    }

    private static string GetTypeName(NativeNode? node)
    {
        if (node is null)
            return "null";
        if (node is NativeObjectNode)
            return "object";
        if (node is NativeArrayNode)
            return "array";
        return "primitive";
    }
}
