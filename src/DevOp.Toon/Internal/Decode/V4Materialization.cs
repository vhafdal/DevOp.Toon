#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DevOp.Toon.Core;
using DevOp.Toon.Internal.Encode;
using DevOp.Toon.Internal.Shared;

namespace DevOp.Toon.Internal.Decode;

internal static class V4Materialization
{
    private static readonly ConcurrentDictionary<Type, Plan> plans = new();
    private static readonly ConcurrentDictionary<Type, CollectionPlan> collections = new();
    private static readonly ConcurrentDictionary<Type, Plan?> bindablePlans = new();
    internal static Plan GetPlan(Type type) => plans.GetOrAdd(type, BuildPlan);

    /// <summary>
    /// Returns whether typed 4.x decoding of <paramref name="type"/> binds <paramref name="key"/> to a declared member,
    /// using the same name comparison as the decoder; <see langword="false"/> when the type cannot be decoded.
    /// </summary>
    internal static bool BindsMember(Type type, string key)
    {
        var plan = bindablePlans.GetOrAdd(type, static target =>
        {
            try { return GetPlan(target); }
            catch (NotSupportedException) { return null; }
        });
        return plan != null && plan.Members.ContainsKey(key);
    }

    internal delegate bool PrimitiveSetter(object instance, ReadOnlySpan<char> token);
    private delegate T SpanParser<T>(ReadOnlySpan<char> token);

    internal sealed class Member
    {
        internal Type Type = null!;
        internal string Name = string.Empty;
        internal string WireName = string.Empty;
        internal int Index;
        internal int Argument = -1;
        internal Action<object, object?>? Setter;
        internal PrimitiveSetter? SimpleSetter;
        internal PrimitiveSetter? TextSetter;
    }

    internal sealed class Plan
    {
        internal Type Type = null!;
        internal Type? ValueType;
        internal Func<object, string, bool>? DictionaryContains;
        internal Action<object, string, object?>? DictionarySet;
        internal Func<object>? Factory;
        internal Func<object?[], object>? Constructor;
        internal object?[]? Defaults;
        internal Dictionary<string, Member> Members = new(StringComparer.Ordinal);
        internal Func<object, object?>? ExtensionGetter;
        internal Action<object, object?>? ExtensionSetter;
        internal int ExtensionArgument = -1;
        internal string? ExtensionName;
        internal HashSet<string>? UnboundMembers;
    }

