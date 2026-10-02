namespace AI.TextCorrection;

using System.Diagnostics;

/// <summary>Bounds each async read and lets a single-threaded browser handle input between parsing batches.</summary>
public sealed class CooperativeReadStream(Stream source) : Stream
{
    private readonly Stopwatch _slice = Stopwatch.StartNew();
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, Math.Min(count, 16384));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_slice.ElapsedMilliseconds >= 4)
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            _slice.Restart();
        }
        return await source.ReadAsync(buffer[..Math.Min(buffer.Length, 16384)], cancellationToken).ConfigureAwait(false);
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
