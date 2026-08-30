#region Using Directives

using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Stores shallow, human-readable chord books in ordinary filesystem folders.</summary>
public sealed class FileSystemBookStore : IBookStore, IExternalBookReconciler, IDisposable
{
	#region Private Data

	private const string DatabaseFileName = "database.json";
	private const int BufferSize = 81920;
	private static readonly UTF8Encoding Utf8NoBom = new(false, true);
	private readonly SemaphoreSlim commitLock = new(1, 1);
	private readonly Dictionary<Guid, string> paths = [];
	private readonly string rootDirectory;
	private readonly Guid storeId = Guid.NewGuid();
	private readonly Action<FileSystemCommitStep>? faultInjector;

	#endregion

	#region Constructors

	/// <summary>Creates a store rooted at the specified directory.</summary>
	public FileSystemBookStore(string rootDirectory)
		: this(rootDirectory, null)
	{
	}

	internal FileSystemBookStore(string rootDirectory, Action<FileSystemCommitStep>? faultInjector)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
		this.rootDirectory = Path.GetFullPath(rootDirectory);
		this.faultInjector = faultInjector;
		Directory.CreateDirectory(this.rootDirectory);
	}

	#endregion

	#region Public API

	/// <inheritdoc />
	public BookStoreCapabilities Capabilities => BookStoreCapabilities.ExternalChangeDetection
		| BookStoreCapabilities.UserVisibleLocation | BookStoreCapabilities.AvailableSpaceReporting;

	/// <inheritdoc />
	public async Task<BookLocation> CreateBookAsync(string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		cancellationToken.ThrowIfCancellationRequested();
		ChordDatabase database = ChordDatabase.Create(name, deviceId);
		string directory = this.GetUnusedBookDirectory(name);
		Directory.CreateDirectory(directory);
		try
		{
			await WriteTextDurablyAsync(Path.Combine(directory, DatabaseFileName), DatabaseJson.Serialize(database), cancellationToken)
				.ConfigureAwait(false);
		}
		catch
		{
			Directory.Delete(directory, recursive: false);
			throw;
		}

		return this.Register(directory, database.Id);
	}

	/// <summary>Opens an existing chord-book folder.</summary>
	public async Task<BookLocation> OpenBookAsync(string directory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		string fullPath = Path.GetFullPath(directory);
		string json = await File.ReadAllTextAsync(Path.Combine(fullPath, DatabaseFileName), Utf8NoBom, cancellationToken)
			.ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(json);
		return this.Register(fullPath, database.Id);
	}

	/// <summary>Gets the user-visible directory for a location owned by this store.</summary>
	public string GetDirectory(BookLocation location) => this.GetPath(location);

	/// <inheritdoc />
	public void Dispose() => this.commitLock.Dispose();

	/// <inheritdoc />
	public Task<bool> ExistsAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string path = this.GetPath(location);
		return Task.FromResult(File.Exists(Path.Combine(path, DatabaseFileName)));
	}

	/// <inheritdoc />
	public async Task DeleteBookAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		foreach (SongFile file in database.SongFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			File.Delete(GetManagedPath(directory, file.RelativePath));
		}

		File.Delete(Path.Combine(directory, DatabaseFileName));
		if (!Directory.EnumerateFileSystemEntries(directory).Any())
		{
			Directory.Delete(directory, recursive: false);
		}
	}

	/// <inheritdoc />
	public Task<string> ReadDatabaseJsonAsync(BookLocation location, CancellationToken cancellationToken = default)
		=> File.ReadAllTextAsync(Path.Combine(this.GetPath(location), DatabaseFileName), Utf8NoBom, cancellationToken);

	/// <inheritdoc />
	public async IAsyncEnumerable<ManagedAssetDescriptor> EnumerateManagedAssetsAsync(
		BookLocation location,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		foreach (SongFile file in database.SongFiles.OrderBy(file => file.Id))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string path = GetManagedPath(directory, file.RelativePath);
			FileInfo info = new(path);
			string hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
			yield return new(file.Id, file.RelativePath, info.Length, hash);
		}
	}

	/// <inheritdoc />
	public Task<Stream> OpenManagedAssetAsync(
		BookLocation location,
		Guid songFileId,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(File.ReadAllText(Path.Combine(directory, DatabaseFileName), Utf8NoBom));
		SongFile file = database.SongFiles.SingleOrDefault(file => file.Id == songFileId)
			?? throw new KeyNotFoundException("The managed asset does not exist.");
		Stream result = new FileStream(
			GetManagedPath(directory, file.RelativePath),
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 1,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return Task.FromResult(result);
	}

	/// <inheritdoc />
	public async Task<IStagedBookWrite> StageWriteAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		string directory = this.GetPath(location);
		string json = await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		ChordDatabase database = DatabaseJson.Deserialize(json);
		string stageDirectory = Path.Combine(
			Path.GetDirectoryName(directory)!,
			$".{Path.GetFileName(directory)}.chordbook-stage-{Guid.NewGuid():N}");
		Directory.CreateDirectory(stageDirectory);
		try
		{
			Dictionary<Guid, string> assets = [];
			foreach (SongFile file in database.SongFiles)
			{
				string source = GetManagedPath(directory, file.RelativePath);
				string stagedName = file.RelativePath;
				if (!File.Exists(source))
				{
					IReadOnlyList<string> renameCandidates =
					[
						.. Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
							.Where(path => PortableManagedFileName.TryGetSongFileId(Path.GetFileName(path), out Guid id) && id == file.Id),
					];
					if (renameCandidates.Count != 1)
					{
						throw new BookStoreValidationException($"Managed asset '{file.RelativePath}' is missing or has ambiguous rename candidates.");
					}

					source = renameCandidates[0];
					stagedName = Path.GetFileName(source);
				}

				string target = GetManagedPath(stageDirectory, stagedName);
				await CopyFileAsync(source, target, cancellationToken).ConfigureAwait(false);
				assets.Add(file.Id, stagedName);
			}

			IStagedBookWrite result = new StagedWrite(
				this,
				location,
				directory,
				stageDirectory,
				json,
				assets,
				HashText(json));
			return result;
		}
		catch
		{
			Directory.Delete(stageDirectory, recursive: true);
			throw;
		}
	}

	/// <inheritdoc />
	public Task<long?> GetAvailableSpaceAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string root = Path.GetPathRoot(this.GetPath(location))
			?? throw new BookStoreException("The book directory does not have a filesystem root.");
		return Task.FromResult<long?>(new DriveInfo(root).AvailableFreeSpace);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ExternalBookProblem>> InspectAsync(
		BookLocation location,
		CancellationToken cancellationToken = default)
	{
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		Dictionary<Guid, SongFile> tracked = database.SongFiles.ToDictionary(file => file.Id);
		Dictionary<Guid, string> observed = [];
		List<ExternalBookProblem> problems = [];
		foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileName(path);
			if (!StringComparer.OrdinalIgnoreCase.Equals(name, DatabaseFileName)
				&& PortableManagedFileName.TryGetSongFileId(name, out Guid fileId))
			{
				observed[fileId] = name;
				if (!tracked.ContainsKey(fileId))
				{
					problems.Add(new(name, "A GUID-suffixed file is an unaccepted external import candidate."));
				}
			}
		}

		foreach (SongFile file in database.SongFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string expectedPath = GetManagedPath(directory, file.RelativePath);
			if (!File.Exists(expectedPath))
			{
				if (observed.TryGetValue(file.Id, out string? renamed))
				{
					problems.Add(new(renamed, $"Managed file was externally renamed from '{file.RelativePath}'."));
				}
				else
				{
					problems.Add(new(file.RelativePath, "Managed file is missing; no deletion was inferred."));
				}
			}
			else
			{
				FileInfo info = new(expectedPath);
				if (file.ObservedLength != info.Length || file.ObservedWriteUtc?.UtcDateTime != info.LastWriteTimeUtc)
				{
					string hash = await HashFileAsync(expectedPath, cancellationToken).ConfigureAwait(false);
					if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
					{
						problems.Add(new(file.RelativePath, "Managed file content changed externally."));
					}
				}
			}
		}

		return problems;
	}

	/// <summary>Adopts unambiguous GUID-preserving renames and content edits, while only reporting missing or unknown files.</summary>
	public async Task<BookReconcileResult> ReconcileAsync(
		BookLocation location,
		Guid deviceId,
		CancellationToken cancellationToken = default)
	{
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
		Dictionary<Guid, List<string>> observed = [];
		foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
		{
			string name = Path.GetFileName(path);
			if (!StringComparer.OrdinalIgnoreCase.Equals(name, DatabaseFileName)
				&& PortableManagedFileName.TryGetSongFileId(name, out Guid fileId))
			{
				if (!observed.TryGetValue(fileId, out List<string>? names))
				{
					names = [];
					observed.Add(fileId, names);
				}

				names.Add(name);
			}
		}

		HashSet<Guid> tracked = [.. database.SongFiles.Select(file => file.Id)];
		List<ExternalBookProblem> problems = [];
		foreach ((Guid id, List<string> names) in observed.Where(pair => !tracked.Contains(pair.Key)))
		{
			_ = id;
			problems.AddRange(names.Select(name => new ExternalBookProblem(name, "A GUID-suffixed file is an unaccepted external import candidate.")));
		}

		int renamedCount = 0;
		int changedCount = 0;
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (SongFile file in database.SongFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			bool contentChanged = false;
			bool renamed = false;
			string expectedPath = GetManagedPath(directory, file.RelativePath);
			string actualName = file.RelativePath;
			if (!File.Exists(expectedPath))
			{
				if (observed.TryGetValue(file.Id, out List<string>? names) && names.Count == 1)
				{
					actualName = names[0];
					file.RelativePath = actualName;
					renamed = true;
					renamedCount++;
				}
				else
				{
					string message = names?.Count > 1
						? "Managed file has multiple GUID-matching rename candidates."
						: "Managed file is missing; no deletion was inferred.";
					problems.Add(new(file.RelativePath, message));
					continue;
				}
			}

			string actualPath = GetManagedPath(directory, actualName);
			FileInfo info = new(actualPath);
			if (file.ObservedLength != info.Length || file.ObservedWriteUtc?.UtcDateTime != info.LastWriteTimeUtc)
			{
				byte[] bytes = await File.ReadAllBytesAsync(actualPath, cancellationToken).ConfigureAwait(false);
				string hash = SongFileAnalyzer.Hash(bytes);
				if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
				{
					SongFileAnalysis analysis = SongFileAnalyzer.Analyze(bytes, actualName);
					ApplyAnalysis(database, file, analysis, deviceId, now);
					file.ContentHash = hash;
					file.ContentRevision++;
					contentChanged = true;
					changedCount++;
				}

				file.ObservedLength = info.Length;
				file.ObservedWriteUtc = info.LastWriteTimeUtc;
			}

			if (renamed && !contentChanged)
			{
				file.Revision = NextRevision(file.Revision, deviceId, now);
			}
		}

		if (renamedCount > 0 || changedCount > 0)
		{
			database.Revision = NextRevision(database.Revision, deviceId, now);
			await using IStagedBookWrite write = await this.StageWriteAsync(location, cancellationToken).ConfigureAwait(false);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken).ConfigureAwait(false);
			await write.CommitAsync(cancellationToken).ConfigureAwait(false);
		}

		return new(renamedCount, changedCount, problems);
	}

	#endregion

	#region Private Methods

	private static async Task CopyFileAsync(string source, string target, CancellationToken cancellationToken)
	{
		await using FileStream input = new(
			source,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		await using FileStream output = new(
			target,
			FileMode.CreateNew,
			FileAccess.Write,
			FileShare.None,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.WriteThrough);
		await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);
		output.Flush(flushToDisk: true);
		File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
	}

	private static void ApplyAnalysis(
		ChordDatabase database,
		SongFile file,
		SongFileAnalysis analysis,
		Guid deviceId,
		DateTimeOffset now)
	{
		file.MediaKind = analysis.MediaKind;
		file.SourceFormat = analysis.SourceFormat;
		file.TextEncoding = analysis.TextEncoding;
		file.ByteOrderMark = analysis.ByteOrderMark;
		Song song = database.Songs.Single(item => item.Id == file.SongId);
		if (analysis.Metadata.ContainsKey("title"))
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
		file.Revision = NextRevision(file.Revision, deviceId, now);
	}

	private static string GetManagedPath(string directory, string relativePath)
	{
		IReadOnlyList<string> problems = PortableManagedFileName.Validate(relativePath);
		if (problems.Count != 0)
		{
			throw new BookStoreValidationException(string.Join(" ", problems));
		}

		return Path.Combine(directory, relativePath);
	}

	private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(text))).ToLowerInvariant();

	private static RevisionStamp NextRevision(RevisionStamp current, Guid deviceId, DateTimeOffset now) => new()
	{
		Revision = current.Revision + 1,
		ModifiedUtc = now,
		DeviceId = deviceId,
	};

	private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static string SanitizeDirectoryName(string name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder result = new(name.Length);
		foreach (char character in name.Trim())
		{
			result.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
		}

		string value = result.ToString().TrimEnd(' ', '.');
		return string.IsNullOrEmpty(value) ? "ChordBook" : value;
	}

	private static async Task WriteTextDurablyAsync(string path, string text, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.CreateNew,
			FileAccess.Write,
			FileShare.None,
			BufferSize,
			FileOptions.Asynchronous | FileOptions.WriteThrough);
		byte[] bytes = Utf8NoBom.GetBytes(text);
		await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
		stream.Flush(flushToDisk: true);
	}

	private async Task CommitAsync(
		BookLocation location,
		string directory,
		string stageDirectory,
		string databaseJson,
		Dictionary<Guid, string> assets,
		string expectedDatabaseHash,
		CancellationToken cancellationToken)
	{
		ChordDatabase next;
		try
		{
			next = DatabaseJson.Deserialize(databaseJson);
		}
		catch (DatabaseFormatException exception)
		{
			throw new BookStoreValidationException("The staged database is invalid.", exception);
		}

		if (next.Id != location.Token)
		{
			throw new BookStoreValidationException("The staged database ID does not match the opened book.");
		}

		if (!next.SongFiles.Select(file => file.Id).ToHashSet().SetEquals(assets.Keys))
		{
			throw new BookStoreValidationException("The staged assets do not exactly match the database's managed song files.");
		}

		foreach (SongFile file in next.SongFiles)
		{
			if (!StringComparer.Ordinal.Equals(file.RelativePath, assets[file.Id]))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} has a path that does not match the database.");
			}

			string stagedPath = GetManagedPath(stageDirectory, file.RelativePath);
			string hash = await HashFileAsync(stagedPath, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(file.ContentHash) && !StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} does not match its content hash.");
			}
		}

		await this.commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			string currentJson = await File.ReadAllTextAsync(Path.Combine(directory, DatabaseFileName), Utf8NoBom, cancellationToken)
				.ConfigureAwait(false);
			if (!StringComparer.Ordinal.Equals(HashText(currentJson), expectedDatabaseHash))
			{
				throw new BookStoreConcurrencyException();
			}

			ChordDatabase current = DatabaseJson.Deserialize(currentJson);
			string rollback = Path.Combine(stageDirectory, ".rollback");
			Directory.CreateDirectory(rollback);
			await CopyFileAsync(Path.Combine(directory, DatabaseFileName), Path.Combine(rollback, DatabaseFileName), cancellationToken)
				.ConfigureAwait(false);
			foreach (SongFile file in current.SongFiles)
			{
				string path = GetManagedPath(directory, file.RelativePath);
				if (File.Exists(path))
				{
					await CopyFileAsync(path, GetManagedPath(rollback, file.RelativePath), cancellationToken).ConfigureAwait(false);
				}
			}

			this.faultInjector?.Invoke(FileSystemCommitStep.RollbackSnapshotCreated);

			try
			{
				foreach (SongFile file in next.SongFiles)
				{
					await ReplaceFromStageAsync(
						GetManagedPath(stageDirectory, file.RelativePath),
						GetManagedPath(directory, file.RelativePath),
						cancellationToken).ConfigureAwait(false);
					this.faultInjector?.Invoke(FileSystemCommitStep.ManagedAssetReplaced);
				}

				await ReplaceTextAsync(Path.Combine(directory, DatabaseFileName), DatabaseJson.Serialize(next), cancellationToken)
					.ConfigureAwait(false);
				this.faultInjector?.Invoke(FileSystemCommitStep.DatabaseReplaced);
				HashSet<string> nextPaths = new(next.SongFiles.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
				foreach (SongFile oldFile in current.SongFiles.Where(file => !nextPaths.Contains(file.RelativePath)))
				{
					File.Delete(GetManagedPath(directory, oldFile.RelativePath));
				}
			}
			catch
			{
				await RestoreAsync(directory, rollback, current, next).ConfigureAwait(false);
				throw;
			}
		}
		finally
		{
			this.commitLock.Release();
		}
	}

