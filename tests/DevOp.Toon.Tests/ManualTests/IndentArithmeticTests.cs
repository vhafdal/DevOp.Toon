using System.Reflection;

namespace DevOp.Toon.Tests;

public sealed class IndentArithmeticTests
{
    [Fact]
    public void StartLine_IndentMultiplicationOverflows_ThrowsBeforeRentingLargeBuffer()
    {
        var type = typeof(ToonEncoder).Assembly.GetType("DevOp.Toon.Internal.Encode.CompactBufferWriter")!;
        using var writer = (IDisposable)Activator.CreateInstance(type, 2, 256, "\n")!;
        var start = type.GetMethod("StartLine", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => start.Invoke(writer, [int.MaxValue]));
        Assert.IsType<OverflowException>(error.InnerException);
    }
}