    private static Plan BuildPlan(Type type)
    {
        var plan = new Plan { Type = type };
        var dictionary = type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>) && t.GetGenericArguments()[0] == typeof(string));
        if (dictionary == null)
            dictionary = type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && t.GetGenericArguments()[0] == typeof(string));
        if (dictionary != null || type == typeof(object) || type == typeof(IDictionary))
        {
            plan.ValueType = dictionary?.GetGenericArguments()[1] ?? typeof(object);
            var concrete = type.IsInterface || type.IsAbstract || type == typeof(object) ? typeof(Dictionary<,>).MakeGenericType(typeof(string), plan.ValueType) : type;
            if (concrete.GetConstructor(Type.EmptyTypes) == null) throw new NotSupportedException($"TOON cannot construct dictionary target '{type}'.");
            plan.Factory = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(concrete), typeof(object))).Compile();
            var mutable = typeof(IDictionary<,>).MakeGenericType(typeof(string), plan.ValueType);
            if (!mutable.IsAssignableFrom(concrete)) throw new NotSupportedException($"TOON cannot populate dictionary target '{type}'.");
            var dictionaryInstance = Expression.Parameter(typeof(object), "instance");
            var dictionaryKey = Expression.Parameter(typeof(string), "key");
            var dictionaryValue = Expression.Parameter(typeof(object), "value");
            var typedDictionary = Expression.Convert(dictionaryInstance, mutable);
            plan.DictionaryContains = Expression.Lambda<Func<object, string, bool>>(Expression.Call(typedDictionary, mutable.GetMethod("ContainsKey")!, dictionaryKey), dictionaryInstance, dictionaryKey).Compile();
            plan.DictionarySet = Expression.Lambda<Action<object, string, object?>>(Expression.Call(typedDictionary, mutable.GetProperty("Item")!.SetMethod!, dictionaryKey, Expression.Convert(dictionaryValue, plan.ValueType)), dictionaryInstance, dictionaryKey, dictionaryValue).Compile();
            return plan;
        }
        if (typeof(IEnumerable).IsAssignableFrom(type) || type.IsPrimitive || type == typeof(string))
            throw new NotSupportedException($"TOON object syntax cannot bind target type '{type}'.");
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToArray();
        var selectedParameters = Array.Empty<ParameterInfo>();
        var empty = type.GetConstructor(Type.EmptyTypes);
        if (empty != null || type.IsValueType)
            plan.Factory = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(type), typeof(object))).Compile();
        else
        {
            var constructors = type.GetConstructors().Where(c => c.GetParameters().All(p => p.Name != null && (p.HasDefaultValue || properties.Any(prop => string.Equals(prop.Name, p.Name, StringComparison.OrdinalIgnoreCase))))).OrderByDescending(c => c.GetParameters().Length).ToArray();
            if (constructors.Length == 0 || (constructors.Length > 1 && constructors[0].GetParameters().Length == constructors[1].GetParameters().Length))
                throw new NotSupportedException($"TOON cannot bind target type '{type}'.");
            var constructor = constructors[0];
            var parameters = constructor.GetParameters();
            selectedParameters = parameters;
            plan.Members = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
            plan.Defaults = parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var args = Expression.Parameter(typeof(object[]), "args");
            plan.Constructor = Expression.Lambda<Func<object?[], object>>(Expression.Convert(Expression.New(constructor, parameters.Select((p, i) => Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType))), typeof(object)), args).Compile();
        }
        var extension = ExtensionDataMetadata.Find(type);
        var index = 0;
        foreach (var property in properties)
        {
            if (extension != null && ExtensionDataMetadata.IsExtension(property, extension))
            {
                var target = Expression.Parameter(typeof(object), "instance");
                plan.ExtensionName = $"{type}.{property.Name}";
                plan.ExtensionGetter = Expression.Lambda<Func<object, object?>>(Expression.Convert(Expression.Property(type.IsValueType ? Expression.Unbox(target, type) : Expression.Convert(target, type), property), typeof(object)), target).Compile();
                if (plan.Constructor != null)
                    plan.ExtensionArgument = Array.FindIndex(selectedParameters, p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                if (property.SetMethod?.IsPublic == true)
                {
                    var value = Expression.Parameter(typeof(object), "value");
                    plan.ExtensionSetter = Expression.Lambda<Action<object, object?>>(Expression.Call(type.IsValueType ? Expression.Unbox(target, type) : Expression.Convert(target, type), property.SetMethod!, Expression.Convert(value, property.PropertyType)), target, value).Compile();
                }
                continue;
            }
            var member = new Member { Type = property.PropertyType, Name = property.Name, WireName = property.Name, Index = index++ };
            if (plan.Constructor != null)
            {
                member.Argument = Array.FindIndex(selectedParameters, p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            }
            if (property.SetMethod?.IsPublic == true)
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                var value = Expression.Parameter(typeof(object), "value");
                member.SimpleSetter = CreateSimpleSetter(property);
                member.TextSetter = CreateTextSetter(property);
                member.Setter = Expression.Lambda<Action<object, object?>>(Expression.Call(type.IsValueType ? Expression.Unbox(instance, type) : Expression.Convert(instance, type), property.SetMethod!, Expression.Convert(value, property.PropertyType)), instance, value).Compile();
            }
            if (member.Setter != null || member.Argument >= 0)
            {
                plan.Members[property.Name] = member;
                var alias = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                if (alias != null && alias != property.Name) plan.Members[alias] = new Member { Type = member.Type, Name = member.Name, WireName = alias, Index = index++, Argument = member.Argument, Setter = member.Setter, SimpleSetter = member.SimpleSetter, TextSetter = member.TextSetter };
            }
            else if (extension != null)
            {
                // A declared member the decoder cannot bind is never captured as extension data.
                var unbound = plan.UnboundMembers ??= new HashSet<string>(plan.Members.Comparer);
                unbound.Add(property.Name);
                var alias = property.GetCustomAttribute<ToonPropertyNameAttribute>()?.Name;
                if (alias is { Length: > 0 }) unbound.Add(alias);
            }
        }
        return plan;
    }

    private static PrimitiveSetter? CreateSimpleSetter(PropertyInfo property)
    {
        var nullable = Nullable.GetUnderlyingType(property.PropertyType);
        var type = nullable ?? property.PropertyType;
        if (type == typeof(bool)) return nullable == null ? BuildSetter<bool>(property, ParseBoolean) : BuildSetter<bool?>(property, token => ParseBoolean(token));
        if (type == typeof(byte)) return nullable == null ? BuildSetter<byte>(property, ParseByte) : BuildSetter<byte?>(property, token => ParseByte(token));
        if (type == typeof(sbyte)) return nullable == null ? BuildSetter<sbyte>(property, ParseSByte) : BuildSetter<sbyte?>(property, token => ParseSByte(token));
        if (type == typeof(short)) return nullable == null ? BuildSetter<short>(property, ParseInt16) : BuildSetter<short?>(property, token => ParseInt16(token));
        if (type == typeof(ushort)) return nullable == null ? BuildSetter<ushort>(property, ParseUInt16) : BuildSetter<ushort?>(property, token => ParseUInt16(token));
        if (type == typeof(int)) return nullable == null ? BuildSetter<int>(property, ParseInt32) : BuildSetter<int?>(property, token => ParseInt32(token));
        if (type == typeof(uint)) return nullable == null ? BuildSetter<uint>(property, ParseUInt32) : BuildSetter<uint?>(property, token => ParseUInt32(token));
        if (type == typeof(long)) return nullable == null ? BuildSetter<long>(property, ParseInt64) : BuildSetter<long?>(property, token => ParseInt64(token));
        if (type == typeof(ulong)) return nullable == null ? BuildSetter<ulong>(property, ParseUInt64) : BuildSetter<ulong?>(property, token => ParseUInt64(token));
        if (type == typeof(float)) return nullable == null ? BuildSetter<float>(property, ParseSingle) : BuildSetter<float?>(property, token => ParseSingle(token));
        if (type == typeof(double)) return nullable == null ? BuildSetter<double>(property, ParseDouble) : BuildSetter<double?>(property, token => ParseDouble(token));
        if (type == typeof(decimal)) return nullable == null ? BuildSetter<decimal>(property, ParseDecimal) : BuildSetter<decimal?>(property, token => ParseDecimal(token));
        return null;
    }

    private static PrimitiveSetter BuildSetter<T>(PropertyInfo property, SpanParser<T> parser, bool handleNull = true)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(T), "value");
        var declaring = property.DeclaringType!;
        var setter = Expression.Lambda<Action<object, T>>(Expression.Call(declaring.IsValueType ? Expression.Unbox(instance, declaring) : Expression.Convert(instance, declaring), property.SetMethod!, value), instance, value).Compile();
        return (target, token) =>
        {
            T parsed;
            try { parsed = handleNull && token.SequenceEqual("null") ? default! : parser(token); }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            setter(target, parsed);
            return true;
        };
    }

    private static PrimitiveSetter? CreateTextSetter(PropertyInfo property)
    {
        var nullable = Nullable.GetUnderlyingType(property.PropertyType);
        var type = nullable ?? property.PropertyType;
        if (type == typeof(DateTime)) return nullable == null ? BuildSetter<DateTime>(property, ParseDateTime, handleNull: false) : BuildSetter<DateTime?>(property, token => ParseDateTime(token), handleNull: false);
        if (type == typeof(DateTimeOffset)) return nullable == null ? BuildSetter<DateTimeOffset>(property, ParseDateTimeOffset, handleNull: false) : BuildSetter<DateTimeOffset?>(property, token => ParseDateTimeOffset(token), handleNull: false);
        if (type == typeof(Guid)) return nullable == null ? BuildSetter<Guid>(property, ParseGuid, handleNull: false) : BuildSetter<Guid?>(property, token => ParseGuid(token), handleNull: false);
        if (type == typeof(TimeSpan)) return nullable == null ? BuildSetter<TimeSpan>(property, ParseTimeSpan, handleNull: false) : BuildSetter<TimeSpan?>(property, token => ParseTimeSpan(token), handleNull: false);
#if NET6_0_OR_GREATER
        if (type == typeof(DateOnly)) return nullable == null ? BuildSetter<DateOnly>(property, ParseDateOnly, handleNull: false) : BuildSetter<DateOnly?>(property, token => ParseDateOnly(token), handleNull: false);
        if (type == typeof(TimeOnly)) return nullable == null ? BuildSetter<TimeOnly>(property, ParseTimeOnly, handleNull: false) : BuildSetter<TimeOnly?>(property, token => ParseTimeOnly(token), handleNull: false);
#endif
        return null;
    }

    private static DateTime ParseDateTime(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        return DateTime.Parse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
#else
        if (DateTime.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)) return value;
        return DateTime.Parse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