#pragma warning disable SA1204 // Keeping transaction helpers adjacent makes the commit/rollback flow auditable.
	private static async Task ReplaceFromStageAsync(string source, string target, CancellationToken cancellationToken)
	{
		string temporary = target + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await CopyFileAsync(source, temporary, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, target, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	private static async Task ReplaceTextAsync(string target, string text, CancellationToken cancellationToken)
	{
		string temporary = target + $".{Guid.NewGuid():N}.tmp";
		try
		{
			await WriteTextDurablyAsync(temporary, text, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, target, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	private static async Task RestoreAsync(
		string directory,
		string rollback,
		ChordDatabase current,
		ChordDatabase attempted)
	{
		foreach (SongFile file in current.SongFiles)
		{
			string backup = GetManagedPath(rollback, file.RelativePath);
			if (File.Exists(backup))
			{
				await ReplaceFromStageAsync(backup, GetManagedPath(directory, file.RelativePath), CancellationToken.None).ConfigureAwait(false);
			}
		}

		HashSet<string> currentPaths = new(current.SongFiles.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
		foreach (SongFile file in attempted.SongFiles.Where(file => !currentPaths.Contains(file.RelativePath)))
		{
			File.Delete(GetManagedPath(directory, file.RelativePath));
		}

		string databaseBackup = Path.Combine(rollback, DatabaseFileName);
		await ReplaceFromStageAsync(databaseBackup, Path.Combine(directory, DatabaseFileName), CancellationToken.None).ConfigureAwait(false);
	}
#pragma warning restore SA1204

	private string GetPath(BookLocation location)
	{
		if (location.StoreId != this.storeId)
		{
			throw new ArgumentException("The opaque location belongs to another book store.", nameof(location));
		}

		return this.paths.TryGetValue(location.Token, out string? result)
			? result
			: throw new KeyNotFoundException("The book is not open in this store.");
	}

	private string GetUnusedBookDirectory(string name)
	{
		string baseName = SanitizeDirectoryName(name);
		string result = Path.Combine(this.rootDirectory, baseName);
		for (int suffix = 2; Directory.Exists(result) || File.Exists(result); suffix++)
		{
			result = Path.Combine(this.rootDirectory, $"{baseName} ({suffix})");
		}

		return result;
	}

	private BookLocation Register(string directory, Guid databaseId)
	{
		if (this.paths.TryGetValue(databaseId, out string? existing)
			&& !StringComparer.OrdinalIgnoreCase.Equals(existing, directory))
		{
			throw new BookStoreException($"Book ID {databaseId} is already open at a different directory.");
		}

		this.paths[databaseId] = directory;
		return new(this.storeId, databaseId);
	}

	#endregion

	#region Private Types

	private sealed class StagedWrite : IStagedBookWrite
	{
		private readonly FileSystemBookStore owner;
		private readonly BookLocation location;
		private readonly string directory;
		private readonly string stageDirectory;
		private readonly string expectedDatabaseHash;
		private Dictionary<Guid, string>? assets;
		private string databaseJson;

		public StagedWrite(
			FileSystemBookStore owner,
			BookLocation location,
			string directory,
			string stageDirectory,
			string databaseJson,
			Dictionary<Guid, string> assets,
			string expectedDatabaseHash)
		{
			this.owner = owner;
			this.location = location;
			this.directory = directory;
			this.stageDirectory = stageDirectory;
			this.databaseJson = databaseJson;
			this.assets = assets;
			this.expectedDatabaseHash = expectedDatabaseHash;
		}

		public Task WriteDatabaseJsonAsync(string json, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			this.EnsureActive();
			this.databaseJson = json;
			return Task.CompletedTask;
		}

		public async Task WriteManagedAssetAsync(
			Guid songFileId,
			string relativePath,
			Stream content,
			CancellationToken cancellationToken = default)
		{
			Dictionary<Guid, string> activeAssets = this.EnsureActive();
			string target = GetManagedPath(this.stageDirectory, relativePath);
			if (activeAssets.TryGetValue(songFileId, out string? oldPath)
				&& !PortableManagedFileName.Comparer.Equals(oldPath, relativePath))
			{
				File.Delete(GetManagedPath(this.stageDirectory, oldPath));
			}

			await using FileStream output = new(
				target,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				BufferSize,
				FileOptions.Asynchronous | FileOptions.WriteThrough);
			await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
			await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			output.Flush(flushToDisk: true);
			activeAssets[songFileId] = relativePath;
		}

		public Task RenameManagedAssetAsync(
			Guid songFileId,
			string relativePath,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<Guid, string> activeAssets = this.EnsureActive();
			if (!activeAssets.TryGetValue(songFileId, out string? oldPath))
			{
				throw new KeyNotFoundException("The managed asset does not exist.");
			}

			string target = GetManagedPath(this.stageDirectory, relativePath);
			File.Move(GetManagedPath(this.stageDirectory, oldPath), target);
			activeAssets[songFileId] = relativePath;
			return Task.CompletedTask;
		}

		public Task DeleteManagedAssetAsync(Guid songFileId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<Guid, string> activeAssets = this.EnsureActive();
			if (!activeAssets.Remove(songFileId, out string? path))
			{
				throw new KeyNotFoundException("The managed asset does not exist.");
			}

			File.Delete(GetManagedPath(this.stageDirectory, path));
			return Task.CompletedTask;
		}

		public async Task CommitAsync(CancellationToken cancellationToken = default)
		{
			Dictionary<Guid, string> activeAssets = this.EnsureActive();
			await this.owner.CommitAsync(
				this.location,
				this.directory,
				this.stageDirectory,
				this.databaseJson,
				activeAssets,
				this.expectedDatabaseHash,
				cancellationToken).ConfigureAwait(false);
			this.assets = null;
			Directory.Delete(this.stageDirectory, recursive: true);
		}

		public ValueTask DisposeAsync()
		{
			this.assets = null;
			if (Directory.Exists(this.stageDirectory))
			{
				Directory.Delete(this.stageDirectory, recursive: true);
			}

			return ValueTask.CompletedTask;
		}

		private Dictionary<Guid, string> EnsureActive() => this.assets
			?? throw new InvalidOperationException("The staged write has already completed or been disposed.");
	}

	#endregion
}
