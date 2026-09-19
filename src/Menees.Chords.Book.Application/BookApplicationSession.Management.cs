#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application;

public sealed partial class BookApplicationSession
{
	#region Public API

	/// <summary>Creates a song through the shared atomic import pipeline, then refreshes the active catalog.</summary>
	public Task<Guid> CreateSongAsync(
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string text,
		Guid deviceId,
		CancellationToken cancellationToken = default)
		=> this.CreateSongAsync(new SongEditMetadata(title, artists, tags), text, deviceId, cancellationToken);

	/// <summary>Creates text and explicitly edited catalog metadata in a single import transaction.</summary>
	public async Task<Guid> CreateSongAsync(
		SongEditMetadata metadata, string text, Guid deviceId, CancellationToken cancellationToken = default)
	{
		SongMetadataValidation.Validate(metadata.Scalars);
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			BookImportResult result = await BookImportService.CreateSongAsync(
				activeStore, activeLocation, metadata.Title, metadata.Artists, metadata.Tags, text, deviceId, metadata.Scalars, cancellationToken)
				.ConfigureAwait(false);
			await this.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
			return result.SongId;
		}
		finally
		{
			this.mutationLock.Release();
		}
	}

	/// <summary>Moves an occurrence to an absolute one-based position, preserving every entry identity.</summary>
	public Task SetSetlistEntryPositionAsync(Guid setlistId, Guid entryId, int position, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				if (position < 1 || position > setlist.Entries.Count)
				{
					throw new ArgumentOutOfRangeException(nameof(position), $"Enter a position from 1 to {setlist.Entries.Count}.");
				}

				SetlistEntry entry = setlist.Entries.Single(item => item.Id == entryId);
				setlist.Entries.Remove(entry);
				setlist.Entries.Insert(position - 1, entry);
				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken,
			refreshSongs: false);

	/// <summary>Saves a permutation of the existing entry IDs after native drag-and-drop reordering.</summary>
	public Task SetSetlistOrderAsync(Guid setlistId, IReadOnlyList<Guid> entryIds, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entryIds);
		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				Dictionary<Guid, SetlistEntry> entries = setlist.Entries.ToDictionary(entry => entry.Id);
				if (entryIds.Count != entries.Count || entryIds.Distinct().Count() != entries.Count || !entryIds.ToHashSet().SetEquals(entries.Keys))
				{
					throw new InvalidOperationException("The setlist entries changed. Reopen the setlist before reordering it.");
				}

				setlist.Entries.Clear();
				setlist.Entries.AddRange(entryIds.Select(id => entries[id]));
				setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken,
			refreshSongs: false);
	}

	/// <summary>Archives or restores a selection of songs without modifying their files or setlist occurrences.</summary>
	public Task SetSongsArchivedAsync(IReadOnlyList<Guid> songIds, bool archived, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		return this.MutateMetadataAsync(
			(database, now) =>
			{
				Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
				foreach (Guid id in songIds.Distinct())
				{
					Song song = songs[id];
					song.IsArchived = archived;
					song.Revision = NextRevision(song.Revision, deviceId, now);
				}
			},
			deviceId,
			cancellationToken,
			refreshSongs: true);
	}

	/// <summary>Permanently deletes an archived setlist, retaining a synchronization tombstone.</summary>
	public Task DeleteArchivedSetlistAsync(Guid setlistId, Guid deviceId, CancellationToken cancellationToken = default)
		=> this.MutateMetadataAsync(
			(database, now) =>
			{
				Setlist setlist = database.Setlists.Single(item => item.Id == setlistId);
				if (!setlist.IsArchived)
				{
					throw new InvalidOperationException("Archive the setlist before permanently deleting it.");
				}

				database.Setlists.Remove(setlist);
				AddTombstone(database, setlist.Id, nameof(Setlist), setlist.Revision, deviceId, now);
			},
			deviceId,
			cancellationToken);

	/// <summary>Deletes only archived songs, their managed files, and all references in one staged commit.</summary>
	public async Task DeleteArchivedSongsAsync(IReadOnlyList<Guid> songIds, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(songIds);
		HashSet<Guid> ids = [.. songIds];
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		string expectedJson = this.committedJson!;
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			ChordDatabase database = this.Database!;
			Dictionary<Guid, Song> songsById = database.Songs.ToDictionary(song => song.Id);
			Song[] songs = [.. ids.Select(id => songsById[id])];
			if (songs.Any(song => !song.IsArchived))
			{
				throw new InvalidOperationException("Every selected song must be archived before permanent deletion.");
			}

			DateTimeOffset now = DateTimeOffset.UtcNow;
			await using IStagedBookWrite write = await activeStore.StageWriteAsync(activeLocation, expectedJson, cancellationToken).ConfigureAwait(false);
			foreach (SongFile file in database.SongFiles.Where(file => ids.Contains(file.SongId)))
			{
				await write.DeleteManagedAssetAsync(file.Id, cancellationToken).ConfigureAwait(false);
				AddTombstone(database, file.Id, nameof(SongFile), file.Revision, deviceId, now);
			}

			foreach (SongInstrumentSetting setting in database.SongInstrumentSettings.Where(item => ids.Contains(item.SongId)))
			{
				AddTombstone(database, setting.Id, nameof(SongInstrumentSetting), setting.Revision, deviceId, now);
			}

			foreach (Setlist setlist in database.Setlists)
			{
				if (setlist.Entries.RemoveAll(entry => ids.Contains(entry.SongId)) > 0)
				{
					setlist.Revision = NextRevision(setlist.Revision, deviceId, now);
				}
			}

			foreach (Song song in songs)
			{
				AddTombstone(database, song.Id, nameof(Song), song.Revision, deviceId, now);
			}

			database.SongFiles.RemoveAll(file => ids.Contains(file.SongId));
			database.SongInstrumentSettings.RemoveAll(setting => ids.Contains(setting.SongId));
			database.Songs.RemoveAll(song => ids.Contains(song.Id));
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

	/// <summary>Loads song metadata and an optional editable text file with an optimistic concurrency revision.</summary>
	public Task<SongEditDocument> GetSongEditAsync(Guid songId, CancellationToken cancellationToken = default)
		=> this.GetSongEditCoreAsync(songId, null, cancellationToken);

	/// <summary>Loads a selected active sheet without changing the song's default display order.</summary>
	public Task<SongEditDocument> GetSongFileEditAsync(Guid songId, Guid fileId, CancellationToken cancellationToken = default)
		=> this.GetSongEditCoreAsync(songId, fileId, cancellationToken);

	/// <summary>Saves explicit catalog edits and changed text together, refusing to overwrite a newer edit.</summary>
	public Task SaveSongEditAsync(
		SongEditDocument original,
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string? text,
		Guid deviceId,
		CancellationToken cancellationToken = default)
		=> this.SaveSongEditAsync(original, new SongEditMetadata(title, artists, tags), text, deviceId, cancellationToken);

	/// <summary>Saves explicit catalog fields and the exact editor buffer in one transaction.</summary>
	public async Task<IReadOnlyList<string>> SaveSongEditAsync(
		SongEditDocument original,
		SongEditMetadata metadata,
		string? text,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(original);
		ArgumentNullException.ThrowIfNull(metadata);
		SongMetadataValidation.Validate(metadata.Scalars, original.Metadata);
		ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Title);
		ArgumentNullException.ThrowIfNull(metadata.Artists);
		ArgumentNullException.ThrowIfNull(metadata.Tags);
		await this.mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		string expectedJson = this.committedJson!;
		try
		{
			(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
			ChordDatabase database = this.Database!;
			Song song = database.Songs.Single(item => item.Id == original.SongId);
			if (song.Revision.Revision != original.Revision)
			{
				throw new InvalidOperationException("This song changed while the editor was open. Reopen it before saving.");
			}

			DateTimeOffset now = DateTimeOffset.UtcNow;
			List<string> warnings = [];
			SongFile? editedFile = original.Text is not null && text is not null ? database.SongFiles.Single(item => item.Id == original.FileId) : null;
			byte[] bytes = [];
			SongFileAnalysis? analysis = null;
			if (editedFile is not null)
			{
				Encoding encoding = GetTextEncoding(editedFile);
				byte[] preamble = editedFile.ByteOrderMark == ByteOrderMarkKind.None ? [] : encoding.GetPreamble();
				bytes = [.. preamble, .. encoding.GetBytes(text!)];
				analysis = SongFileAnalyzer.Analyze(bytes, editedFile.RelativePath);
				if (analysis.MediaKind != MediaKind.Text || analysis.SourceFormat == SourceFormat.OpenSongXml)
				{
					throw new InvalidOperationException("The text editor supports ChordPro, chord-over-text, and mixed song text.");
				}

				warnings.AddRange(SongEditorWarnings.Get(analysis));
				warnings.AddRange(SourceMetadataReconciliation.Apply(song, analysis).Select(conflict =>
					$"{conflict.Field}: kept the independent catalog value '{conflict.CatalogValue}'; source contains '{conflict.SourceValue}'."));
			}

			bool changedText = original.Text is not null && text is not null && !StringComparer.Ordinal.Equals(original.Text, text);
			await using IStagedBookWrite? write = changedText
				? await activeStore.StageWriteAsync(activeLocation, expectedJson, cancellationToken).ConfigureAwait(false) : null;
			if (write is not null)
			{
				SongFile file = database.SongFiles.Single(item => item.Id == original.FileId);
				using Stream existing = await activeStore.OpenManagedAssetAsync(activeLocation, file.Id, cancellationToken).ConfigureAwait(false);
				using MemoryStream existingBytes = new();
				await existing.CopyToAsync(existingBytes, cancellationToken).ConfigureAwait(false);
				if (!StringComparer.Ordinal.Equals(SongFileAnalyzer.Hash(existingBytes.ToArray()), original.ContentHash))
				{
					throw new InvalidOperationException("The song file changed outside the editor. Reopen it before saving.");
				}

				file.ContentHash = SongFileAnalyzer.Hash(bytes);
				file.ContentRevision++;
				file.SourceFormat = analysis!.SourceFormat;
				file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion;
				file.ObservedLength = bytes.Length;
				file.ObservedWriteUtc = now;
				file.Revision = NextRevision(file.Revision, deviceId, now);

				using MemoryStream content = new(bytes);
				await write.WriteManagedAssetAsync(file.Id, file.RelativePath, content, cancellationToken).ConfigureAwait(false);
			}

			metadata.ApplyTo(song, original);

			song.Revision = NextRevision(song.Revision, deviceId, now);
			database.Revision = NextRevision(database.Revision, deviceId, now);
			string updatedJson;
			if (write is null)
			{
				updatedJson = await activeStore.CommitMetadataAsync(activeLocation, expectedJson, database, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				updatedJson = DatabaseJson.Serialize(database);
				await write.WriteDatabaseJsonAsync(updatedJson, cancellationToken).ConfigureAwait(false);
				await write.CommitAsync(cancellationToken).ConfigureAwait(false);
			}

			this.SetDatabase(database);
			this.committedJson = updatedJson;
			return warnings;
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

	#region Private Methods

	private static Encoding GetTextEncoding(SongFile file)
		=> Encoding.GetEncoding(file.TextEncoding ?? "utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

	private static void AddTombstone(ChordDatabase database, Guid id, string type, RevisionStamp revision, Guid deviceId, DateTimeOffset now)
		=> database.Tombstones.Add(new Tombstone { EntityId = id, EntityType = type, Revision = NextRevision(revision, deviceId, now) });

	private async Task<SongEditDocument> GetSongEditCoreAsync(Guid songId, Guid? fileId, CancellationToken cancellationToken)
	{
		(IBookStore activeStore, BookLocation activeLocation) = this.GetActiveBook();
		ChordDatabase database = this.Database ?? throw new InvalidOperationException("No book is open.");
		Song song = database.Songs.Single(item => item.Id == songId);
		SongFile? file = fileId is Guid selectedId
			? database.SongFiles.Single(item => item.Id == selectedId && item.SongId == songId && !item.IsArchived)
			: this.GetOrderedSongFiles(songId).FirstOrDefault(item => !item.IsArchived);
		string? text = null;
		string? hash = null;
		if (file is { MediaKind: MediaKind.Text, RecoveryVersion: null } && file.SourceFormat != SourceFormat.OpenSongXml)
		{
			using Stream stream = await activeStore.OpenManagedAssetAsync(activeLocation, file.Id, cancellationToken).ConfigureAwait(false);
			using MemoryStream bytes = new();
			await stream.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
			hash = SongFileAnalyzer.Hash(bytes.ToArray());
			bytes.Position = 0;
			using StreamReader reader = new(bytes, GetTextEncoding(file), detectEncodingFromByteOrderMarks: true);
			text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
		}

		Dictionary<string, IReadOnlyList<string>> metadata = SongMetadata.Enumerate(song)
			.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)[.. pair.Value], StringComparer.Ordinal);
		if (!song.MetadataOverrides.ContainsKey("tempo") && song.MetronomeOverride?.BeatsPerMinute is int tempo)
		{
			metadata["tempo"] = [tempo.ToString(System.Globalization.CultureInfo.InvariantCulture)];
		}

		if (song.DurationSeconds is int duration && !metadata.ContainsKey("duration"))
		{
			metadata["duration"] = [duration.ToString(System.Globalization.CultureInfo.InvariantCulture)];
		}

		return new(song.Id, song.Revision.Revision, song.Title, [.. song.Artists], [.. song.Tags], file?.Id, hash, text) { Metadata = metadata };
	}

	#endregion
}
