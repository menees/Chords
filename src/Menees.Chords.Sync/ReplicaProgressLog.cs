#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Fixed-size completion records avoid rewriting the confirmed plan after each asset.</summary>
internal static class ReplicaProgressLog
{
	#region Public Methods

	public static async Task AppendAsync(string path, string operationId, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		byte[] record = SHA256.HashData(Encoding.UTF8.GetBytes(operationId));
		await using FileStream output = new(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

		// A power loss can leave a partial last record. Only complete, flushed records acknowledge progress.
		long completeLength = output.Length - (output.Length % SHA256.HashSizeInBytes);
		output.SetLength(completeLength);
		output.Position = completeLength;
		await output.WriteAsync(record, cancellationToken).ConfigureAwait(false);
		output.Flush(flushToDisk: true);
	}

	public static async Task ReadAsync(string path, SyncOperationJournal journal, CancellationToken cancellationToken)
	{
		if (File.Exists(path))
		{
			Dictionary<string, string> ids = journal.ConfirmedPlan.Operations.ToDictionary(
				operation => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Id))), operation => operation.Id, StringComparer.Ordinal);
			await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			const long MaximumLength = 32 * 1024 * 1024;
			if (input.Length > MaximumLength)
			{
				throw new InvalidDataException("The synchronization progress log exceeds its size limit.");
			}

			byte[] record = new byte[SHA256.HashSizeInBytes];
			while (await input.ReadAtLeastAsync(record, record.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == record.Length)
			{
				if (!ids.TryGetValue(Convert.ToHexString(record), out string? id))
				{
					throw new InvalidDataException("The synchronization progress log contains an unknown completion record.");
				}

				journal.MarkCompleted(id);
			}
		}
	}

	#endregion
}
