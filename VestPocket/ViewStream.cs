using System.IO;

namespace VestPocket;

/// <summary>
/// Represents a stream that acts as a view of a subset of another stream
/// Based on a public forum post by Marc Gravell:
/// https://social.msdn.microsoft.com/Forums/vstudio/en-US/c409b63b-37df-40ca-9322-458ffe06ea48/how-to-access-part-of-a-filestream-or-memorystream?forum=netfxbcl
/// </summary>
internal class ViewStream : Stream
{
    private Stream baseStream;
    private long length;
    private long position;

    public ViewStream()
    {

    }

    public void SetStream(Stream baseStream, long offset, long length)
    {
        if (baseStream == null) throw new ArgumentNullException(nameof(baseStream));
        if (!baseStream.CanRead) throw new ArgumentException("Can't read base stream");
        if (!baseStream.CanSeek) throw new ArgumentException("Can't seek base stream");
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));

        this.baseStream = baseStream;
        this.length = length;
        if (baseStream.Position != offset)
        {
            baseStream.Seek(offset, SeekOrigin.Begin);
        }
        position = 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = Remaining;

        if (remaining <= 0) return 0;

        if (remaining < count)
        {
            count = (int)remaining;
        }

        int read = baseStream.Read(buffer, offset, count);

        position += read;

        return read;
    }

    public override long Length
    {
        get { return length; }
    }

    private long Remaining => length - position;

    public override bool CanRead => true;
    public override bool CanWrite
    {
        get { return false; }
    }
    public override bool CanSeek
    {
        get { return false; }
    }
    public override long Position
    {
        get
        {
            return position;
        }
        set { throw new NotSupportedException(); }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
    public override void Flush()
    {
        baseStream.Flush();
    }
    protected override void Dispose(bool disposing)
    {
        // We explicitly want this stream to be reusable
        // it doesn't need to dispose like a normal stream
        // since all resources are in the base stream
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
}
