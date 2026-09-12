#region Using Directives

using System.Collections.Concurrent;
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
	private readonly ConcurrentDictionary<Guid, AssetPathIndex> assetPaths = new();
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
		using (FileStream lease = AcquireBookLease(fullPath))
		{
			foreach (string stage in Directory.EnumerateDirectories(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.chordbook-stage-*"))
			{
				AssetMoveJournal.Recover(fullPath, stage);
			}
		}

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
		using (FileStream lease = AcquireBookLease(directory))
		{
			ChordDatabase database = DatabaseJson.Deserialize(await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false));
			foreach (SongFile file in database.SongFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				File.Delete(GetManagedPath(directory, file.RelativePath));
			}

			File.Delete(Path.Combine(directory, DatabaseFileName));
		}

		File.Delete(Path.Combine(directory, ".write-lock"));
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
			yield return new(file.Id, file.RelativePath, file.ObservedLength ?? 0, file.ContentHash);
		}
	}

	/// <inheritdoc />
	public async Task<Stream> OpenManagedAssetAsync(
		BookLocation location,
		Guid songFileId,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string directory = this.GetPath(location);
		string databasePath = Path.Combine(directory, DatabaseFileName);
		FileInfo info = new(databasePath);
		if (!this.assetPaths.TryGetValue(location.Token, out AssetPathIndex? index)
			|| index.Length != info.Length || index.ModifiedUtc != info.LastWriteTimeUtc)
		{
			string json = await File.ReadAllTextAsync(databasePath, Utf8NoBom, cancellationToken).ConfigureAwait(false);
			ChordDatabase database = DatabaseJson.Deserialize(json);
			index = new(info.Length, info.LastWriteTimeUtc, database.SongFiles.ToDictionary(file => file.Id, file => file.RelativePath));
			this.assetPaths[location.Token] = index;
		}

		string path = index.Paths.TryGetValue(songFileId, out string? relativePath) ? GetManagedPath(directory, relativePath)
			: throw new KeyNotFoundException("The managed asset does not exist.");
		Stream result = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 1,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return result;
	}

	/// <inheritdoc />
	public async Task CommitMetadataAsync(BookLocation location, string expectedJson, string updatedJson, CancellationToken cancellationToken = default)
	{
		_ = this.GetPath(location);
		MetadataCommit.Validate(location, expectedJson, updatedJson);
		await this.CommitReconciledMetadataAsync(location, expectedJson, updatedJson, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<string> CommitMetadataAsync(
		BookLocation location,
		string expectedJson,
		ChordDatabase database,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		MetadataCommit.Validate(location, expectedJson, database);
		string json = DatabaseJson.Serialize(database);
		await this.CommitReconciledMetadataAsync(location, expectedJson, json, cancellationToken).ConfigureAwait(false);
		return json;
	}

	/// <inheritdoc />
	public async Task<IStagedBookWrite> StageWriteAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		string json = await this.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		return await this.StageWriteAsync(location, json, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public Task<IStagedBookWrite> StageWriteAsync(BookLocation location, string json, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string directory = this.GetPath(location);
		ChordDatabase database = DatabaseJson.Deserialize(json);
		string stageDirectory = Path.Combine(
			Path.GetDirectoryName(directory)!,
			$".{Path.GetFileName(directory)}.chordbook-stage-{Guid.NewGuid():N}");
		Directory.CreateDirectory(stageDirectory);
		Dictionary<Guid, string> assets = database.SongFiles.ToDictionary(file => file.Id, file => file.RelativePath);
		return Task.FromResult<IStagedBookWrite>(new StagedWrite(this, location, directory, stageDirectory, json, assets, json));
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

	/// <summary>Reviews external changes without saving metadata or changing source files.</summary>
	public Task<BookReconcilePreview> PreviewReconcileAsync(BookLocation location, Guid deviceId, CancellationToken cancellationToken = default)
		=> new FileSystemBookReconciler(this).PreviewAsync(location, deviceId, cancellationToken);

	/// <summary>Applies a reviewed snapshot, retaining catalog conflicts unless explicitly selected.</summary>
	public Task<BookReconcileResult> ApplyReconcileAsync(
		BookReconcilePreview preview,
		IReadOnlySet<string>? useSourceValues = null,
		CancellationToken cancellationToken = default)
		=> new FileSystemBookReconciler(this).ApplyAsync(preview, useSourceValues, cancellationToken);

	/// <summary>Adopts external changes while retaining independently edited catalog metadata.</summary>
	public async Task<BookReconcileResult> ReconcileAsync(BookLocation location, Guid deviceId, CancellationToken cancellationToken = default)
	{
		BookReconcilePreview preview = await this.PreviewReconcileAsync(location, deviceId, cancellationToken).ConfigureAwait(false);
		return await this.ApplyReconcileAsync(preview, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	internal Task CommitReconciliationAsync(BookLocation location, string expectedJson, string updatedJson, CancellationToken cancellationToken)
		=> this.CommitReconciledMetadataAsync(location, expectedJson, updatedJson, cancellationToken);

	#endregion

	#region Private Methods

	private static FileStream AcquireBookLease(string directory)
	{
		try
		{
			return new FileStream(Path.Combine(directory, ".write-lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch (IOException)
		{
			throw new BookStoreConcurrencyException();
		}
	}

	private static async Task<string> WriteAssetContentAsync(string target, Stream content, CancellationToken cancellationToken)
	{
		string temporary = target + ".writing";
		byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		try
		{
			await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
			{
				int read;
				while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
				{
					hash.AppendData(buffer, 0, read);
					await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				}

				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				output.Flush(flushToDisk: true);
			}

			File.Move(temporary, target, overwrite: true);
			return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
		}
		finally
		{
			System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
			File.Delete(temporary);
		}
	}

	private static List<SongFile> GetInstalledFiles(ChordDatabase next, Dictionary<Guid, SongFile> previous, Dictionary<Guid, string> writes)
		=> [.. next.SongFiles.Where(file => writes.ContainsKey(file.Id)
			|| !previous.TryGetValue(file.Id, out SongFile? old) || !StringComparer.Ordinal.Equals(file.RelativePath, old.RelativePath))];

	private static string GetManagedPath(string directory, string relativePath)
	{
		IReadOnlyList<string> problems = PortableManagedFileName.Validate(relativePath);
		if (problems.Count != 0)
		{
			throw new BookStoreValidationException(string.Join(" ", problems));
		}

		return Path.Combine(directory, relativePath);
	}

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

	private async Task CommitReconciledMetadataAsync(BookLocation location, string expectedJson, string updatedJson, CancellationToken cancellationToken)
	{
		string directory = this.GetPath(location);
		await this.commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			using FileStream lease = AcquireBookLease(directory);
			string target = Path.Combine(directory, DatabaseFileName);
			string current = await File.ReadAllTextAsync(target, Utf8NoBom, cancellationToken).ConfigureAwait(false);
			if (!StringComparer.Ordinal.Equals(current, expectedJson))
			{
				throw new BookStoreConcurrencyException();
			}

			// Only the small JSON file is flushed and atomically replaced. Song bytes are never opened.
			await ReplaceTextAsync(target, updatedJson, cancellationToken).ConfigureAwait(false);
			this.assetPaths.TryRemove(location.Token, out _);
		}
		finally
		{
			this.commitLock.Release();
		}
	}

	private void InstallAssets(
		string directory,
		string stageDirectory,
		string rollback,
		List<SongFile> installed,
		Dictionary<Guid, SongFile> previousFiles,
		Dictionary<Guid, string> writtenHashes,
		AssetMoveJournal journal,
		CancellationToken cancellationToken)
	{
		foreach (SongFile file in installed)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string source = writtenHashes.ContainsKey(file.Id) ? GetManagedPath(stageDirectory, file.RelativePath)
				: GetManagedPath(rollback, previousFiles[file.Id].RelativePath);
			string target = GetManagedPath(directory, file.RelativePath);
			if (writtenHashes.ContainsKey(file.Id) && file.ObservedWriteUtc is DateTimeOffset modified)
			{
				File.SetLastWriteTimeUtc(source, modified.UtcDateTime);
			}

			journal.Move(source, target);
			this.faultInjector?.Invoke(FileSystemCommitStep.ManagedAssetReplaced);
		}
	}

	private async Task CommitAsync(
		BookLocation location,
		string directory,
		string stageDirectory,
		string databaseJson,
		Dictionary<Guid, string> assets,
		Dictionary<Guid, string> writtenHashes,
		string expectedDatabaseJson,
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

		if (next.Id != location.Token || !next.SongFiles.Select(file => file.Id).ToHashSet().SetEquals(assets.Keys))
		{
			throw new BookStoreValidationException("The staged database identity or asset set does not match the transaction.");
		}

		ChordDatabase current = DatabaseJson.Deserialize(expectedDatabaseJson);
		Dictionary<Guid, SongFile> previousFiles = current.SongFiles.ToDictionary(file => file.Id);
		foreach (SongFile file in next.SongFiles)
		{
			if (!StringComparer.Ordinal.Equals(file.RelativePath, assets[file.Id]))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} has a path that does not match the database.");
			}

			string hash = writtenHashes.TryGetValue(file.Id, out string? writtenHash) ? writtenHash
				: previousFiles.TryGetValue(file.Id, out SongFile? original) ? original.ContentHash
				: throw new BookStoreValidationException("A new asset requires content.");
			if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.ContentHash))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} does not match its content hash.");
			}
		}

		Dictionary<Guid, SongFile> nextFiles = next.SongFiles.ToDictionary(file => file.Id);
		List<SongFile> affected = [.. current.SongFiles.Where(file => writtenHashes.ContainsKey(file.Id)
			|| !nextFiles.TryGetValue(file.Id, out SongFile? nextFile) || !StringComparer.Ordinal.Equals(file.RelativePath, nextFile.RelativePath))];
		await this.commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			using FileStream lease = AcquireBookLease(directory);
			string targetJson = Path.Combine(directory, DatabaseFileName);
			string currentJson = await File.ReadAllTextAsync(targetJson, Utf8NoBom, cancellationToken).ConfigureAwait(false);
			if (!StringComparer.Ordinal.Equals(currentJson, expectedDatabaseJson))
			{
				throw new BookStoreConcurrencyException();
			}

			HashSet<string> affectedPaths = new(affected.Select(file => file.RelativePath), PortableManagedFileName.Comparer);
			List<SongFile> installed = GetInstalledFiles(next, previousFiles, writtenHashes);
			foreach (SongFile file in installed)
			{
				if (!affectedPaths.Contains(file.RelativePath) && File.Exists(GetManagedPath(directory, file.RelativePath)))
				{
					throw new BookStoreValidationException("An asset destination already exists outside this transaction.");
				}
			}

			string rollback = Directory.CreateDirectory(Path.Combine(stageDirectory, ".rollback")).FullName;
			using AssetMoveJournal journal = new(directory, stageDirectory, currentJson, databaseJson);
			bool databaseReplaced = false;
			try
			{
				foreach (SongFile file in affected)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string source = GetManagedPath(directory, file.RelativePath);
					if (File.Exists(source))
					{
						string target = GetManagedPath(rollback, file.RelativePath);
						journal.Move(source, target);
					}
				}

				this.faultInjector?.Invoke(FileSystemCommitStep.RollbackSnapshotCreated);
				this.InstallAssets(directory, stageDirectory, rollback, installed, previousFiles, writtenHashes, journal, cancellationToken);

				await ReplaceTextAsync(targetJson, databaseJson, cancellationToken).ConfigureAwait(false);
				this.assetPaths.TryRemove(location.Token, out _);
				databaseReplaced = true;
				this.faultInjector?.Invoke(FileSystemCommitStep.DatabaseReplaced);
				journal.Complete();
			}
			catch
			{
				if (databaseReplaced)
				{
					await ReplaceTextAsync(targetJson, currentJson, CancellationToken.None).ConfigureAwait(false);
				}

				journal.Rollback();
				journal.Discard();
				throw;
			}
		}
		finally
		{
			this.commitLock.Release();
		}
	}

