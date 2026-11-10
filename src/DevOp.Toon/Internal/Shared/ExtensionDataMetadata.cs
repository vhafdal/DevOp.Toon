#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using DevOp.Toon.Core;

namespace DevOp.Toon.Internal.Shared;

/// <summary>
/// Locates and validates the <see cref="ToonExtensionDataAttribute"/> property of a CLR type for the 4.x encoders and decoder.
/// </summary>
internal static class ExtensionDataMetadata
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> properties = new();

    /// <summary>
    /// Returns the type's extension data property, or <see langword="null"/> when it declares none.
    /// </summary>
    /// <exception cref="NotSupportedException">The type declares more than one such property, or the property cannot hold extension data.</exception>
    internal static PropertyInfo? Find(Type type) => properties.GetOrAdd(type, static target => Resolve(target));

    internal static bool IsExtension(PropertyInfo property, PropertyInfo extension)
        => property.Name == extension.Name && property.DeclaringType == extension.DeclaringType;

    private static PropertyInfo? Resolve(Type type)
    {
        PropertyInfo? found = null;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!Attribute.IsDefined(property, typeof(ToonExtensionDataAttribute), inherit: true)) continue;
            if (found != null)
                throw new NotSupportedException($"TOON type '{type}' declares more than one [ToonExtensionData] property ('{found.Name}' and '{property.Name}').");
            if (property.GetIndexParameters().Length != 0)
                throw new NotSupportedException($"TOON [ToonExtensionData] cannot be applied to the indexer of '{type}'.");
            if (property.PropertyType != typeof(IDictionary<string, object?>) && property.PropertyType != typeof(Dictionary<string, object?>))
                throw new NotSupportedException($"TOON [ToonExtensionData] property '{type}.{property.Name}' must be IDictionary<string, object?> or Dictionary<string, object?>, not '{property.PropertyType}'.");
            if (property.GetMethod?.IsPublic != true)
                throw new NotSupportedException($"TOON [ToonExtensionData] property '{type}.{property.Name}' requires a public getter.");
            found = property;
        }
        return found;
    }
}
