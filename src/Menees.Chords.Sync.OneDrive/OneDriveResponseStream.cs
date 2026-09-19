#region Using Directives

using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync.OneDrive;

/// <summary>Transfers response ownership to the consumer of a streaming download.</summary>
internal sealed class OneDriveResponseStream(Stream inner, HttpResponseMessage response) : Stream
{
	#region Members

	public override bool CanRead => inner.CanRead;

	public override bool CanSeek => inner.CanSeek;

	public override bool CanWrite => false;

	public override long Length => inner.Length;

	public override long Position { get => inner.Position; set => inner.Position = value; }

	public override void Flush() => throw new NotSupportedException();

	public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

	public override int Read(Span<byte> buffer) => inner.Read(buffer);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		=> inner.ReadAsync(buffer, cancellationToken);

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		=> inner.ReadAsync(buffer, offset, count, cancellationToken);

	public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			response.Dispose();
		}

		base.Dispose(disposing);
	}

	#endregion
}
