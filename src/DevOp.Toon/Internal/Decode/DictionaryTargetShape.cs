#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace DevOp.Toon.Internal.Decode;

/// <summary>
/// Classifies dictionary-shaped target types for the legacy typed materializers, so string-keyed dictionaries of any
/// concrete type bind their entries and dictionaries the legacy routes cannot key are rejected instead of decoding empty.
/// </summary>
internal static class DictionaryTargetShape
{
    /// <summary>
    /// Inspects <paramref name="type"/> for a dictionary shape.
    /// </summary>
    /// <param name="type">The target type.</param>
    /// <param name="valueType">The dictionary value type when the target can be bound; otherwise <see langword="null"/>.</param>
    /// <param name="concreteType">The type to construct when the target can be bound; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="type"/> is not a dictionary. <see langword="true"/> when it is; then a
    /// <see langword="null"/> <paramref name="concreteType"/> means the target cannot be bound (non-string keys, no
    /// parameterless constructor, or no non-generic <see cref="IDictionary"/> to populate).
    /// </returns>
    public static bool TryResolve(Type type, out Type? valueType, out Type? concreteType)
    {
        valueType = null;
        concreteType = null;

        var isDictionary = false;
        Type? stringValueType = null;
        foreach (var candidate in EnumerateCandidates(type))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (definition != typeof(IDictionary<,>) && definition != typeof(IReadOnlyDictionary<,>))
            {
                continue;
            }

            isDictionary = true;
            var arguments = candidate.GetGenericArguments();
            if (arguments[0] == typeof(string))
            {
                stringValueType = arguments[1];
                break;
            }
        }

        if (!isDictionary)
        {
            return typeof(IDictionary).IsAssignableFrom(type);
        }

        if (stringValueType == null)
        {
            return true;
        }

        if (type.IsInterface || type.IsAbstract)
        {
            var defaultConcrete = typeof(Dictionary<,>).MakeGenericType(typeof(string), stringValueType);
            if (type.IsAssignableFrom(defaultConcrete))
            {
                valueType = stringValueType;
                concreteType = defaultConcrete;
            }

            return true;
        }

        if (!type.IsValueType &&
            type.GetConstructor(Type.EmptyTypes) != null &&
            typeof(IDictionary).IsAssignableFrom(type) &&
            typeof(IDictionary<,>).MakeGenericType(typeof(string), stringValueType).IsAssignableFrom(type))
        {
            valueType = stringValueType;
            concreteType = type;
        }

        return true;
    }

    private static IEnumerable<Type> EnumerateCandidates(Type type)
    {
        if (type.IsInterface)
        {
            yield return type;
        }

        foreach (var implemented in type.GetInterfaces())
        {
            yield return implemented;
        }
    }
}
