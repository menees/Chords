using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync.OneDrive;

/// <summary>Streams the caller's remaining content without taking ownership of its stream.</summary>
internal sealed class OneDriveStreamContent(Stream source, long length) : HttpContent
{
	protected override bool TryComputeLength(out long computedLength)
	{
		computedLength = length;
		return true;
	}

	protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => source.CopyToAsync(stream);

	protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
		=> source.CopyToAsync(stream, cancellationToken);
}
