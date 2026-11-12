#nullable enable
using System;
using System.Collections.Generic;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Expands eligible dotted keys at every depth of a native node graph into nested native objects while preserving quoted
/// dotted keys as literals.
/// </summary>
internal static class NativePathExpansion
{
    /// <summary>
    /// Expands unquoted dotted keys such as <c>a.b.c</c> into nested object nodes, in objects at any depth and in the
    /// objects inside arrays.
    /// </summary>
    /// <param name="node">The decoded node to expand; it is updated in place where that is possible.</param>
    /// <param name="strict">Whether conflicts should throw instead of being overwritten or merged.</param>
    /// <returns>The expanded node graph: subtrees without expandable keys are kept, objects with them are rebuilt.</returns>
    public static NativeNode? ExpandPaths(NativeNode? node, bool strict)
    {
        return Expand(node, null, new ExpansionState(strict));
    }

    private sealed class ExpansionState
    {
        public ExpansionState(bool strict) => Strict = strict;

        public bool Strict { get; }

        // Split segments per key, null for a key that stays literal: the rows of a table repeat the same keys.
        public Dictionary<string, string[]?>? Segments;

        // The properties of the object being rebuilt; reused because rebuilding never recurses.
        public List<KeyValuePair<string, NativeNode?>>? Buffer;
    }

    private static NativeNode? Expand(NativeNode? node, HashSet<string>? inheritedLiteralKeys, ExpansionState state)
    {
        if (node is NativeArrayNode array)
        {
            var rowLiteralKeys = array.GetRowLiteralKeys();
            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item is NativePrimitiveNode or null)
                    continue;

                var expanded = Expand(item, rowLiteralKeys, state);
                if (!ReferenceEquals(expanded, item))
                    array.SetItem(i, expanded);
            }

            return array;
        }

        if (node is not NativeObjectNode obj)
            return node;

        List<KeyValuePair<string, NativeNode?>>? replaced = null;
        var expands = false;
        var literalKeys = obj.GetLiteralKeys() ?? inheritedLiteralKeys;
        foreach (var kvp in obj)
        {
            expands |= GetSegments(kvp.Key, literalKeys, state) != null;
            if (kvp.Value is NativePrimitiveNode or null)
                continue;

            var expanded = Expand(kvp.Value, null, state);
            if (!ReferenceEquals(expanded, kvp.Value))
                (replaced ??= new List<KeyValuePair<string, NativeNode?>>()).Add(new KeyValuePair<string, NativeNode?>(kvp.Key, expanded));
        }

        if (replaced != null)
        {
            foreach (var kvp in replaced)
                obj[kvp.Key] = kvp.Value;
        }

        if (!expands)
            return obj;

        // Rebuild the object in place from its properties, whose values are already expanded.
        var properties = state.Buffer ??= new List<KeyValuePair<string, NativeNode?>>();
        properties.AddRange(obj);
        obj.Clear();
        try
        {
            foreach (var kvp in properties)
            {
                if (GetSegments(kvp.Key, literalKeys, state) is { } keySegments)
                    SetNestedValue(obj, keySegments, kvp.Value, state.Strict);
                else
                    SetValue(obj, kvp.Key, kvp.Value, state.Strict);
            }
        }
        finally
        {
            properties.Clear();
        }

        return obj;
    }

    // Returns the path segments of an expandable key, or null for a key that stays literal. A key is split and validated
    // once per expansion: the rows of a table repeat the same keys.
    private static string[]? GetSegments(string key, HashSet<string>? literalKeys, ExpansionState state)
    {
        if (key.IndexOf(Constants.DOT) < 0 || literalKeys?.Contains(key) == true)
            return null;

        var cache = state.Segments ??= new Dictionary<string, string[]?>(StringComparer.Ordinal);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var segments = key.Split(Constants.DOT);
        foreach (var segment in segments)
        {
            if (!ValidationShared.IsIdentifierSegment(segment))
            {
                segments = null;
                break;
            }
        }

        cache[key] = segments;
        return segments;
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

        target[key] = value;
    }

    private static void DeepMerge(NativeObjectNode target, NativeObjectNode source, bool strict)
    {
        foreach (var kvp in source)
        {
            SetValue(target, kvp.Key, kvp.Value, strict);
        }
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
