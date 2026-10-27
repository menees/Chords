#region Using Directives

using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Imports supported source files without rewriting their content.</summary>
public static class BookImportService
{
	#region Private Data

	private const int BufferSize = 81920;

	#endregion

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

		Task<PendingImport> Read() => Task.FromResult(new PendingImport(
			"New Song.txt", new MemoryStream(bytes, writable: false), SongFileAnalyzer.Hash(bytes), analysis));
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
		Task<PendingImport> Read() => ReadAsync(sourceName, source, cancellationToken);
		IReadOnlyList<BookImportResult> results = await ImportAsync(
			store,
			location,
			[Read],
			deviceId,
			skipDuplicates: false,
			cancellationToken).ConfigureAwait(false);
		return results[0];
	}

	/// <summary>Captures selected files and suggests conflicts without changing the book or reading existing assets.</summary>
	public static async Task<BookImportReview> ReviewFilesAsync(
		IBookStore store, BookLocation location, IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sourcePaths);
		string json = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase catalog = DatabaseJson.Deserialize(json);
		BookImportReview review = new(json, catalog.Id);
		try
		{
			BookImportMatcher matcher = new(catalog);
			foreach (string path in sourcePaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Guid id = Guid.CreateVersion7();
				await using FileStream source = new(
					path,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read,
					bufferSize: BufferSize,
					options: FileOptions.Asynchronous | FileOptions.SequentialScan);
				PendingImport pending = await ReadAsync(Path.GetFileName(path), source, cancellationToken, review.GetPath(id)).ConfigureAwait(false);
				await using (Stream content = pending.Content)
				{
					if (pending.Analysis.MediaKind != MediaKind.Pdf)
					{
						await using FileStream snapshot = new(
							review.GetPath(id),
							FileMode.CreateNew,
							FileAccess.Write,
							FileShare.None,
							bufferSize: BufferSize,
							options: FileOptions.Asynchronous | FileOptions.SequentialScan);
						await content.CopyToAsync(snapshot, cancellationToken).ConfigureAwait(false);
					}
				}

				review.Add(matcher.Match(id, pending.SourceName, pending.ContentHash, pending.Analysis));
			}

			return review;
		}
		catch
		{
			review.Dispose();
			throw;
		}
	}

	/// <summary>Applies explicit review decisions in one staged transaction against the exact reviewed catalog.</summary>
	public static Task<IReadOnlyList<BookImportResult>> ApplyReviewAsync(
		IBookStore store,
		BookLocation location,
		BookImportReview review,
		IReadOnlyList<BookImportDecision> decisions,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(review);
		ArgumentNullException.ThrowIfNull(decisions);
		IReadOnlyList<BookImportItem> items = review.Items;
		if (decisions.Count != items.Count || decisions.Select(item => item.ItemId).Distinct().Count() != items.Count)
		{
			throw new ArgumentException("Choose one action for each reviewed source.", nameof(decisions));
		}

		Dictionary<Guid, BookImportDecision> choices = decisions.ToDictionary(item => item.ItemId);
		Dictionary<Guid, Guid> targets = [];
		List<Func<Task<PendingImport>>> imports = [];
		foreach (BookImportItem item in items)
		{
			if (!choices.TryGetValue(item.Id, out BookImportDecision? choice) || !Enum.IsDefined(choice.Action)
				|| (choice.Action != BookImportAction.AddSheet && choice.TargetSongId is not null))
			{
				throw new ArgumentException("Choose a valid action for each reviewed source.", nameof(decisions));
			}

			if (choice.Action == BookImportAction.Skip)
			{
				continue;
			}

			Guid? target = null;
			if (choice.Action == BookImportAction.AddSheet)
			{
				BookImportCandidate? candidate = item.Candidates.FirstOrDefault(value => value.SongId == choice.TargetSongId);
				if (candidate is null || candidate.IsArchived)
				{
					throw new InvalidOperationException("Choose a suggested active song before adding a sheet. Restore archived songs first.");
				}

				target = candidate.IsIncoming
					? targets.GetValueOrDefault(candidate.SongId) : candidate.SongId;
				if (target == Guid.Empty)
				{
					throw new InvalidOperationException("The earlier incoming target is skipped. Import that song or choose another target.");
				}
			}

			targets.Add(item.Id, target ?? item.Id);
			imports.Add(async () => new PendingImport(
				item.Name,
				await review.OpenSourceAsync(item.Id, cancellationToken).ConfigureAwait(false),
				item.ContentHash,
				item.Analysis,
				target,
				item.Id,
				choice.Action == BookImportAction.ImportNewSong));
		}

		return imports.Count == 0 ? SkipReviewAsync(store, location, review.ExpectedCatalog, cancellationToken)
			: ImportAsync(store, location, imports, deviceId, skipDuplicates: true, cancellationToken, expectedCatalog: review.ExpectedCatalog);
	}
	#endregion

	#region Private Methods

	private static async Task<IReadOnlyList<BookImportResult>> SkipReviewAsync(
		IBookStore store, BookLocation location, string expectedCatalog, CancellationToken cancellationToken)
	{
		if (await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false) != expectedCatalog)
		{
			throw new BookStoreConcurrencyException();
		}

		return [];
	}

	private static async Task<IReadOnlyList<BookImportResult>> ImportAsync(
		IBookStore store,
		BookLocation location,
		IEnumerable<Func<Task<PendingImport>>> imports,
		Guid deviceId,
		bool skipDuplicates,
		CancellationToken cancellationToken,
		Action<Song>? initializeSong = null,
		Guid? targetSongId = null,
		string? expectedCatalog = null)
	{
		string expectedJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		if (expectedCatalog is not null && expectedCatalog != expectedJson)
		{
			throw new BookStoreConcurrencyException();
		}

		ChordDatabase database = DatabaseJson.Deserialize(expectedJson);
		Dictionary<Guid, Song> songsById = database.Songs.ToDictionary(song => song.Id);
		Dictionary<Guid, int> priorities = database.SongFiles.GroupBy(file => file.SongId)
			.ToDictionary(group => group.Key, group => group.Min(file => file.DisplayPriority));
		HashSet<string> sheetIdentities = database.SongFiles.Select(file => file.SongId.ToString("N") + "\0" + file.ContentHash.ToUpperInvariant())
			.ToHashSet(StringComparer.Ordinal);
		Song? targetSong = targetSongId is Guid id ? database.Songs.Single(song => song.Id == id) : null;
		if (targetSong is { IsArchived: true })
		{
			throw new InvalidOperationException("Restore the song before adding sheets.");
		}

		HashSet<string> identities = [];
		if (skipDuplicates)
		{
			foreach (SongFile file in database.SongFiles.Where(file => targetSong is null || file.SongId == targetSong.Id))
			{
				identities.Add(CreateIdentity(targetSong?.Title ?? songsById[file.SongId].Title, file.ContentHash));
			}
		}

		List<BookImportResult> additions = [];
		await using IStagedBookWrite write = await store.StageWriteAsync(location, expectedJson, cancellationToken).ConfigureAwait(false);
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (Func<Task<PendingImport>> read in imports)
		{
			PendingImport import = await read().ConfigureAwait(false);
			await using Stream content = import.Content;
			string contentHash = import.ContentHash;
			Song? destination = import.TargetSongId is Guid targetId ? songsById[targetId] : targetSong;
			if (destination is { IsArchived: true })
			{
				throw new InvalidOperationException("Restore the song before adding sheets.");
			}

			bool add = destination is not null
				? sheetIdentities.Add(destination.Id.ToString("N") + "\0" + contentHash.ToUpperInvariant())
				: import.ForceNewSong || !skipDuplicates || identities.Add(CreateIdentity(import.Analysis.Title, contentHash));
			if (add)
			{
				Guid songId = destination?.Id ?? import.NewSongId ?? Guid.CreateVersion7();
				Guid songFileId = Guid.CreateVersion7();
				string extension = Path.GetExtension(import.SourceName);
				Song song = destination ?? new()
				{
					Id = songId,
					Title = import.Analysis.Title,
					Artists = [.. import.Analysis.Artists],
					Revision = RevisionStamp.Initial(deviceId, now),
				};
				initializeSong?.Invoke(song);
				string relativePath = PortableManagedFileName.Create(song.Title, songFileId, extension);
				if (destination is null)
				{
					foreach ((string key, IReadOnlyList<SourceMetadataValue> values) in import.Analysis.Metadata)
					{
						song.SourceMetadata[key] =
						[
							.. values.Select(value => new SourceMetadataValue { Value = value.Value, SourceName = value.SourceName }),
						];
					}

					database.Songs.Add(song);
					songsById.Add(songId, song);
				}

				SongFile file = CreateSongFile(
					import,
					songId,
					songFileId,
					relativePath,
					destination is null ? 0 : Math.Max(int.MinValue + 1, priorities.GetValueOrDefault(songId)) - 1,
					now,
					deviceId);
				database.SongFiles.Add(file);
				priorities[songId] = file.DisplayPriority;
				sheetIdentities.Add(songId.ToString("N") + "\0" + contentHash.ToUpperInvariant());
				destination?.Revision = new() { Revision = destination.Revision.Revision + 1, ModifiedUtc = now, DeviceId = deviceId };

				additions.Add(new BookImportResult(songId, songFileId, relativePath, import.Analysis));
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

	private static SongFile CreateSongFile(
		PendingImport import,
		Guid songId,
		Guid songFileId,
		string relativePath,
		int priority,
		DateTimeOffset now,
		Guid deviceId)
	{
		return new()
		{
			Id = songFileId,
			SongId = songId,
			RelativePath = relativePath,
			MediaKind = import.Analysis.MediaKind,
			SourceFormat = import.Analysis.SourceFormat,
			DisplayPriority = priority,
			TextEncoding = import.Analysis.TextEncoding,
			ByteOrderMark = import.Analysis.ByteOrderMark,
			ContentHash = import.ContentHash,
			ObservedLength = import.Content.Length,
			ObservedWriteUtc = now,
			ContentRevision = 1,
			AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion,
			Revision = RevisionStamp.Initial(deviceId, now),
		};
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
		CancellationToken cancellationToken,
		string? snapshotPath = null)
	{
		const int PrefixBytes = 4096;
		const int MaximumTextBytes = 16 * 1024 * 1024;
		byte[] prefix = new byte[PrefixBytes];
		int prefixLength = await source.ReadAtLeastAsync(prefix, PrefixBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
		bool pdf = prefix.AsSpan(0, prefixLength).StartsWith("%PDF-"u8);
		if (!pdf)
		{
			SongFileAnalyzer.ValidateTextPrefix(prefix.AsSpan(0, prefixLength));
		}

		Stream content = pdf
			? new FileStream(
				snapshotPath ?? Path.Combine(Path.GetTempPath(), "ChordBookImport-" + Guid.NewGuid().ToString("N") + ".tmp"),
				FileMode.CreateNew,
				FileAccess.ReadWrite,
				FileShare.None,
				BufferSize,
				FileOptions.Asynchronous | FileOptions.SequentialScan | (snapshotPath is null ? FileOptions.DeleteOnClose : FileOptions.None))
			: new MemoryStream();
		try
		{
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			hash.AppendData(prefix, 0, prefixLength);
			await content.WriteAsync(prefix.AsMemory(0, prefixLength), cancellationToken).ConfigureAwait(false);
			byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
			try
			{
				int count;
				while ((count = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
				{
					if (!pdf && count > MaximumTextBytes - content.Length)
					{
						throw new BookStoreValidationException("Song text exceeds the 16 MB import limit. PDF files are streamed separately.");
					}

					hash.AppendData(buffer, 0, count);
					await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}

			SongFileAnalysis analysis = pdf ? SongFileAnalyzer.Analyze(prefix.AsMemory(0, prefixLength), sourceName)
				: SongFileAnalyzer.Analyze(((MemoryStream)content).GetBuffer().AsMemory(0, checked((int)content.Length)), sourceName);
			content.Position = 0;
			return new PendingImport(sourceName, content, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), analysis);
		}
		catch
		{
			await content.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	#endregion

	#region Private Types

	private sealed record PendingImport(
		string SourceName,
		Stream Content,
		string ContentHash,
		SongFileAnalysis Analysis,
		Guid? TargetSongId = null,
		Guid? NewSongId = null,
		bool ForceNewSong = false);

	#endregion
}