#endif
    }

    private static DateTimeOffset ParseDateTimeOffset(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        return DateTimeOffset.Parse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None);
#else
        if (DateTimeOffset.TryParseExact(token, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return value;
        return DateTimeOffset.Parse(token, CultureInfo.InvariantCulture, DateTimeStyles.None);
#endif
    }

    private static Guid ParseGuid(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        return Guid.Parse(token.ToString());
#else
        return Guid.Parse(token);
#endif
    }

    private static TimeSpan ParseTimeSpan(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        return TimeSpan.Parse(token.ToString(), CultureInfo.InvariantCulture);
#else
        return TimeSpan.Parse(token, CultureInfo.InvariantCulture);
#endif
    }

#if NET6_0_OR_GREATER
    private static DateOnly ParseDateOnly(ReadOnlySpan<char> token) => DateOnly.Parse(token, CultureInfo.InvariantCulture);
    private static TimeOnly ParseTimeOnly(ReadOnlySpan<char> token) => TimeOnly.Parse(token, CultureInfo.InvariantCulture);
#endif

    private static bool ParseBoolean(ReadOnlySpan<char> token)
    {
        if (token.SequenceEqual("true")) return true;
        if (token.SequenceEqual("false")) return false;
        throw new FormatException("Not a boolean token.");
    }

    private static byte ParseByte(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = byte.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = byte.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static sbyte ParseSByte(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = sbyte.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = sbyte.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static short ParseInt16(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = short.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = short.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static ushort ParseUInt16(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = ushort.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = ushort.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static int ParseInt32(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = int.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = int.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static uint ParseUInt32(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = uint.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = uint.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static long ParseInt64(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = long.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = long.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static ulong ParseUInt64(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = ulong.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = ulong.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    private static float ParseSingle(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = float.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        if (float.IsInfinity(value) || float.IsNaN(value)) throw ToonFormatException.Syntax("Number outside supported finite range");
        return value == 0 ? default : value;
    }

    private static double ParseDouble(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = double.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        if (double.IsInfinity(value) || double.IsNaN(value)) throw ToonFormatException.Syntax("Number outside supported finite range");
        return value == 0 ? default : value;
    }

    private static decimal ParseDecimal(ReadOnlySpan<char> token)
    {
#if NETSTANDARD2_0
        var value = decimal.Parse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        var value = decimal.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
        return value == 0 ? default : value;
    }

    internal struct ObjectBuilder
    {
        private readonly Plan? plan;
        private readonly Type? type;
        private readonly object? instance;
        private readonly object?[]? arguments;
        private Dictionary<string, object?>? pending;
        private ulong seen;
        private HashSet<string>? overflow;
        private IDictionary<string, object?>? extension;

        internal ObjectBuilder(Type? type)
        {
            this.type = type; plan = type == null || type == typeof(void) ? null : GetPlan(type);
            instance = type == null ? new NativeObjectNode() : plan?.Factory?.Invoke();
            arguments = plan?.Defaults == null ? null : (object?[])plan.Defaults.Clone();
            pending = null; seen = 0; overflow = null; extension = null;
        }

        internal Type? FieldType(string name) => type == null ? null : type == typeof(void) ? typeof(void) : plan!.ValueType ?? (plan.Members.TryGetValue(name, out var member) ? member.Type : plan.ExtensionName != null && plan.UnboundMembers?.Contains(name) != true ? typeof(object) : typeof(void));
        internal Member? FindMember(string name) => plan != null && plan.Members.TryGetValue(name, out var member) ? member : null;
        internal bool Contains(string name, Member? boundMember = null)
        {
            if (instance is NativeObjectNode native) return native.ContainsKey(name);
            if (instance is IDictionary dictionary) return dictionary.Contains(name);
            if (instance != null && plan?.DictionaryContains != null) return plan.DictionaryContains(instance, name);
            var member = boundMember ?? FindMember(name);
            if (member != null && name == member.WireName && member.Index < 64) return (seen & (1ul << member.Index)) != 0;
            return overflow?.Contains(name) == true;
        }
        internal void Set(string name, object? value, Member? boundMember = null)
        {
            if (instance is NativeObjectNode native) { native[name] = (NativeNode?)value; return; }
            if (instance is IDictionary dictionary) { dictionary[name] = value; return; }
            if (instance != null && plan?.DictionarySet != null) { plan.DictionarySet(instance, name, value); return; }
            if (type == typeof(void))
            {
                (overflow ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
                return;
            }
            var member = boundMember ?? FindMember(name);
            if (member != null)
            {
                if (name == member.WireName && member.Index < 64) seen |= 1ul << member.Index;
                else (overflow ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
                if (member.Argument >= 0) arguments![member.Argument] = value;
                else if (instance != null) member.Setter!(instance, value);
                else (pending ??= new Dictionary<string, object?>(StringComparer.Ordinal))[member.Name] = value;
            }
            else
            {
                (overflow ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
                if (plan?.ExtensionName != null && plan.UnboundMembers?.Contains(name) != true) Extension()[name] = value;
            }
        }

        private IDictionary<string, object?> Extension()
        {
            if (extension != null) return extension;
            if (instance == null) return extension = new Dictionary<string, object?>(StringComparer.Ordinal);
            extension = (IDictionary<string, object?>?)plan!.ExtensionGetter!(instance);
            if (extension != null) return extension;
            if (plan.ExtensionSetter == null) throw UnsetExtension(plan);
            extension = new Dictionary<string, object?>(StringComparer.Ordinal);
            plan.ExtensionSetter(instance, extension);
            return extension;
        }

        private static NotSupportedException UnsetExtension(Plan plan)
            => new($"TOON cannot store extension data in '{plan.ExtensionName}': it has no public setter and is not initialised.");

        internal bool TrySetPrimitive(string name, ReadOnlySpan<char> token, Member? boundMember = null)
        {
            var member = boundMember ?? FindMember(name);
            if (instance == null || member?.SimpleSetter == null) return false;
            if (!member.SimpleSetter(instance, token)) return false;
            if (name == member.WireName && member.Index < 64) seen |= 1ul << member.Index;
            else (overflow ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
            return true;
        }

        internal bool TrySetText(string name, ReadOnlySpan<char> token, bool rejectDuplicate, int? lineNumber, Member? boundMember = null)
        {
            var member = boundMember ?? FindMember(name);
            if (instance == null || member?.TextSetter == null) return false;
            if (token.Length > 0 && token[0] == '"')
            {
                if (token.Length < 2 || token[token.Length - 1] != '"') return false;
                token = token.Slice(1, token.Length - 2);
            }
            if (token.IndexOf('\\') >= 0 || token.IndexOf('"') >= 0) return false;
            for (var p = 0; p < token.Length; p++)
            {
                if (!char.IsSurrogate(token[p])) continue;
                if (!char.IsHighSurrogate(token[p]) || p + 1 >= token.Length || !char.IsLowSurrogate(token[++p]))
                    throw ToonFormatException.Syntax("Unpaired surrogate", lineNumber);
            }
            if (rejectDuplicate && Contains(name, member)) throw ToonFormatException.Syntax("Duplicate object key", lineNumber);
            if (!member.TextSetter(instance, token)) return false;
            if (name == member.WireName && member.Index < 64) seen |= 1ul << member.Index;
            else (overflow ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
            return true;
        }

        internal object? Finish()
        {
            if (arguments == null) return instance;
            if (extension != null && plan!.ExtensionArgument >= 0) arguments[plan.ExtensionArgument] = extension;
            var value = plan!.Constructor!(arguments);
            if (pending != null) foreach (var entry in pending) plan.Members[entry.Key].Setter!(value, entry.Value);
            if (extension != null && plan.ExtensionArgument < 0)
            {
                var target = (IDictionary<string, object?>?)plan.ExtensionGetter!(value);
                if (target != null) foreach (var entry in extension) target[entry.Key] = entry.Value;
                else if (plan.ExtensionSetter != null) plan.ExtensionSetter(value, extension);
                else throw UnsetExtension(plan);
            }
            return value;
        }
    }

    private sealed class CollectionPlan
    {
        internal Type Element = null!;
        internal Func<IList> Factory = null!;
        internal Array? EmptyArray;
    }

    private static CollectionPlan Collection(Type type) => collections.GetOrAdd(type, static target =>
    {
        var element = target.IsArray ? target.GetElementType()! : target.GetInterfaces().Concat(new[] { target }).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] ?? typeof(object);
        var list = typeof(List<>).MakeGenericType(element);
        return new CollectionPlan { Element = element, EmptyArray = target.IsArray ? Array.CreateInstance(element, 0) : null, Factory = Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(list), typeof(IList))).Compile() };
    });

    internal struct ArrayBuilder
    {
        private readonly Type? type;
        private IList? items;
        private readonly NativeArrayNode? native;
        internal Type? ElementType { get; }
        internal int Count { get; private set; }
        internal ArrayBuilder(Type? type)
        {
            this.type = type; Count = 0;
            native = type == null ? new NativeArrayNode() : null;
            var collection = type == null || type == typeof(void) ? null : Collection(type);
            ElementType = type == null ? null : type == typeof(void) ? typeof(void) : collection!.Element;
            items = null;
        }
        internal void Add(object? value)
        {
            if (native != null) native.Add((NativeNode?)value);
            else if (type != null && type != typeof(void)) (items ??= Collection(type).Factory()).Add(value);
            Count++;
        }
        internal object? Finish()
        {
            if (native != null) return native;
            if (type == null || type == typeof(void)) return null;
            if (type.IsArray)
            {
                if (Count == 0) return Collection(type).EmptyArray;
                var array = Array.CreateInstance(ElementType!, Count); items!.CopyTo(array, 0); return array;
            }
            items ??= Collection(type).Factory();
            if (type.IsInstanceOfType(items)) return items;
            var concrete = Activator.CreateInstance(type);
            if (concrete is IList list) { foreach (var item in items!) list.Add(item); return list; }
            throw new NotSupportedException($"TOON cannot bind array target '{type}'.");
        }
    }

    internal static object? Number(ReadOnlySpan<char> token, double parsed, Type? type)
    {
        if (type == null || type == typeof(void) || type == typeof(object)) return Scalar(parsed, type);
        var effective = Nullable.GetUnderlyingType(type) ?? type;
#if NETSTANDARD2_0
        var numericText = token.ToString();
#else
        var numericText = token;
#endif
        var numberType = effective.IsEnum ? Enum.GetUnderlyingType(effective) : effective;
        object value;
        if (numberType == typeof(decimal)) value = decimal.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
        else if (numberType == typeof(long)) value = long.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
        else if (numberType == typeof(ulong)) value = ulong.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
        else if (numberType == typeof(int)) value = int.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
        else if (numberType == typeof(uint)) value = uint.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
        else return Scalar(parsed, type);
        return effective.IsEnum ? Enum.ToObject(effective, value) : value;
    }

    internal static object? Scalar(object? value, Type? type)
    {
        if (type == null) return new NativePrimitiveNode(value);
        if (type == typeof(void)) return null;
        var nullable = Nullable.GetUnderlyingType(type);
        if (value == null) return nullable != null || !type.IsValueType ? null : Activator.CreateInstance(type);
        type = nullable ?? type;
        if (type.IsInstanceOfType(value)) return value;
        if (type.IsEnum) return value is string text ? Enum.Parse(type, text, false) : Enum.ToObject(type, System.Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)!);
        if (value is string str)
        {
            if (type == typeof(Guid)) return Guid.Parse(str);
            if (type == typeof(DateTime)) return DateTime.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.None);
            if (type == typeof(Version)) return Version.Parse(str);
            if (typeof(IEnumerable<byte>).IsAssignableFrom(type))
            {
                var bytes = System.Convert.FromBase64String(str);
                if (type.IsInstanceOfType(bytes)) return bytes;
                if (type.IsAssignableFrom(typeof(List<byte>))) return new List<byte>(bytes);
            }
            if (type == typeof(TimeSpan)) return TimeSpan.Parse(str, CultureInfo.InvariantCulture);
            if (type == typeof(byte[])) return System.Convert.FromBase64String(str);
            if (type == typeof(Uri)) return new Uri(str, UriKind.RelativeOrAbsolute);
#if NET6_0_OR_GREATER
            if (type == typeof(DateOnly)) return DateOnly.Parse(str, CultureInfo.InvariantCulture);
            if (type == typeof(TimeOnly)) return TimeOnly.Parse(str, CultureInfo.InvariantCulture);
#endif
        }
        return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }
}
