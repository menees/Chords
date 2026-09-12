#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Reviews external files separately from adopting their metadata. Source files are never written.</summary>
internal sealed class FileSystemBookReconciler
{
	#region Private Data

	private const int BufferSize = 81920;
	private const int PdfHeaderLength = 5;
	private readonly FileSystemBookStore store;

	#endregion

	#region Public API

	public FileSystemBookReconciler(FileSystemBookStore store) => this.store = store;

	public async Task<BookReconcilePreview> PreviewAsync(BookLocation location, Guid deviceId, CancellationToken token)
	{
		string directory = this.store.GetDirectory(location);
		string expected = await this.store.ReadDatabaseJsonAsync(location, token).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(expected);
		Dictionary<Guid, List<string>> observed = EnumerateFiles(directory, token);

		HashSet<Guid> tracked = [.. database.SongFiles.Select(file => file.Id)];
		List<ExternalBookProblem> problems = [.. observed.Where(pair => !tracked.Contains(pair.Key)).SelectMany(
			pair => pair.Value.Select(name => new ExternalBookProblem(name, "Unaccepted external import candidate. Import explicitly to add it.")))];
		List<BookReconcileChange> changes = [];
		List<ReconciledFileObservation> observations = [];
		Dictionary<Guid, SongFileAnalysis> analyses = [];
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (SongFile file in database.SongFiles)
		{
			token.ThrowIfCancellationRequested();
			if (!observed.TryGetValue(file.Id, out List<string>? names) || names.Count != 1)
			{
				string message = names?.Count > 1 ? "Multiple files have this sheet ID; resolve the duplicate names first."
					: "Managed file is missing; no deletion was inferred.";
				problems.Add(new(file.RelativePath, message));
				continue;
			}

			string name = names[0];
			if (PortableManagedFileName.Validate(name).Count > 0)
			{
				problems.Add(new(name, "The external filename is not portable; rename it before applying."));
				continue;
			}

			FileInfo info = new(Path.Combine(directory, name));
			bool renamed = !StringComparer.Ordinal.Equals(file.RelativePath, name);
			bool changedObservation = file.ObservedLength != info.Length || file.ObservedWriteUtc?.UtcDateTime != info.LastWriteTimeUtc;
			if (!renamed && !changedObservation)
			{
				continue;
			}

			string hash = file.ContentHash;
			if (changedObservation)
			{
				try
				{
					SongFileAnalysis? analysis;
					(hash, analysis) = await ReadObservedFileAsync(info.FullName, name, file.ContentHash, token).ConfigureAwait(false);
					if (analysis is not null)
					{
						analyses.Add(file.Id, analysis);
						file.MediaKind = analysis.MediaKind;
						file.SourceFormat = analysis.SourceFormat;
						file.TextEncoding = analysis.TextEncoding;
						file.ByteOrderMark = analysis.ByteOrderMark;
						file.AnalysisVersion = SongFileAnalyzer.CurrentAnalysisVersion;
						file.ContentRevision++;
					}
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
				{
					problems.Add(new(name, exception.Message));
					continue;
				}
			}

			bool contentChanged = analyses.ContainsKey(file.Id);
			observations.Add(new(name, info.Length, info.LastWriteTimeUtc, hash));
			if (renamed || contentChanged)
			{
				changes.Add(new(file.Id, file.RelativePath, name, contentChanged));
				file.Revision = NextRevision(file.Revision, deviceId, now);
			}

			file.RelativePath = name;
			file.ContentHash = hash;
			file.ObservedLength = info.Length;
			file.ObservedWriteUtc = info.LastWriteTimeUtc;
		}

		List<BookMetadataConflict> conflicts = [];
		Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
		IEnumerable<IGrouping<Guid, SongFile>> groups = database.SongFiles
			.Where(file => !file.IsArchived && file.MediaKind == MediaKind.Text).GroupBy(file => file.SongId);
		foreach (IGrouping<Guid, SongFile> group in groups)
		{
			SongFile primary = group.OrderByDescending(file => file.DisplayPriority).ThenBy(file => file.Id).First();
			if (analyses.TryGetValue(primary.Id, out SongFileAnalysis? analysis))
			{
				Song song = songs[group.Key];
				conflicts.AddRange(SourceMetadataReconciliation.Apply(song, analysis));
				song.Revision = NextRevision(song.Revision, deviceId, now);
			}
		}

		if (observations.Count > 0)
		{
			database.Revision = NextRevision(database.Revision, deviceId, now);
		}

		return new(
			location,
			expected,
			DatabaseJson.Serialize(database),
			changes.AsReadOnly(),
			conflicts.AsReadOnly(),
			problems.AsReadOnly(),
			observations.AsReadOnly());
	}

	public async Task<BookReconcileResult> ApplyAsync(BookReconcilePreview preview, IReadOnlySet<string>? useSourceValues, CancellationToken token)
	{
		ArgumentNullException.ThrowIfNull(preview);
		string directory = this.store.GetDirectory(preview.Location);
		if (!StringComparer.Ordinal.Equals(await this.store.ReadDatabaseJsonAsync(preview.Location, token).ConfigureAwait(false), preview.ExpectedJson))
		{
			throw new BookStoreConcurrencyException();
		}

		ChordDatabase database = DatabaseJson.Deserialize(preview.ProposedJson);
		Dictionary<Guid, Song> songs = database.Songs.ToDictionary(song => song.Id);
		foreach (BookMetadataConflict conflict in preview.Conflicts.Where(conflict => useSourceValues?.Contains(conflict.Key) == true))
		{
			Song song = songs[conflict.SongId];
			if (conflict.Field == "Title")
			{
				song.Title = conflict.SourceValue;
			}
			else
			{
				song.Artists = [.. SourceMetadataReconciliation.Values(song, "artist", "author").Distinct(StringComparer.OrdinalIgnoreCase)];
			}
		}

		List<FileStream> locks = [];
		try
		{
			foreach (ReconciledFileObservation observation in preview.Observations)
			{
				string path = Path.Combine(directory, observation.Name);
				FileStream input = OpenRead(path);
				locks.Add(input);
				string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
				if (input.Length != observation.Length || File.GetLastWriteTimeUtc(path) != observation.WriteUtc
					|| !StringComparer.OrdinalIgnoreCase.Equals(hash, observation.Hash))
				{
					throw new BookStoreException("A sheet changed after the preview. Review folder changes again before applying.");
				}
			}

			if (preview.HasChanges)
			{
				await this.store.CommitReconciliationAsync(
					preview.Location, preview.ExpectedJson, DatabaseJson.Serialize(database), token).ConfigureAwait(false);
			}
		}
		finally
		{
			foreach (FileStream input in locks)
			{
				input.Dispose();
			}
		}

		return new(
			preview.Changes.Count(change => change.PreviousName != change.CurrentName),
			preview.Changes.Count(change => change.ContentChanged),
			preview.Problems);
	}

	#endregion

	#region Private Methods

	private static Dictionary<Guid, List<string>> EnumerateFiles(string directory, CancellationToken token)
	{
		Dictionary<Guid, List<string>> observed = [];
		foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			token.ThrowIfCancellationRequested();
			string name = Path.GetFileName(path);
			if (PortableManagedFileName.TryGetSongFileId(name, out Guid id))
			{
				if (!observed.TryGetValue(id, out List<string>? names))
				{
					names = [];
					observed.Add(id, names);
				}

				names.Add(name);
			}
		}

		return observed;
	}

