using System.Text;
using DevOp.Toon.Core;

namespace DevOp.Toon.Tests;

[Collection("DefaultOptions")]
public sealed class DefaultUtf8PolicyTests
{
    [Fact]
    public async Task Decode_DefaultLegacyOptions_UsesSameUtf8PolicyForSyncAndAsync()
    {
        var original = ToonDecoder.DefaultOptions;
        try
        {
            ToonDecoder.DefaultOptions = new ToonDecodeOptions { ProtocolVersion = ToonProtocolVersion.CustomLegacy };
            byte[] bytes = [.. Encoding.UTF8.GetBytes("value: "), 0xff];
            using var sync = new MemoryStream(bytes);
            using var asyncStream = new MemoryStream(bytes);
            var syncValue = ToonDecoder.Decode(sync);
            var asyncValue = await ToonDecoder.DecodeAsync(asyncStream);
            Assert.Equal("\ufffd", syncValue!["value"]!.GetValue<string>());
            Assert.Equal(syncValue["value"]!.GetValue<string>(), asyncValue!["value"]!.GetValue<string>());
        }
        finally { ToonDecoder.DefaultOptions = original; }
    }
}
