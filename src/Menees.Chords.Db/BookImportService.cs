#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Imports supported source files without rewriting their content.</summary>
public static class BookImportService
{
	#region Public API

	/// <summary>Creates a song from explicitly authored text and catalog metadata without converting its syntax.</summary>
	public static Task<BookImportResult> CreateSongAsync(
		IBookStore store,
		BookLocation location,
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string text,
		Guid deviceId,
		CancellationToken cancellationToken = default)
		=> CreateSongAsync(store, location, title, artists, tags, text, deviceId, null, cancellationToken);

	/// <summary>Creates authored text and independent scalar catalog metadata in one atomic import.</summary>
	public static async Task<BookImportResult> CreateSongAsync(
		IBookStore store,
		BookLocation location,
		string title,
		IReadOnlyList<string> artists,
		IReadOnlyList<string> tags,
		string text,
		Guid deviceId,
		IReadOnlyDictionary<string, IReadOnlyList<string>>? metadata,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		ArgumentNullException.ThrowIfNull(artists);
		ArgumentNullException.ThrowIfNull(tags);
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
		SongFileAnalysis analysis = SongFileAnalyzer.Analyze(bytes, "New Song.txt");
		if (analysis.MediaKind != MediaKind.Text || analysis.SourceFormat == SourceFormat.OpenSongXml)
		{
			throw new InvalidOperationException("Paste ChordPro, chord-over-text, or mixed song text. Import PDF and OpenSong files instead.");
		}

		PendingImport import = new("New Song.txt", bytes, analysis);
		Task<PendingImport> Read() => Task.FromResult(import);
		void Initialize(Song song)
		{
			song.Title = title.Trim();
			song.Artists = [.. artists.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];
			song.Tags = [.. tags.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())];
			if (metadata is not null)
			{
				foreach ((string name, IReadOnlyList<string> values) in metadata)
				{
					song.MetadataOverrides[name] = [.. values];
				}
			}

			_ = SourceMetadataReconciliation.Apply(song, analysis);
		}

