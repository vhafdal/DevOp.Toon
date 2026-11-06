using System.Text;

namespace DevOp.Toon.Tests;

public sealed class StreamUtf8BufferTests
{
    [Theory]
    [InlineData(ToonProtocolVersion.V3_0)]
    [InlineData(ToonProtocolVersion.CustomLegacy)]
    [InlineData(ToonProtocolVersion.V4_4)]
    [InlineData(ToonProtocolVersion.CustomV4_4)]
    public async Task EncodeToStream_UnicodeAroundBufferBoundaries_MatchesBytesAndWritesOnce(ToonProtocolVersion version)
    {
        foreach (var length in new[] { 4095, 4096, 8191, 8192, 65535, 65536 })
        {
            var rows = new[] { new Row { Value = new string('a', length) + "😀Þ汉\uffff" }, new Row { Value = "end😀" } };
            var options = Options(version);
            var expected = ToonEncoder.EncodeToBytes(rows, options);
            using var sync = new RecordingStream();
            ToonEncoder.EncodeToStream(rows, sync, options);
            Assert.Equal(expected, sync.Bytes);
            Assert.Equal(1, sync.Writes);
            Assert.False(sync.Closed);
            using var asyncStream = new RecordingStream();
            using var cancellation = new CancellationTokenSource();
            await ToonEncoder.EncodeToStreamAsync(rows, asyncStream, options, cancellation.Token);
            Assert.Equal(expected, asyncStream.Bytes);
            Assert.Equal(1, asyncStream.Writes);
            Assert.Equal(cancellation.Token, asyncStream.Token);
            Assert.False(asyncStream.Closed);
            Assert.False(expected.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        }
    }

    [Fact]
    public async Task EncodeToStreamAsync_PreCancelled_DoesNotWrite()
    {
        using var stream = new RecordingStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ToonEncoder.EncodeToStreamAsync(new[] { new Row { Value = "😀" } }, stream, Options(ToonProtocolVersion.V4_4), cancellation.Token));
        Assert.Equal(0, stream.Writes);
    }

    [Fact]
    public async Task EncodeToStream_DestinationFailure_PropagatesAndLeavesDestinationOpen()
    {
        var rows = new[] { new Row { Value = new string('汉', 8192) + "😀" } };
        using var sync = new RecordingStream { ThrowOnWrite = true };
        Assert.Throws<IOException>(() => ToonEncoder.EncodeToStream(rows, sync, Options(ToonProtocolVersion.V4_4)));
        Assert.False(sync.Closed);
        using var asyncStream = new RecordingStream { ThrowOnWrite = true };
        await Assert.ThrowsAsync<IOException>(() => ToonEncoder.EncodeToStreamAsync(rows, asyncStream, Options(ToonProtocolVersion.V4_4)));
        Assert.False(asyncStream.Closed);
    }

    [Fact]
    public async Task EncodeToStream_LegacyUnpairedSurrogate_PreservesExistingReplacementPolicy()
    {
        var rows = new[] { new Row { Value = "a\ud800b" } };
        var options = Options(ToonProtocolVersion.CustomLegacy);
        var expected = ToonEncoder.EncodeToBytes(rows, options);
        using var stream = new RecordingStream();
        await ToonEncoder.EncodeToStreamAsync(rows, stream, options);
        Assert.Equal(expected, stream.Bytes);
    }

    private static ToonEncodeOptions Options(ToonProtocolVersion version) => new()
    {
        ProtocolVersion = version, Indent = 2, IgnoreNullOrEmpty = false, ExcludeEmptyArrays = false
    };

    public sealed class Row { public string Value { get; set; } = ""; }

    private sealed class RecordingStream : Stream
    {
        private readonly MemoryStream storage = new();
        internal byte[] Bytes => storage.ToArray();
        internal int Writes { get; private set; }
        internal bool Closed { get; private set; }
        internal bool ThrowOnWrite { get; init; }
        internal CancellationToken Token { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => storage.Length;
        public override long Position { get => storage.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (ThrowOnWrite) throw new IOException("destination");
            Writes++;
            storage.Write(buffer, offset, count);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnWrite) throw new IOException("destination");
            Writes++;
            storage.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        protected override void Dispose(bool disposing) { Closed = true; if (disposing) storage.Dispose(); base.Dispose(disposing); }
    }
}
