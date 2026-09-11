#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Refreshes persisted catalog metadata when the file analyzer changes.</summary>
public static class BookMetadataRefresh
{
	#region Public API

	/// <summary>Reanalyzes affected managed files once and atomically commits their catalog metadata.</summary>
	public static async Task<BookMetadataRefreshResult> RefreshAsync(
		IBookStore store,
		BookLocation location,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(location);
		string expectedJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(expectedJson);
		HashSet<Guid> affectedSongIds =
		[
			.. database.SongFiles
				.Where(file => file.AnalysisVersion < SongFileAnalyzer.CurrentAnalysisVersion)
				.Select(file => file.SongId),
		];
		int analyzedFileCount = 0;
		int updatedSongCount = 0;
		if (affectedSongIds.Count > 0)
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			Dictionary<Guid, SongFileAnalysis> analyses = [];
			Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
			ILookup<Guid, SongFile> filesBySong = database.SongFiles.ToLookup(file => file.SongId);
			foreach (SongFile file in database.SongFiles.Where(file => affectedSongIds.Contains(file.SongId)))
			{
				try
				{
					using Stream stream = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken).ConfigureAwait(false);
					using MemoryStream content = new();
					await stream.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
					SongFileAnalysis analysis = SongFileAnalyzer.Analyze(content.ToArray(), file.RelativePath);
					analyses[file.Id] = analysis;
					analyzedFileCount++;
					if (file.AnalysisVersion < SongFileAnalyzer.CurrentAnalysisVersion)
					{
						file.MediaKind = analysis.MediaKind;
						file.SourceFormat = analysis.SourceFormat;
						file.TextEncoding = analysis.TextEncoding;
						file.ByteOrderMark = analysis.ByteOrderMark;
						file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion;
						file.Revision = NextRevision(file.Revision, deviceId, now);
					}
				}
				catch (FileNotFoundException)
				{
					// External reconciliation reports missing assets; metadata maintenance must not prevent opening the book.
				}
				catch (DirectoryNotFoundException)
				{
					// External reconciliation reports missing assets; metadata maintenance must not prevent opening the book.
				}
			}

			foreach (Guid songId in affectedSongIds)
			{
				SongFile? metadataFile = filesBySong[songId]
					.Where(file => !file.IsArchived
						&& analyses.TryGetValue(file.Id, out SongFileAnalysis? analysis) && analysis.MediaKind == MediaKind.Text)
					.OrderByDescending(file => file.DisplayPriority)
					.ThenBy(file => file.Id)
					.FirstOrDefault();
				if (metadataFile is not null)
				{
					ApplyMetadata(songs[songId], analyses[metadataFile.Id], deviceId, now);
					updatedSongCount++;
				}
			}

			if (analyzedFileCount > 0)
			{
				database.Revision = NextRevision(database.Revision, deviceId, now);
				await store.CommitMetadataAsync(location, expectedJson, DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			}
		}

		return new(analyzedFileCount, updatedSongCount);
	}

	#endregion

	#region Private Methods

	private static void ApplyMetadata(Song song, SongFileAnalysis analysis, Guid deviceId, DateTimeOffset now)
	{
		if (analysis.Metadata.ContainsKey("title") || analysis.Metadata.ContainsKey("t"))
		{
			song.Title = analysis.Title;
		}

		song.Artists = [.. analysis.Artists];
		song.SourceMetadata.Clear();
		foreach ((string key, IReadOnlyList<SourceMetadataValue> values) in analysis.Metadata)
		{
			song.SourceMetadata[key] =
			[
				.. values.Select(value => new SourceMetadataValue { Value = value.Value, SourceName = value.SourceName }),
			];
		}

		song.Revision = NextRevision(song.Revision, deviceId, now);
	}

	private static RevisionStamp NextRevision(RevisionStamp current, Guid deviceId, DateTimeOffset now) => new()
	{
		Revision = current.Revision + 1,
		ModifiedUtc = now,
		DeviceId = deviceId,
	};

	#endregion
}
