using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Recognises dictionaries for encoding: types implementing <see cref="IDictionary"/>, and types that implement only
/// the generic <see cref="IDictionary{TKey,TValue}"/> or <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
/// </summary>
internal static class DictionaryEntries
{
    private static readonly ConcurrentDictionary<Type, Func<object, IEnumerable<DictionaryEntry>>?> GenericAdapters = new();

    /// <summary>Returns whether values of <paramref name="type"/> encode as objects rather than as key/value arrays.</summary>
    internal static bool IsDictionaryType(Type type)
        => typeof(IDictionary).IsAssignableFrom(type) || GetGenericAdapter(type) is not null;

    /// <summary>Returns the entries of <paramref name="value"/> when it is a dictionary; otherwise <see langword="null"/>.</summary>
    internal static IEnumerable<DictionaryEntry>? Get(object? value)
    {
        if (value is null or string)
            return null;

        if (value is IDictionary dictionary)
            return FromNonGeneric(dictionary);

        return GetGenericAdapter(value.GetType())?.Invoke(value);
    }

    private static IEnumerable<DictionaryEntry> FromNonGeneric(IDictionary dictionary)
    {
        foreach (DictionaryEntry entry in dictionary)
            yield return entry;
    }

    private static Func<object, IEnumerable<DictionaryEntry>>? GetGenericAdapter(Type type)
        => GenericAdapters.GetOrAdd(type, static t => CreateGenericAdapter(t));

    private static Func<object, IEnumerable<DictionaryEntry>>? CreateGenericAdapter(Type type)
    {
        var dictionaryInterface = FindGenericDictionaryInterface(type);
        if (dictionaryInterface is null)
            return null;

        var arguments = dictionaryInterface.GetGenericArguments();
        var adapterType = typeof(Adapter<,>).MakeGenericType(arguments[0], arguments[1]);
        return (Func<object, IEnumerable<DictionaryEntry>>)adapterType
            .GetField(nameof(Adapter<object, object>.Instance), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
    }

    private static Type? FindGenericDictionaryInterface(Type type)
    {
        if (IsGenericDictionaryInterface(type))
            return type;

        foreach (var candidate in type.GetInterfaces())
        {
            if (IsGenericDictionaryInterface(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsGenericDictionaryInterface(Type type)
    {
        if (!type.IsInterface || !type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);
    }

    private static class Adapter<TKey, TValue>
    {
        internal static readonly Func<object, IEnumerable<DictionaryEntry>> Instance =
            static value => Enumerate((IEnumerable<KeyValuePair<TKey, TValue>>)value);

        private static IEnumerable<DictionaryEntry> Enumerate(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            foreach (var pair in source)
                yield return new DictionaryEntry(pair.Key!, pair.Value);
        }
    }
}
