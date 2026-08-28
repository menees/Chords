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
		using MemoryStream content = new();
		await source.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
		byte[] bytes = content.ToArray();
		SongFileAnalysis analysis = SongFileAnalyzer.Analyze(bytes, sourceName);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		DateTimeOffset now = DateTimeOffset.UtcNow;
		Guid songId = Guid.CreateVersion7(now);
		Guid songFileId = Guid.CreateVersion7(now.AddTicks(1));
		string extension = Path.GetExtension(sourceName);
		string relativePath = PortableManagedFileName.Create(analysis.Title, songFileId, extension);
		Song song = new()
		{
			Id = songId,
			Title = analysis.Title,
			Artists = [.. analysis.Artists],
			Revision = RevisionStamp.Initial(deviceId, now),
		};
		foreach ((string key, IReadOnlyList<SourceMetadataValue> values) in analysis.Metadata)
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
			MediaKind = analysis.MediaKind,
			SourceFormat = analysis.SourceFormat,
			TextEncoding = analysis.TextEncoding,
			ByteOrderMark = analysis.ByteOrderMark,
			ContentHash = SongFileAnalyzer.Hash(bytes),
			ObservedLength = bytes.Length,
			ObservedWriteUtc = now,
			ContentRevision = 1,
			Revision = RevisionStamp.Initial(deviceId, now),
		};
		database.Songs.Add(song);
		database.SongFiles.Add(file);
		database.Revision = new()
		{
			Revision = database.Revision.Revision + 1,
			ModifiedUtc = now,
			DeviceId = deviceId,
		};

		await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
		await write.WriteManagedAssetAsync(songFileId, relativePath, new MemoryStream(bytes, writable: false), cancellationToken).ConfigureAwait(false);
		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
		await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		return new(songId, songFileId, relativePath, analysis);
	}

	#endregion
}