		IReadOnlyList<BookImportResult> results = await ImportAsync(
			store, location, [Read], deviceId, skipDuplicates: false, cancellationToken, Initialize).ConfigureAwait(false);
		return results[0];
	}

	/// <summary>Imports a local file into a chord book.</summary>
	public static async Task<BookImportResult> ImportFileAsync(
		IBookStore store,
		BookLocation location,
		string sourcePath,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		await using FileStream input = new(
			sourcePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 1,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return await ImportAsync(store, location, Path.GetFileName(sourcePath), input, deviceId, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Imports multiple local files using one atomic staged write.</summary>
	public static async Task<IReadOnlyList<BookImportResult>> ImportFilesAsync(
		IBookStore store,
		BookLocation location,
		IReadOnlyList<string> sourcePaths,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sourcePaths);
		IEnumerable<Func<Task<PendingImport>>> imports = sourcePaths.Select(path => new Func<Task<PendingImport>>(
			() => ReadFileAsync(path, cancellationToken)));
		return await ImportAsync(store, location, imports, deviceId, skipDuplicates: true, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Adds local files to an existing song, skipping identical content already associated with it.</summary>
	public static Task<IReadOnlyList<BookImportResult>> ImportSongFilesAsync(
		IBookStore store,
		BookLocation location,
		Guid songId,
		IReadOnlyList<string> sourcePaths,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sourcePaths);
		IEnumerable<Func<Task<PendingImport>>> imports = sourcePaths.Select(path => new Func<Task<PendingImport>>(
			() => ReadFileAsync(path, cancellationToken)));
		return ImportAsync(store, location, imports, deviceId, skipDuplicates: true, cancellationToken, targetSongId: songId);
	}

	/// <summary>Imports a named stream into a chord book.</summary>
	public static async Task<BookImportResult> ImportAsync(
		IBookStore store,
		BookLocation location,
		string sourceName,
		Stream source,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
		ArgumentNullException.ThrowIfNull(source);
		PendingImport import = await ReadAsync(sourceName, source, cancellationToken).ConfigureAwait(false);
		Task<PendingImport> Read() => Task.FromResult(import);
		IReadOnlyList<BookImportResult> results = await ImportAsync(
			store,
			location,
			[Read],
			deviceId,
			skipDuplicates: false,
			cancellationToken).ConfigureAwait(false);
		return results[0];
	}

	#endregion

	#region Private Methods

	private static async Task<IReadOnlyList<BookImportResult>> ImportAsync(
		IBookStore store,
		BookLocation location,
		IEnumerable<Func<Task<PendingImport>>> imports,
		Guid deviceId,
		bool skipDuplicates,
		CancellationToken cancellationToken,
		Action<Song>? initializeSong = null,
		Guid? targetSongId = null)
	{
		string expectedJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(expectedJson);
		Song? targetSong = targetSongId is Guid id ? database.Songs.Single(song => song.Id == id) : null;
		if (targetSong is { IsArchived: true })
		{
			throw new InvalidOperationException("Restore the song before adding sheets.");
		}

		int appendedPriority = targetSong is null ? 0
			: database.SongFiles.Where(file => file.SongId == targetSong.Id).Select(file => file.DisplayPriority).DefaultIfEmpty(0).Min();
		if (appendedPriority > int.MinValue)
		{
			appendedPriority--;
		}

		HashSet<string> identities = [];
		if (skipDuplicates)
		{
			Dictionary<Guid, Song>? songsById = targetSong is null ? database.Songs.ToDictionary(song => song.Id) : null;
			foreach (SongFile file in database.SongFiles.Where(file => targetSong is null || file.SongId == targetSong.Id))
			{
				identities.Add(CreateIdentity(targetSong?.Title ?? songsById![file.SongId].Title, file.ContentHash));
			}
		}

		List<BookImportResult> additions = [];
		await using IStagedBookWrite write = await store.StageWriteAsync(location, expectedJson, cancellationToken).ConfigureAwait(false);
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (Func<Task<PendingImport>> read in imports)
		{
			PendingImport import = await read().ConfigureAwait(false);
			string contentHash = SongFileAnalyzer.Hash(import.Content);
			if (!skipDuplicates || identities.Add(CreateIdentity(targetSong?.Title ?? import.Analysis.Title, contentHash)))
			{
				Guid songId = targetSong?.Id ?? Guid.CreateVersion7();
				Guid songFileId = Guid.CreateVersion7();
				string extension = Path.GetExtension(import.SourceName);
				Song song = targetSong ?? new()
				{
					Id = songId,
					Title = import.Analysis.Title,
					Artists = [.. import.Analysis.Artists],
					Revision = RevisionStamp.Initial(deviceId, now),
				};
				initializeSong?.Invoke(song);
				string relativePath = PortableManagedFileName.Create(song.Title, songFileId, extension);
				if (targetSong is null)
				{
					foreach ((string key, IReadOnlyList<SourceMetadataValue> values) in import.Analysis.Metadata)
					{
						song.SourceMetadata[key] =
						[
							.. values.Select(value => new SourceMetadataValue { Value = value.Value, SourceName = value.SourceName }),
						];
					}

					database.Songs.Add(song);
				}

				SongFile file = new()
				{
					Id = songFileId,
					SongId = songId,
					RelativePath = relativePath,
					MediaKind = import.Analysis.MediaKind,
					SourceFormat = import.Analysis.SourceFormat,
					DisplayPriority = targetSong is null ? 0 : appendedPriority,
					TextEncoding = import.Analysis.TextEncoding,
					ByteOrderMark = import.Analysis.ByteOrderMark,
					ContentHash = contentHash,
					ObservedLength = import.Content.Length,
					ObservedWriteUtc = now,
					ContentRevision = 1,
					AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion,
					Revision = RevisionStamp.Initial(deviceId, now),
				};
				database.SongFiles.Add(file);
				additions.Add(new BookImportResult(songId, songFileId, relativePath, import.Analysis));
				using MemoryStream content = new(import.Content, writable: false);
				await write.WriteManagedAssetAsync(songFileId, relativePath, content, cancellationToken).ConfigureAwait(false);
			}
		}

		if (additions.Count > 0)
		{
			targetSong?.Revision = new() { Revision = targetSong.Revision.Revision + 1, ModifiedUtc = now, DeviceId = deviceId };

			database.Revision = new()
			{
				Revision = database.Revision.Revision + 1,
				ModifiedUtc = now,
				DeviceId = deviceId,
			};
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		}

		return additions;
	}

	private static async Task<PendingImport> ReadFileAsync(string sourcePath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		await using FileStream input = new(
			sourcePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 1,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return await ReadAsync(Path.GetFileName(sourcePath), input, cancellationToken).ConfigureAwait(false);
	}

	private static string CreateIdentity(string title, string contentHash) => title.ToUpperInvariant() + "\0" + contentHash;

	private static async Task<PendingImport> ReadAsync(
		string sourceName,
		Stream source,
		CancellationToken cancellationToken)
	{
		using MemoryStream content = new();
		await source.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
		byte[] bytes = content.ToArray();
		SongFileAnalysis analysis = SongFileAnalyzer.Analyze(bytes, sourceName);
		return new PendingImport(sourceName, bytes, analysis);
	}

	#endregion

	#region Private Types

	private sealed record PendingImport(string SourceName, byte[] Content, SongFileAnalysis Analysis);

	#endregion
}
