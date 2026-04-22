#nullable enable
using System.Collections.Concurrent;
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Decode
{
    /// <summary>
    /// Coordinates typed TOON decoding by trying the direct materializer first and falling back to native-node materialization.
    /// </summary>
    internal static class TypedDecoder
    {
        /// <summary>
        /// Tracks direct materializer misses by target type so performance regressions can be observed in tests or profiling.
        /// </summary>
        internal static readonly ConcurrentDictionary<string, int> DirectMaterializerMisses = new();

        /// <summary>
        /// Scans and decodes TOON text into the requested CLR type.
        /// </summary>
        /// <typeparam name="T">The target CLR type.</typeparam>
        /// <param name="toonString">The source TOON payload.</param>
        /// <param name="options">Resolved decode options.</param>
        /// <returns>The materialized value.</returns>
        public static T? Decode<T>(string toonString, ResolvedDecodeOptions options)
        {
            var scanResult = Scanner.ToParsedLines(toonString, options.Indent, options.Strict);
            return Decode<T>(scanResult, options);
        }

        /// <summary>
        /// Decodes an existing scan result into the requested CLR type.
        /// </summary>
        /// <typeparam name="T">The target CLR type.</typeparam>
        /// <param name="scanResult">Pre-scanned TOON lines.</param>
        /// <param name="options">Resolved decode options.</param>
        /// <returns>The materialized value.</returns>
        public static T? Decode<T>(ScanResult scanResult, ResolvedDecodeOptions options)
        {
            if (scanResult.Lines.Count == 0)
            {
                if (NativeTypedMaterializer.TryConvert<T>(new NativeObjectNode(), out var emptyValue))
                {
                    return emptyValue;
                }

                throw CreateUnsupportedTargetTypeException(typeof(T));
            }

            var cursor = new LineCursor(scanResult.Lines, scanResult.BlankLines);
            if (DirectMaterializer.TryDecode<T>(cursor, options, out var typedValue))
            {
                return typedValue;
            }

            DirectMaterializerMisses.AddOrUpdate(typeof(T).FullName ?? typeof(T).Name, 1, static (_, n) => n + 1);

            cursor = new LineCursor(scanResult.Lines, scanResult.BlankLines);
            var nativeResult = NativeDecoders.DecodeValueFromLines(cursor, options);
            if (NativeTypedMaterializer.TryConvert<T>(nativeResult, out typedValue))
            {
                return typedValue;
            }

            throw CreateUnsupportedTargetTypeException(typeof(T));
        }

        private static NotSupportedException CreateUnsupportedTargetTypeException(Type targetType)
        {
            return new NotSupportedException($"TOON typed decode does not support target type '{targetType.FullName}'.");
        }
    }
}
