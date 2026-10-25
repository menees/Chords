#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Applies a previously confirmed catalog through the book store's incremental, failure-safe transaction.</summary>
public static class LocalBookSyncApplier
{
	#region Public Methods

	public static Task ApplyAsync(
		IBookStore store,
		BookLocation location,
		string expectedJson,
		string targetJson,
		Func<SongFile, CancellationToken, Task<Stream>> openSource,
		string stagingDirectory,
		CancellationToken cancellationToken = default)
		=> ApplyCoreAsync(store, location, expectedJson, targetJson, openSource, stagingDirectory, false, cancellationToken);

	internal static Task ApplyVerifiedAsync(
		IBookStore store,
		BookLocation location,
		string expectedJson,
		string targetJson,
		Func<SongFile, CancellationToken, Task<Stream>> openSource,
		string stagingDirectory,
		CancellationToken cancellationToken)
		=> ApplyCoreAsync(store, location, expectedJson, targetJson, openSource, stagingDirectory, true, cancellationToken);

	private static async Task ApplyCoreAsync(
		IBookStore store,
		BookLocation location,
		string expectedJson,
		string targetJson,
		Func<SongFile, CancellationToken, Task<Stream>> openSource,
		string stagingDirectory,
		bool sourceVerified,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(openSource);
		ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
		cancellationToken.ThrowIfCancellationRequested();
		ChordDatabase original = DatabaseJson.Deserialize(expectedJson);
		ChordDatabase target = DatabaseJson.Deserialize(targetJson);
		if (original.Id != target.Id)
		{
			throw new ArgumentException("Synchronization cannot replace a book with a different identity.", nameof(targetJson));
		}

		Dictionary<Guid, SongFile> oldFiles = original.SongFiles.ToDictionary(file => file.Id);
		bool metadataOnly = oldFiles.Count == target.SongFiles.Count && target.SongFiles.All(file =>
			oldFiles.TryGetValue(file.Id, out SongFile? old) && old.SongId == file.SongId && old.RelativePath == file.RelativePath
			&& old.ContentHash == file.ContentHash && old.ContentRevision == file.ContentRevision && old.ObservedLength == file.ObservedLength);
		if (metadataOnly)
		{
			await store.CommitMetadataAsync(location, expectedJson, target, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			HashSet<Guid> retained = [];
			await using IStagedBookWrite transaction = await store.StageWriteAsync(location, expectedJson, cancellationToken).ConfigureAwait(false);
			transaction.VerifyOriginalAssets = true;
			foreach (SongFile file in target.SongFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				retained.Add(file.Id);
				if (oldFiles.TryGetValue(file.Id, out SongFile? old)
					&& !string.IsNullOrEmpty(file.ContentHash) && StringComparer.OrdinalIgnoreCase.Equals(old.ContentHash, file.ContentHash))
				{
					if (!StringComparer.Ordinal.Equals(old.RelativePath, file.RelativePath))
					{
						await transaction.RenameManagedAssetAsync(file.Id, file.RelativePath, cancellationToken).ConfigureAwait(false);
					}
				}
				else
				{
					await using Stream source = await openSource(file, cancellationToken).ConfigureAwait(false);
					long length = source.CanSeek ? source.Length - source.Position
						: file.ObservedLength ?? throw new InvalidDataException("The incoming sheet has no known length.");
					if (sourceVerified)
					{
						await transaction.WriteManagedAssetAsync(file.Id, file.RelativePath, source, cancellationToken).ConfigureAwait(false);
					}
					else
					{
						await using Stream staged = await VerifiedAssetStage.CreateAsync(
							source, file.ContentHash, length, stagingDirectory, cancellationToken).ConfigureAwait(false);
						await transaction.WriteManagedAssetAsync(file.Id, file.RelativePath, staged, cancellationToken).ConfigureAwait(false);
					}
				}
			}

			foreach (SongFile file in original.SongFiles)
			{
				if (!retained.Contains(file.Id))
				{
					await transaction.DeleteManagedAssetAsync(file.Id, cancellationToken).ConfigureAwait(false);
				}
			}

			await transaction.WriteDatabaseJsonAsync(targetJson, cancellationToken).ConfigureAwait(false);
			await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	#endregion
}
