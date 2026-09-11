#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Imports supported source files without rewriting their content.</summary>
public static class BookImportService
{
	#region Public API

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
		CancellationToken cancellationToken)
	{
		string expectedJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(expectedJson);
		Dictionary<Guid, Song> songsById = database.Songs.ToDictionary(song => song.Id);
		HashSet<string> identities =
		[
			.. database.SongFiles.Select(file => CreateIdentity(songsById[file.SongId].Title, file.ContentHash)),
		];
		List<BookImportResult> additions = [];
		await using IStagedBookWrite write = await store.StageWriteAsync(location, expectedJson, cancellationToken).ConfigureAwait(false);
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (Func<Task<PendingImport>> read in imports)
		{
			PendingImport import = await read().ConfigureAwait(false);
			string contentHash = SongFileAnalyzer.Hash(import.Content);
			if (!skipDuplicates || identities.Add(CreateIdentity(import.Analysis.Title, contentHash)))
			{
				Guid songId = Guid.CreateVersion7();
				Guid songFileId = Guid.CreateVersion7();
				string extension = Path.GetExtension(import.SourceName);
				string relativePath = PortableManagedFileName.Create(import.Analysis.Title, songFileId, extension);
				Song song = new()
				{
					Id = songId,
					Title = import.Analysis.Title,
					Artists = [.. import.Analysis.Artists],
					Revision = RevisionStamp.Initial(deviceId, now),
				};
				foreach ((string key, IReadOnlyList<SourceMetadataValue> values) in import.Analysis.Metadata)
				{
					song.SourceMetadata[key] =
					[
						.. values.Select(value => new SourceMetadataValue { Value = value.Value, SourceName = value.SourceName }),
					];
				}

				SongFile file = new()
				{
					Id = songFileId,
					SongId = songId,
					RelativePath = relativePath,
					MediaKind = import.Analysis.MediaKind,
					SourceFormat = import.Analysis.SourceFormat,
					TextEncoding = import.Analysis.TextEncoding,
					ByteOrderMark = import.Analysis.ByteOrderMark,
					ContentHash = contentHash,
					ObservedLength = import.Content.Length,
					ObservedWriteUtc = now,
					ContentRevision = 1,
					AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion,
					Revision = RevisionStamp.Initial(deviceId, now),
				};
				database.Songs.Add(song);
				database.SongFiles.Add(file);
				additions.Add(new BookImportResult(songId, songFileId, relativePath, import.Analysis));
				using MemoryStream content = new(import.Content, writable: false);
				await write.WriteManagedAssetAsync(songFileId, relativePath, content, cancellationToken).ConfigureAwait(false);
			}
		}

		if (additions.Count > 0)
		{
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
