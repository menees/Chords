#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

public sealed partial class BookApplicationSession
{
	#region Public API

	/// <summary>Lists sheets in display order without opening their content.</summary>
	public IReadOnlyList<SongFileCatalogItem> GetSongFiles(Guid songId)
	{
		SongFile[] files = [.. this.GetOrderedSongFiles(songId)];
		Guid? defaultId = files.FirstOrDefault(file => !file.IsArchived)?.Id;
		return [.. files.Select(file => new SongFileCatalogItem(
			file.Id, file.RelativePath, file.MediaKind, file.SourceFormat, file.IsArchived, file.RecoveryVersion is not null, file.Id == defaultId))];
	}

	/// <summary>Adds related local sheets in one byte-preserving import transaction.</summary>
	public async Task<int> ImportSongFilesAsync(Guid songId, IReadOnlyList<string> sourcePaths, Guid deviceId, CancellationToken cancellationToken = default)
	{
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			IReadOnlyList<BookImportResult> results = await BookImportService.ImportSongFilesAsync(
				activeStore, activeLocation, songId, sourcePaths, deviceId, cancellationToken).ConfigureAwait(false);
			await this.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
			return results.Count;
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

	/// <summary>Moves a sheet to a one-based display position using only a metadata commit.</summary>
	public Task SetSongFilePositionAsync(Guid songId, Guid fileId, int position, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				List<SongFile> files = [.. this.GetOrderedSongFiles(songId)];
				ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
				ArgumentOutOfRangeException.ThrowIfGreaterThan(position, files.Count);
				SongFile moved = files.Single(file => file.Id == fileId);
				files.Remove(moved);
				files.Insert(position - 1, moved);
				for (int index = 0; index < files.Count; index++)
				{
					SongFile file = files[index];
					int priority = files.Count - index;
					if (file.DisplayPriority != priority)
					{
						file.DisplayPriority = priority;
						file.Revision = NextRevision(file.Revision, deviceId, now);
					}
				}
			},
			deviceId,
			cancellationToken);

	/// <summary>Archives or restores a sheet without changing its bytes or explicit references.</summary>
	public Task SetSongFileArchivedAsync(Guid songId, Guid fileId, bool archived, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				SongFile file = database.SongFiles.Single(item => item.SongId == songId && item.Id == fileId);
				file.IsArchived = archived;
				file.Revision = NextRevision(file.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken,
			refreshSongs: true);

	/// <summary>Renames a sheet while retaining its permanent ID, extension, and source bytes.</summary>
	public Task RenameSongFileAsync(Guid songId, Guid fileId, string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return this.MutateAssetsAsync(
			async (database, write, now) =>
			{
				SongFile file = database.SongFiles.Single(item => item.SongId == songId && item.Id == fileId);
				string path = PortableManagedFileName.Create(name, file.Id, Path.GetExtension(file.RelativePath));
				await write.RenameManagedAssetAsync(file.Id, path, cancellationToken).ConfigureAwait(false);
				file.RelativePath = path;
				file.Revision = NextRevision(file.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);
	}

	/// <summary>Deletes an archived sheet and clears explicit preferences that referenced it.</summary>
	public Task DeleteArchivedSongFileAsync(Guid songId, Guid fileId, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateAssetsAsync(
			async (database, write, now) =>
			{
				SongFile file = database.SongFiles.Single(item => item.SongId == songId && item.Id == fileId);
				if (!file.IsArchived)
				{
					throw new InvalidOperationException("Archive the sheet before permanently deleting it.");
				}

				await write.DeleteManagedAssetAsync(file.Id, cancellationToken).ConfigureAwait(false);
				database.SongFiles.Remove(file);
				AddTombstone(database, file.Id, nameof(SongFile), file.Revision, deviceId, now);
				foreach (SongInstrumentSetting setting in database.SongInstrumentSettings.Where(item => item.PreferredSongFileId == fileId))
				{
					setting.PreferredSongFileId = null;
					setting.Revision = NextRevision(setting.Revision, deviceId, now);
				}

				foreach (Setlist setlist in database.Setlists)
				{
					bool changed = false;
					foreach (SetlistEntry entry in setlist.Entries.Where(item => item.PreferredSongFileId == fileId))
					{
						entry.PreferredSongFileId = null;
						changed = true;
					}

					if (changed)
					{
						setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
					}
				}
			},
			deviceId,
			cancellationToken);

	#endregion

	#region Private Methods

	private IOrderedEnumerable<SongFile> GetOrderedSongFiles(Guid songId)
		=> (this.Database ?? throw new InvalidOperationException("No book is open.")).SongFiles
			.Where(file => file.SongId == songId)
			.OrderByDescending(file => file.DisplayPriority).ThenBy(file => file.MediaKind).ThenBy(file => file.Id);

	private async Task MutateAssetsAsync(
		Func<ChordDatabase, IStagedBookWrite, DateTimeOffset, Task> mutation,
		Guid deviceId,
		CancellationToken cancellationToken)
	{
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		string? expectedJson = this.committedJson;
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			ChordDatabase database = this.Database!;
			DateTimeOffset now = DateTimeOffset.UtcNow;
			await using IStagedBookWrite write = await activeStore.StageWriteAsync(activeLocation, expectedJson!, cancellationToken).ConfigureAwait(false);
			await mutation(database, write, now).ConfigureAwait(false);
			database.Revision = NextRevision(database.Revision, deviceId, now);
			string updatedJson = DatabaseJson.Serialize(database);
			await write.WriteDatabaseJsonAsync(updatedJson, cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
			this.SetDatabase(database);
			this.committedJson = updatedJson;
		}
		catch
		{
			if (expectedJson is not null)
			{
				this.SetDatabase(DatabaseJson.Deserialize(expectedJson));
			}

			throw;
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

	#endregion
}
