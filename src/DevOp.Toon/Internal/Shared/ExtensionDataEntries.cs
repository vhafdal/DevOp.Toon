#nullable enable
using System;
using System.Collections.Generic;

namespace DevOp.Toon.Internal.Shared;

/// <summary>
/// Collects the extension data entries of one decoded object. Sibling keys follow last-wins; entries merged from a
/// nested object written by an earlier release never replace a sibling entry with the same key, in either order.
/// </summary>
internal sealed class ExtensionDataEntries
{
    private HashSet<string>? nestedKeys;

    /// <summary>Gets the collected entries in first-seen order.</summary>
    internal Dictionary<string, object?> Entries { get; } = new(StringComparer.Ordinal);

    /// <summary>Records a sibling entry, replacing any earlier entry with the same key.</summary>
    internal void Add(string key, object? value)
    {
        Entries[key] = value;
        nestedKeys?.Remove(key);
    }

    /// <summary>Records an entry taken from a nested extension object unless a sibling entry already holds the key.</summary>
    internal void AddNested(string key, object? value)
    {
        if (Entries.ContainsKey(key) && nestedKeys?.Contains(key) != true) return;
        Entries[key] = value;
        (nestedKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(key);
    }
}
