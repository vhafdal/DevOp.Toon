#nullable enable
using System.Collections.Generic;
using System.Linq;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

internal static class NativePathExpansion
{
    public static NativeObjectNode ExpandPaths(NativeObjectNode obj, bool strict, HashSet<string>? quotedKeys = null)
    {
        var result = new NativeObjectNode();

        foreach (var kvp in obj)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            bool wasQuoted = quotedKeys != null && quotedKeys.Contains(key);

            if (!wasQuoted && key.Contains(Constants.DOT) && IsExpandable(key))
            {
                var segments = key.Split(Constants.DOT);
                SetNestedValue(result, segments, value, strict);
            }
            else
            {
                SetValue(result, key, value, strict);
            }
        }

        return result;
    }

    private static bool IsExpandable(string key)
    {
        var segments = key.Split(Constants.DOT);
        return segments.All(ValidationShared.IsIdentifierSegment);
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
