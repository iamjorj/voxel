using System.IO;
using System.Text;

namespace Voxel.Setup;

/// <summary>
/// The app to install travels as a zip appended to the end of VoxelSetup.exe:
/// [setup exe][zip][zip length: 8 bytes][magic "VOXELPAY": 8 bytes].
/// Appended rather than embedded so the setup starts instantly: .NET never has to load or
/// unpack 200 MB of resources, the zip is just read from the file when installing.
/// </summary>
internal static class Payload
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VOXELPAY");

    /// <summary>The zip inside this exe, or the file given with --payload (for testing an unbundled build); null if none.</summary>
    public static Stream? Open(string[] args)
    {
        var i = Array.IndexOf(args, "--payload");
        if (i >= 0 && i + 1 < args.Length) return File.OpenRead(args[i + 1]);

        var file = File.OpenRead(Environment.ProcessPath!);
        var trailer = new byte[16];
        if (file.Length > trailer.Length)
        {
            file.Seek(-trailer.Length, SeekOrigin.End);
            file.ReadExactly(trailer);
            var length = BitConverter.ToInt64(trailer, 0);
            if (trailer.AsSpan(8).SequenceEqual(Magic) && length > 0 && length <= file.Length - trailer.Length)
                return new Slice(file, file.Length - trailer.Length - length, length);
        }
        file.Dispose();
        return null;
    }

    /// <summary>Read-only, seekable view of part of a file (zip reading needs to seek).</summary>
    private sealed class Slice(Stream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = Math.Clamp(value, 0, length); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, length - _position);
            if (n <= 0) return 0;
            inner.Position = start + _position;
            n = inner.Read(buffer[..n]);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
