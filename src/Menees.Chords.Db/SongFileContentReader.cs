using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Reads bounded song text or a streamed PDF once for analysis and optional integrity hashing.</summary>
internal static class SongFileContentReader
{
	internal static async Task<(string? Hash, SongFileAnalysis? Analysis, string? Problem)> ReadAsync(
		Stream source, string name, bool computeHash, CancellationToken cancellationToken)
	{
		const int BufferSize = 81920;
		const int PdfHeaderLength = 5;
		const int MaximumTextBytes = 16 * 1024 * 1024;
		byte[] prefix = new byte[PdfHeaderLength];
		int length = await source.ReadAtLeastAsync(prefix, prefix.Length, false, cancellationToken).ConfigureAwait(false);
		bool pdf = prefix.AsSpan(0, length).StartsWith("%PDF-"u8);
		using IncrementalHash? hash = computeHash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
		using MemoryStream? text = pdf ? null : new();
		hash?.AppendData(prefix, 0, length);
		text?.Write(prefix, 0, length);
		bool tooLarge = false;
		if (!pdf || computeHash)
		{
			byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
			try
			{
				int count;
				while ((count = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
				{
					hash?.AppendData(buffer, 0, count);
					if (text is not null && !tooLarge)
					{
						tooLarge = count > MaximumTextBytes - text.Length;
						if (!tooLarge)
						{
							text.Write(buffer, 0, count);
						}
						else if (!computeHash)
						{
							break;
						}
					}
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		string? contentHash = hash is null ? null : Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
		SongFileAnalysis? analysis = null;
		string? problem = tooLarge ? "Song text exceeds the 16 MB analysis limit. PDF files are streamed separately." : null;
		if (problem is null)
		{
			try
			{
				Memory<byte> bytes = pdf ? prefix.AsMemory(0, length) : text!.GetBuffer().AsMemory(0, checked((int)text.Length));
				analysis = SongFileAnalyzer.Analyze(bytes, name);
			}
			catch (Exception exception) when (exception is BookStoreValidationException or DecoderFallbackException)
			{
				problem = exception.Message;
			}
		}

		return (contentHash, analysis, problem);
	}
}
