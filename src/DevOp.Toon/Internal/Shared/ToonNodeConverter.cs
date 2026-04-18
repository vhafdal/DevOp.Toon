#nullable enable
using DevOp.Toon.Internal.Encode;

namespace DevOp.Toon.Internal.Shared;

internal static class ToonNodeConverter
{
    public static ToonNode? ToToonNode(NativeNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case NativePrimitiveNode primitive:
                return primitive.Value is null ? null : new ToonValue(primitive.Value);
            case NativeArrayNode array:
                var toonArray = new ToonArray();
                foreach (var item in array)
                {
                    toonArray.Add(ToToonNode(item));
                }

                return toonArray;
            case NativeObjectNode obj:
                var toonObject = new ToonObject();
                foreach (var kvp in obj)
                {
                    toonObject[kvp.Key] = ToToonNode(kvp.Value);
                }

                return toonObject;
            default:
                return null;
        }
    }

    public static NativeNode? ToNativeNode(ToonNode? node)
    {
        switch (node)
        {
            case null:
                return new NativePrimitiveNode(null);
            case ToonValue value:
                return new NativePrimitiveNode(value.Value);
            case ToonArray array:
                var nativeArray = new NativeArrayNode();
                foreach (var item in array)
                {
                    nativeArray.Add(ToNativeNode(item));
                }

                return nativeArray;
            case ToonObject obj:
                var nativeObject = new NativeObjectNode();
                foreach (var kvp in obj)
                {
                    nativeObject.Add(kvp.Key, ToNativeNode(kvp.Value));
                }

                return nativeObject;
            default:
                return new NativePrimitiveNode(null);
        }
    }
}
