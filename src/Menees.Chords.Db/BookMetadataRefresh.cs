#region Using Directives

using System.IO;
using System.Text;
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
		List<ExternalBookProblem> problems = [];
		List<SongFile> pending = [];
		foreach (SongFile file in database.SongFiles.Where(file => file.AnalysisVersion < SongFileAnalyzer.CurrentAnalysisVersion))
		{
			if (file.AnalysisFailure is SongFileAnalysisFailure failure && failure.AnalyzerVersion >= SongFileAnalyzer.CurrentAnalysisVersion
				&& StringComparer.OrdinalIgnoreCase.Equals(failure.ContentHash, file.ContentHash)
				&& (failure.DeviceId is null || failure.DeviceId == deviceId))
			{
				problems.Add(new(file.RelativePath, failure.Message));
			}
			else
			{
				pending.Add(file);
			}
		}

		HashSet<Guid> affectedSongIds = [.. pending.Select(file => file.SongId)];
		bool metadataChanged = false;
		int analyzedFileCount = 0;
		int updatedSongCount = 0;
		if (affectedSongIds.Count > 0)
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			Dictionary<Guid, SongFileAnalysis> analyses = [];
			Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
			ILookup<Guid, SongFile> filesBySong = database.SongFiles.ToLookup(file => file.SongId);
			foreach (SongFile file in pending)
			{
				try
				{
					using Stream stream = await store.OpenManagedAssetAsync(location, file.Id, cancellationToken).ConfigureAwait(false);
					var (_, analysis, problem) = await SongFileContentReader.ReadAsync(
						stream, file.RelativePath, computeHash: false, cancellationToken).ConfigureAwait(false);
					if (problem is not null || analysis is null)
					{
						RecordFailure(file, problem ?? "The sheet could not be analyzed.", problems, deviceId, now);
						metadataChanged = true;
						continue;
					}

					analyses[file.Id] = analysis;
					analyzedFileCount++;
					if (file.AnalysisVersion < SongFileAnalyzer.CurrentAnalysisVersion)
					{
						metadataChanged = true;
						file.AnalysisFailure = null;
						file.MediaKind = analysis.MediaKind;
						file.SourceFormat = analysis.SourceFormat;
						file.TextEncoding = analysis.TextEncoding;
						file.ByteOrderMark = analysis.ByteOrderMark;
						file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion;
						file.Revision = NextRevision(file.Revision, deviceId, now);
					}
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
					or BookStoreValidationException or DecoderFallbackException)
				{
					// A failed maintenance read must not hide an otherwise usable book or replace its source observations.
					RecordFailure(file, exception.Message, problems, deviceId, now, deviceSpecific: true);
					metadataChanged = true;
				}
			}

			foreach (Guid songId in affectedSongIds)
			{
				SongFile? metadataFile = filesBySong[songId]
					.Where(file => !file.IsArchived && file.RecoveryVersion is null
						&& file.MediaKind == MediaKind.Text)
					.OrderByDescending(file => file.DisplayPriority)
					.ThenBy(file => file.Id)
					.FirstOrDefault();
				if (metadataFile is not null && analyses.TryGetValue(metadataFile.Id, out SongFileAnalysis? analysis))
				{
					ApplyMetadata(songs[songId], analysis, deviceId, now);
					updatedSongCount++;
				}
			}

			if (metadataChanged || updatedSongCount > 0)
			{
				database.Revision = NextRevision(database.Revision, deviceId, now);
				await store.CommitMetadataAsync(location, expectedJson, DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			}
		}

		return new(analyzedFileCount, updatedSongCount) { Problems = problems.AsReadOnly() };
	}

	#endregion

	#region Private Methods

	private static void RecordFailure(
		SongFile file, string message, List<ExternalBookProblem> problems, Guid deviceId, DateTimeOffset now, bool deviceSpecific = false)
	{
		file.AnalysisFailure = new(SongFileAnalyzer.CurrentAnalysisVersion, file.ContentHash, message) { DeviceId = deviceSpecific ? deviceId : null };
		file.Revision = NextRevision(file.Revision, deviceId, now);
		problems.Add(new(file.RelativePath, message));
	}

	private static void ApplyMetadata(Song song, SongFileAnalysis analysis, Guid deviceId, DateTimeOffset now)
	{
		_ = SourceMetadataReconciliation.Apply(song, analysis);

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
