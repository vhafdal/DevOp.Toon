using System.Reflection;

namespace DevOp.Toon.Benchmarks;

internal sealed class DecodeReflectionHarness<T>
{
    private readonly object resolvedOptions;
    private readonly int indent;
    private readonly bool strict;
    private readonly MethodInfo scanMethod;
    private readonly ConstructorInfo lineCursorConstructor;
    private readonly PropertyInfo scanLinesProperty;
    private readonly PropertyInfo scanBlankLinesProperty;
    private readonly MethodInfo directDecodeMethod;
    private readonly MethodInfo typedDecodeMethod;

    private DecodeReflectionHarness(
        object resolvedOptions,
        int indent,
        bool strict,
        MethodInfo scanMethod,
        ConstructorInfo lineCursorConstructor,
        PropertyInfo scanLinesProperty,
        PropertyInfo scanBlankLinesProperty,
        MethodInfo directDecodeMethod,
        MethodInfo typedDecodeMethod)
    {
        this.resolvedOptions = resolvedOptions;
        this.indent = indent;
        this.strict = strict;
        this.scanMethod = scanMethod;
        this.lineCursorConstructor = lineCursorConstructor;
        this.scanLinesProperty = scanLinesProperty;
        this.scanBlankLinesProperty = scanBlankLinesProperty;
        this.directDecodeMethod = directDecodeMethod;
        this.typedDecodeMethod = typedDecodeMethod;
    }

    public static DecodeReflectionHarness<T> Create(ToonDecodeOptions decodeOptions)
    {
        var assembly = typeof(ToonDecoder).Assembly;
        var scannerType = assembly.GetType("DevOp.Toon.Internal.Decode.Scanner")
            ?? throw new InvalidOperationException("Scanner type was not found.");
        var scanResultType = assembly.GetType("DevOp.Toon.Internal.Decode.ScanResult")
            ?? throw new InvalidOperationException("ScanResult type was not found.");
        var lineCursorType = assembly.GetType("DevOp.Toon.Internal.Decode.LineCursor")
            ?? throw new InvalidOperationException("LineCursor type was not found.");
        var parsedLineType = assembly.GetType("DevOp.Toon.Internal.Decode.ParsedLine")
            ?? throw new InvalidOperationException("ParsedLine type was not found.");
        var blankLineInfoType = assembly.GetType("DevOp.Toon.Internal.Decode.BlankLineInfo")
            ?? throw new InvalidOperationException("BlankLineInfo type was not found.");
        var resolvedOptionsType = assembly.GetType("DevOp.Toon.Internal.Decode.ResolvedDecodeOptions")
            ?? throw new InvalidOperationException("ResolvedDecodeOptions type was not found.");
        var directMaterializerType = assembly.GetType("DevOp.Toon.Internal.Decode.DirectMaterializer")
            ?? throw new InvalidOperationException("DirectMaterializer type was not found.");
        var typedDecoderType = assembly.GetType("DevOp.Toon.Internal.Decode.TypedDecoder")
            ?? throw new InvalidOperationException("TypedDecoder type was not found.");

        var scanMethod = scannerType.GetMethod("ToParsedLines", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scanner.ToParsedLines was not found.");
        var lineCursorConstructor = lineCursorType.GetConstructor(new[]
            {
                typeof(List<>).MakeGenericType(parsedLineType),
                typeof(List<>).MakeGenericType(blankLineInfoType)
            })
            ?? throw new InvalidOperationException("LineCursor constructor was not found.");
        var scanLinesProperty = scanResultType.GetProperty("Lines", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ScanResult.Lines was not found.");
        var scanBlankLinesProperty = scanResultType.GetProperty("BlankLines", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ScanResult.BlankLines was not found.");

        var resolvedOptions = Activator.CreateInstance(resolvedOptionsType)
            ?? throw new InvalidOperationException("ResolvedDecodeOptions could not be created.");
        resolvedOptionsType.GetProperty("Indent")!.SetValue(resolvedOptions, decodeOptions.Indent);
        resolvedOptionsType.GetProperty("Strict")!.SetValue(resolvedOptions, decodeOptions.Strict);
        resolvedOptionsType.GetProperty("ExpandPaths")!.SetValue(resolvedOptions, decodeOptions.ExpandPaths);

        var directDecodeMethod = directMaterializerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method =>
                method.Name == "TryDecode"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 3)
            .MakeGenericMethod(typeof(T));

        var typedDecodeMethod = typedDecoderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method =>
                method.Name == "Decode"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2
                && method.GetParameters()[0].ParameterType == typeof(string))
            .MakeGenericMethod(typeof(T));

        return new DecodeReflectionHarness<T>(
            resolvedOptions,
            decodeOptions.Indent,
            decodeOptions.Strict,
            scanMethod,
            lineCursorConstructor,
            scanLinesProperty,
            scanBlankLinesProperty,
            directDecodeMethod,
            typedDecodeMethod);
    }

    public object Scan(string toonText)
    {
        return scanMethod.Invoke(null, [toonText, indent, strict])
            ?? throw new InvalidOperationException("Scanner.ToParsedLines returned null.");
    }

    public bool DirectDecode(object scanResult)
    {
        var cursor = CreateCursor(scanResult);
        var args = new object?[] { cursor, resolvedOptions, null };
        return (bool)(directDecodeMethod.Invoke(null, args) ?? false);
    }

    public void TypedDecode(string toonText)
    {
        _ = typedDecodeMethod.Invoke(null, [toonText, resolvedOptions]);
    }

    private object CreateCursor(object scanResult)
    {
        var lines = scanLinesProperty.GetValue(scanResult)
            ?? throw new InvalidOperationException("ScanResult.Lines returned null.");
        var blankLines = scanBlankLinesProperty.GetValue(scanResult)
            ?? throw new InvalidOperationException("ScanResult.BlankLines returned null.");
        return lineCursorConstructor.Invoke([lines, blankLines]);
    }
}