#pragma warning disable SA1204 // Keeping transaction helpers adjacent makes the commit/rollback flow auditable.
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

		this.assetPaths.TryRemove(databaseId, out _);
		this.paths[databaseId] = directory;
		return new(this.storeId, databaseId);
	}

	#endregion

	#region Private Types

	private sealed record AssetPathIndex(long Length, DateTime ModifiedUtc, Dictionary<Guid, string> Paths);

	private sealed class StagedWrite : IStagedBookWrite
	{
		private readonly FileSystemBookStore owner;
		private readonly BookLocation location;
		private readonly string directory;
		private readonly string stageDirectory;
		private readonly string expectedDatabaseJson;
		private readonly Dictionary<Guid, string> writtenHashes = [];
		private Dictionary<Guid, string>? assets;
		private string databaseJson;

		public StagedWrite(
			FileSystemBookStore owner,
			BookLocation location,
			string directory,
			string stageDirectory,
			string databaseJson,
			Dictionary<Guid, string> assets,
			string expectedDatabaseJson)
		{
			this.owner = owner;
			this.location = location;
			this.directory = directory;
			this.stageDirectory = stageDirectory;
			this.databaseJson = databaseJson;
			this.assets = assets;
			this.expectedDatabaseJson = expectedDatabaseJson;
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
			string hash = await WriteAssetContentAsync(target, content, cancellationToken).ConfigureAwait(false);
			if (activeAssets.TryGetValue(songFileId, out string? oldPath)
				&& !PortableManagedFileName.Comparer.Equals(oldPath, relativePath))
			{
				File.Delete(GetManagedPath(this.stageDirectory, oldPath));
			}

			this.writtenHashes[songFileId] = hash;
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
			if (this.writtenHashes.ContainsKey(songFileId) && !StringComparer.Ordinal.Equals(oldPath, relativePath))
			{
				File.Move(GetManagedPath(this.stageDirectory, oldPath), target);
			}

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
			this.writtenHashes.Remove(songFileId);
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
				this.writtenHashes,
				this.expectedDatabaseJson,
				cancellationToken).ConfigureAwait(false);
			this.assets = null;
			Directory.Delete(this.stageDirectory, recursive: true);
		}

		public ValueTask DisposeAsync()
		{
			this.assets = null;
			if (Directory.Exists(this.stageDirectory) && !File.Exists(Path.Combine(this.stageDirectory, AssetMoveJournal.FileName)))
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
