#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace DevOp.Toon.Internal.Encode;

internal static class ClrEncodePlanCache
{
    private static readonly Dictionary<Type, ClrObjectPlan?> Cache = new();
    private static readonly object CacheLock = new();

    public static ClrObjectPlan? GetPlan(Type type)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var plan = BuildPlan(type, new HashSet<Type>());
            Cache[type] = plan;
            return plan;
        }
    }

    private static ClrObjectPlan? BuildPlan(Type type, HashSet<Type> activeTypes)
    {
        if (!NativeNormalize.IsPlainObjectType(type))
        {
            return null;
        }

        if (!activeTypes.Add(type))
        {
            return null;
        }

        try
        {
            var properties = NativeNormalize.GetCachedProperties(type);
            if (properties.Count == 0)
            {
                return null;
            }

            var headerProperties = new List<NativeNormalize.PropertyMetadata>(properties.Count);
            var nonHeaderProperties = new List<NativeNormalize.PropertyMetadata>(properties.Count);
            var nonHeaderPropertyPlans = new List<ClrPropertyPlan>(properties.Count);
            var propertyPlans = new ClrPropertyPlan[properties.Count];

            for (int i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                var childObjectPlan = property.NestedObjectProperties is not null
                    ? BuildPlan(property.Info.PropertyType, activeTypes)
                    : null;
                var childRowPlan = TryBuildEnumerableRowPlan(property, activeTypes);
                var kind = ClassifyProperty(property, childObjectPlan, childRowPlan);

                propertyPlans[i] = new ClrPropertyPlan(property, kind, childObjectPlan, childRowPlan);

                if (kind == ClrPropertyKind.Primitive)
                {
                    headerProperties.Add(property);
                }
                else
                {
                    nonHeaderProperties.Add(property);
                    nonHeaderPropertyPlans.Add(propertyPlans[i]);
                }
            }

            return new ClrObjectPlan(
                type,
                properties,
                propertyPlans,
                headerProperties.ToArray(),
                nonHeaderProperties.ToArray(),
                nonHeaderPropertyPlans.ToArray());
        }
        finally
        {
            activeTypes.Remove(type);
        }
    }

    private static ClrRowPlan? TryBuildEnumerableRowPlan(NativeNormalize.PropertyMetadata property, HashSet<Type> activeTypes)
    {
        var elementType = property.EnumerableElementType;
        if (elementType is null || !NativeNormalize.IsPlainObjectType(elementType))
        {
            return null;
        }

        var childPlan = BuildPlan(elementType, activeTypes);
        if (childPlan is null)
        {
            return null;
        }

        return new ClrRowPlan(elementType, childPlan.HeaderProperties, childPlan.NonHeaderProperties);
    }

    private static ClrPropertyKind ClassifyProperty(
        NativeNormalize.PropertyMetadata property,
        ClrObjectPlan? childObjectPlan,
        ClrRowPlan? childRowPlan)
    {
        if (NativeNormalize.IsPrimitiveClrType(property.Info.PropertyType))
        {
            return ClrPropertyKind.Primitive;
        }

        if (typeof(IDictionary).IsAssignableFrom(property.Info.PropertyType))
        {
            return ClrPropertyKind.Dictionary;
        }

        if (childRowPlan is not null)
        {
            return ClrPropertyKind.TypedObjectArray;
        }

        if (property.EnumerableElementType is not null && typeof(IEnumerable).IsAssignableFrom(property.Info.PropertyType) && property.Info.PropertyType != typeof(string))
        {
            return ClrPropertyKind.Enumerable;
        }

        if (childObjectPlan is not null)
        {
            return ClrPropertyKind.Object;
        }

        return ClrPropertyKind.Fallback;
    }
}

internal sealed class ClrObjectPlan
{
    public ClrObjectPlan(
        Type type,
        IReadOnlyList<NativeNormalize.PropertyMetadata> properties,
        ClrPropertyPlan[] propertyPlans,
        NativeNormalize.PropertyMetadata[] headerProperties,
        NativeNormalize.PropertyMetadata[] nonHeaderProperties,
        ClrPropertyPlan[] nonHeaderPropertyPlans)
    {
        Type = type;
        Properties = properties;
        PropertyPlans = propertyPlans;
        HeaderProperties = headerProperties;
        NonHeaderProperties = nonHeaderProperties;
        NonHeaderPropertyPlans = nonHeaderPropertyPlans;
    }

    public Type Type { get; }

    public IReadOnlyList<NativeNormalize.PropertyMetadata> Properties { get; }

    public ClrPropertyPlan[] PropertyPlans { get; }

    public NativeNormalize.PropertyMetadata[] HeaderProperties { get; }

    public NativeNormalize.PropertyMetadata[] NonHeaderProperties { get; }

    public ClrPropertyPlan[] NonHeaderPropertyPlans { get; }
}

internal readonly struct ClrPropertyPlan
{
    public ClrPropertyPlan(
        NativeNormalize.PropertyMetadata property,
        ClrPropertyKind kind,
        ClrObjectPlan? objectPlan,
        ClrRowPlan? rowPlan)
    {
        Property = property;
        Kind = kind;
        ObjectPlan = objectPlan;
        RowPlan = rowPlan;
    }

    public NativeNormalize.PropertyMetadata Property { get; }

    public ClrPropertyKind Kind { get; }

    public ClrObjectPlan? ObjectPlan { get; }

    public ClrRowPlan? RowPlan { get; }
}

internal readonly struct ClrRowPlan
{
    public ClrRowPlan(
        Type elementType,
        NativeNormalize.PropertyMetadata[] headerProperties,
        NativeNormalize.PropertyMetadata[] nonHeaderProperties)
    {
        ElementType = elementType;
        HeaderProperties = headerProperties;
        NonHeaderProperties = nonHeaderProperties;
    }

    public Type ElementType { get; }

    public NativeNormalize.PropertyMetadata[] HeaderProperties { get; }

    public NativeNormalize.PropertyMetadata[] NonHeaderProperties { get; }
}

internal enum ClrPropertyKind
{
    Primitive,
    Dictionary,
    Enumerable,
    TypedObjectArray,
    Object,
    Fallback
}
