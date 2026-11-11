#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Shared;

/// <summary>
/// Reads and writes the <see cref="ToonExtensionDataAttribute"/> property of a CLR type for the legacy typed decoders
/// and the JSON deserialization route, and converts captured values into their untyped CLR form.
/// </summary>
internal sealed class ExtensionDataAccessor
{
    private static readonly ConcurrentDictionary<Type, ExtensionDataAccessor?> accessors = new();

    private readonly Func<object, object?> getter;
    private readonly Action<object, object?>? setter;

    private ExtensionDataAccessor(Type type, PropertyInfo property)
    {
        Property = property;
        ClrName = property.Name;
        WireName = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name is { Length: > 0 } alias ? alias : property.Name;
        DisplayName = $"{type}.{property.Name}";
        var target = Expression.Parameter(typeof(object), "instance");
        Expression typedTarget = type.IsValueType ? Expression.Unbox(target, type) : Expression.Convert(target, type);
        getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(Expression.Property(typedTarget, property), typeof(object)), target).Compile();
        if (property.SetMethod?.IsPublic == true)
        {
            var value = Expression.Parameter(typeof(object), "value");
            setter = Expression.Lambda<Action<object, object?>>(
                Expression.Call(typedTarget, property.SetMethod, Expression.Convert(value, property.PropertyType)), target, value).Compile();
        }
    }

    /// <summary>Gets the extension data property.</summary>
    internal PropertyInfo Property { get; }

    /// <summary>Gets the CLR name of the extension data property.</summary>
    internal string ClrName { get; }

    /// <summary>Gets the name the property was written under before entries were flattened: its TOON alias, or its CLR name.</summary>
    internal string WireName { get; }

    /// <summary>Gets the type-qualified property name used in error messages.</summary>
    internal string DisplayName { get; }

    /// <summary>
    /// Returns the accessor for the type's extension data property, or <see langword="null"/> when it declares none.
    /// </summary>
    /// <exception cref="NotSupportedException">The type declares an invalid extension data property.</exception>
    internal static ExtensionDataAccessor? Find(Type type)
    {
        if (accessors.TryGetValue(type, out var cached)) return cached;
        var property = ExtensionDataMetadata.Find(type);
        return accessors.GetOrAdd(type, property == null ? null : new ExtensionDataAccessor(type, property));
    }

    /// <summary>Returns whether <paramref name="property"/> is this accessor's extension data property.</summary>
    internal bool IsExtension(PropertyInfo property) => ExtensionDataMetadata.IsExtension(property, Property);

    /// <summary>
    /// Returns whether <paramref name="key"/> names the extension data property itself, the key under which earlier releases
    /// wrote the dictionary as a nested object on the legacy profiles.
    /// </summary>
    internal bool IsNestedKey(string key, StringComparer comparer) => comparer.Equals(key, WireName) || comparer.Equals(key, ClrName);

    /// <summary>
    /// Returns the index of the constructor parameter that receives the extension data, or -1 when none does.
    /// </summary>
    internal int FindConstructorArgument(ParameterInfo[] parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, ClrName, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    /// <summary>
    /// Adds the captured entries to the instance's dictionary, creating and assigning one when the property is null.
    /// </summary>
    /// <exception cref="NotSupportedException">The property is null and has no public setter.</exception>
    internal void Populate(object instance, ExtensionDataEntries entries)
    {
        if (getter(instance) is IDictionary<string, object?> existing)
        {
            foreach (var entry in entries.Entries) existing[entry.Key] = entry.Value;
            return;
        }

        if (setter == null)
            throw new NotSupportedException($"TOON cannot store extension data in '{DisplayName}': it has no public setter and is not initialised.");
        setter(instance, entries.Entries);
    }

    /// <summary>
    /// Converts a decoded node into the untyped form stored in extension data: dictionaries, lists, strings, booleans,
    /// <see langword="null"/>, and every number as <see cref="double"/>.
    /// </summary>
    internal static object? ToValue(NativeNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case NativePrimitiveNode primitive:
                return primitive.Value switch
                {
                    double or string or bool or null => primitive.Value,
                    decimal number => (double)number,
                    long number => (double)number,
                    int number => (double)number,
                    ulong number => (double)number,
                    uint number => (double)number,
                    short number => (double)number,
                    ushort number => (double)number,
                    byte number => (double)number,
                    sbyte number => (double)number,
                    float number => (double)number,
                    var other => other
                };
            case NativeArrayNode array:
                var list = new List<object?>(array.Count);
                foreach (var item in array) list.Add(ToValue(item));
                return list;
            case NativeObjectNode obj:
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var entry in obj) dictionary[entry.Key] = ToValue(entry.Value);
                return dictionary;
            default:
                return null;
        }
    }
}