	private static FileStream OpenRead(string path)
		=> new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

	private static RevisionStamp NextRevision(RevisionStamp revision, Guid device, DateTimeOffset now)
		=> new() { Revision = revision.Revision + 1, DeviceId = device, ModifiedUtc = now };

	private static async Task<(string Hash, SongFileAnalysis? Analysis)> ReadObservedFileAsync(
		string path, string name, string expectedHash, CancellationToken token)
	{
		await using FileStream input = OpenRead(path);
		byte[] prefix = new byte[PdfHeaderLength];
		int length = await input.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken: token).ConfigureAwait(false);
		bool pdf = length == prefix.Length && prefix.AsSpan().SequenceEqual("%PDF-"u8);
		string hash;
		SongFileAnalysis? analysis = null;
		if (pdf)
		{
			input.Position = 0;
			hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).ToLowerInvariant();
			if (!StringComparer.OrdinalIgnoreCase.Equals(hash, expectedHash))
			{
				analysis = SongFileAnalyzer.Analyze(prefix, name);
			}
		}
		else
		{
			using MemoryStream content = new();
			content.Write(prefix, 0, length);
			await input.CopyToAsync(content, token).ConfigureAwait(false);
			Memory<byte> bytes = content.GetBuffer().AsMemory(0, checked((int)content.Length));
			hash = SongFileAnalyzer.Hash(bytes.Span);
			if (!StringComparer.OrdinalIgnoreCase.Equals(hash, expectedHash))
			{
				analysis = SongFileAnalyzer.Analyze(bytes, name);
			}
		}

		return (hash, analysis);
	}

	#endregion
}
