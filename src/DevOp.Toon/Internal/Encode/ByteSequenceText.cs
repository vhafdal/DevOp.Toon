#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DevOp.Toon.Internal.Encode;

internal static class ByteSequenceText
{
    public static bool TryToBase64String(object? value, out string? base64)
    {
        switch (value)
        {
            case byte[] bytes:
                base64 = Convert.ToBase64String(bytes);
                return true;
            case List<byte> list:
                return TryToBase64String(list, out base64);
            case IEnumerable<byte> sequence:
                return TryToBase64String(sequence, out base64);
            default:
                base64 = null;
                return false;
        }
    }

    private static bool TryToBase64String(List<byte> list, out string? base64)
    {
#if NET5_0_OR_GREATER
        base64 = Convert.ToBase64String(CollectionsMarshal.AsSpan(list));
        return true;
#else
        base64 = Convert.ToBase64String(list.ToArray());
        return true;
#endif
    }

    private static bool TryToBase64String(IEnumerable<byte> sequence, out string? base64)
    {
        if (sequence is byte[] bytes)
        {
            base64 = Convert.ToBase64String(bytes);
            return true;
        }

        if (sequence is List<byte> list)
        {
            return TryToBase64String(list, out base64);
        }

        const int InitialBufferSize = 256;
        byte[] rented = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        int count = 0;

        try
        {
            foreach (var item in sequence)
            {
                if (count == rented.Length)
                {
                    byte[] larger = ArrayPool<byte>.Shared.Rent(rented.Length * 2);
                    Buffer.BlockCopy(rented, 0, larger, 0, count);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = larger;
                }

                rented[count++] = item;
            }

            base64 = Convert.ToBase64String(rented, 0, count);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
