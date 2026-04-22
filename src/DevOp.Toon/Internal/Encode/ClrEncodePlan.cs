#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DevOp.Toon.Internal.Encode;

/// <summary>
/// Builds and caches reflection plans for CLR object graphs that can use the typed columnar encoder path.
/// </summary>
internal static class ClrEncodePlanCache
{
    private static readonly ConcurrentDictionary<Type, ClrObjectPlan?> Cache = new();

    /// <summary>
    /// Gets the cached encoding plan for a plain CLR object type, or builds one when the type is eligible.
    /// </summary>
    /// <param name="type">The CLR type to inspect.</param>
    /// <returns>An object plan for supported plain object types; otherwise, <see langword="null"/>.</returns>
    public static ClrObjectPlan? GetPlan(Type type)
        => Cache.GetOrAdd(type, static t => BuildPlan(t, new HashSet<Type>()));

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

/// <summary>
/// Describes the readable properties of a CLR object and splits them into scalar header fields and nested spill fields.
/// </summary>
internal sealed class ClrObjectPlan
{
    /// <summary>
    /// Initializes a complete object encoding plan.
    /// </summary>
    /// <param name="type">The CLR type represented by this plan.</param>
    /// <param name="properties">All readable encoded properties in output order.</param>
    /// <param name="propertyPlans">Classification data for each property.</param>
    /// <param name="headerProperties">Properties that can be written into a tabular header row.</param>
    /// <param name="nonHeaderProperties">Properties that must be emitted as nested or spill values.</param>
    /// <param name="nonHeaderPropertyPlans">Classification data for non-header properties.</param>
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

    /// <summary>
    /// Gets the CLR type represented by this plan.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets all encoded properties in output order.
    /// </summary>
    public IReadOnlyList<NativeNormalize.PropertyMetadata> Properties { get; }

    /// <summary>
    /// Gets per-property classification metadata aligned with <see cref="Properties"/>.
    /// </summary>
    public ClrPropertyPlan[] PropertyPlans { get; }

    /// <summary>
    /// Gets scalar properties that can be written into columnar header rows.
    /// </summary>
    public NativeNormalize.PropertyMetadata[] HeaderProperties { get; }

    /// <summary>
    /// Gets nested, enumerable, dictionary, or fallback properties that are written outside the header row.
    /// </summary>
    public NativeNormalize.PropertyMetadata[] NonHeaderProperties { get; }

    /// <summary>
    /// Gets per-property classification metadata aligned with <see cref="NonHeaderProperties"/>.
    /// </summary>
    public ClrPropertyPlan[] NonHeaderPropertyPlans { get; }
}

/// <summary>
/// Describes how a single CLR property should be encoded.
/// </summary>
internal readonly struct ClrPropertyPlan
{
    /// <summary>
    /// Initializes a property encoding plan.
    /// </summary>
    /// <param name="property">Reflection and naming metadata for the property.</param>
    /// <param name="kind">The encoding category for the property.</param>
    /// <param name="objectPlan">A nested object plan when the property is a supported object.</param>
    /// <param name="rowPlan">A row plan when the property is an enumerable of supported objects.</param>
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

    /// <summary>
    /// Gets reflection, naming, and cached accessor metadata for the property.
    /// </summary>
    public NativeNormalize.PropertyMetadata Property { get; }

    /// <summary>
    /// Gets the encoding category selected for the property.
    /// </summary>
    public ClrPropertyKind Kind { get; }

    /// <summary>
    /// Gets a nested object plan when <see cref="Kind"/> is <see cref="ClrPropertyKind.Object"/>.
    /// </summary>
    public ClrObjectPlan? ObjectPlan { get; }

    /// <summary>
    /// Gets a nested row plan when <see cref="Kind"/> is <see cref="ClrPropertyKind.TypedObjectArray"/>.
    /// </summary>
    public ClrRowPlan? RowPlan { get; }
}

/// <summary>
/// Describes the columnar layout for an enumerable whose elements are plain CLR objects.
/// </summary>
internal readonly struct ClrRowPlan
{
    /// <summary>
    /// Initializes a typed row plan.
    /// </summary>
    /// <param name="elementType">The element type of the enumerable.</param>
    /// <param name="headerProperties">Scalar element properties that become columns.</param>
    /// <param name="nonHeaderProperties">Element properties that are emitted as nested spill values.</param>
    public ClrRowPlan(
        Type elementType,
        NativeNormalize.PropertyMetadata[] headerProperties,
        NativeNormalize.PropertyMetadata[] nonHeaderProperties)
    {
        ElementType = elementType;
        HeaderProperties = headerProperties;
        NonHeaderProperties = nonHeaderProperties;
    }

    /// <summary>
    /// Gets the enumerable element type.
    /// </summary>
    public Type ElementType { get; }

    /// <summary>
    /// Gets element properties that can be written as columnar header fields.
    /// </summary>
    public NativeNormalize.PropertyMetadata[] HeaderProperties { get; }

    /// <summary>
    /// Gets element properties that must be written outside the columnar header row.
    /// </summary>
    public NativeNormalize.PropertyMetadata[] NonHeaderProperties { get; }
}

/// <summary>
/// Identifies the encoder strategy for a CLR property in a typed object plan.
/// </summary>
internal enum ClrPropertyKind
{
    /// <summary>The property can be encoded as a scalar TOON primitive.</summary>
    Primitive,
    /// <summary>The property implements <see cref="IDictionary"/> and is handled by fallback object normalization.</summary>
    Dictionary,
    /// <summary>The property is an enumerable that cannot use a typed object-array row plan.</summary>
    Enumerable,
    /// <summary>The property is an enumerable of plain objects that can use columnar row encoding.</summary>
    TypedObjectArray,
    /// <summary>The property is a nested plain object with its own object plan.</summary>
    Object,
    /// <summary>The property requires the general normalizer and native encoder path.</summary>
    Fallback
}
