using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync;

/// <summary>Streams exact, hash-verified bytes into temporary storage before any live replica mutation.</summary>
public static class VerifiedAssetStage
{
	public static async Task<Stream> CreateAsync(
		Stream source, string contentHash, long expectedLength, string stagingDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
		ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
		if (contentHash is null || contentHash.Length != SHA256.HashSizeInBytes * 2 || !contentHash.All(Uri.IsHexDigit))
		{
			throw new ArgumentException("A SHA-256 content hash is required.", nameof(contentHash));
		}

		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(stagingDirectory);
		const int BufferSize = 81920;
		string path = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".stage");
		FileStream output = new(
			path,
			FileMode.CreateNew,
			FileAccess.ReadWrite,
			FileShare.None,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
		try
		{
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
			long length = 0;
			try
			{
				int count;
				while ((count = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
				{
					if (count > expectedLength - length)
					{
						throw new InvalidDataException("The sheet length changed during synchronization. Neither live copy was replaced.");
					}

					hash.AppendData(buffer, 0, count);
					await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
					length += count;
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}

			if (length != expectedLength || !Convert.ToHexString(hash.GetHashAndReset()).Equals(contentHash, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The sheet failed synchronization integrity verification. Neither live copy was replaced.");
			}

			await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			output.Position = 0;
		}
		catch
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw;
		}

		return output;
	}
}
