#region Using Directives

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Shared bounded, staged, durable JSON persistence for replica checkpoints and journals.</summary>
internal static class ReplicaJsonFile
{
	#region Private Data

	private const long MaximumDocumentBytes = 128 * 1024 * 1024;

	#endregion

	#region Public Methods

	public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken, long maximumBytes = MaximumDocumentBytes)
		where T : class
	{
		cancellationToken.ThrowIfCancellationRequested();
		T? result = null;
		if (File.Exists(path))
		{
			await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (stream.Length > maximumBytes)
			{
				throw new InvalidDataException("The synchronization checkpoint exceeds its size limit.");
			}

			try
			{
				result = await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
					?? throw new InvalidDataException("The synchronization checkpoint is empty.");
			}
			catch (Exception exception) when (exception is JsonException or ArgumentException)
			{
				throw new InvalidDataException("The synchronization checkpoint is corrupt. It was retained for recovery.", exception);
			}
		}

		return result;
	}

	public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken, long maximumBytes = MaximumDocumentBytes)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string temporary = path + ".tmp";
		try
		{
			await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
				if (stream.Length > maximumBytes)
				{
					throw new InvalidDataException("The synchronization checkpoint exceeds its size limit.");
				}

				stream.Flush(flushToDisk: true);
			}

			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	#endregion
}
