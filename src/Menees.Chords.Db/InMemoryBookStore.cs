#region Using Directives

using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db;

/// <summary>Provides a concurrency-safe, atomic in-memory book store for tests and transient workflows.</summary>
public sealed class InMemoryBookStore : IBookStore
{
	#region Private Data

	private readonly Lock syncRoot = new();
	private readonly Guid storeId = Guid.NewGuid();
	private readonly Dictionary<Guid, BookState> books = [];

	#endregion

	#region Public API

	/// <inheritdoc />
	public BookStoreCapabilities Capabilities => BookStoreCapabilities.AtomicReplace;

	/// <inheritdoc />
	public Task<BookLocation> CreateBookAsync(string name, Guid deviceId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ChordDatabase database = ChordDatabase.Create(name, deviceId);
		BookLocation location = new(this.storeId, database.Id);
		lock (this.syncRoot)
		{
			this.books.Add(location.Token, new BookState(DatabaseJson.Serialize(database), [], 0));
		}

		return Task.FromResult(location);
	}

	/// <inheritdoc />
	public Task<bool> ExistsAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		this.ValidateLocation(location);
		lock (this.syncRoot)
		{
			return Task.FromResult(this.books.ContainsKey(location.Token));
		}
	}

	/// <inheritdoc />
	public Task DeleteBookAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		this.ValidateLocation(location);
		lock (this.syncRoot)
		{
			if (!this.books.Remove(location.Token))
			{
				throw new KeyNotFoundException("The book does not exist.");
			}
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task<string> ReadDatabaseJsonAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (this.syncRoot)
		{
			return Task.FromResult(this.GetState(location).DatabaseJson);
		}
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<ManagedAssetDescriptor> EnumerateManagedAssetsAsync(
		BookLocation location,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		List<ManagedAssetDescriptor> descriptors;
		lock (this.syncRoot)
		{
			BookState state = this.GetState(location);
			descriptors = [.. state.Assets.OrderBy(pair => pair.Key).Select(pair => new ManagedAssetDescriptor(
				pair.Key,
				pair.Value.RelativePath,
				pair.Value.Content.LongLength,
				Convert.ToHexString(SHA256.HashData(pair.Value.Content)).ToLowerInvariant()))];
		}

		foreach (ManagedAssetDescriptor descriptor in descriptors)
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return descriptor;
			await Task.Yield();
		}
	}

	/// <inheritdoc />
	public Task<Stream> OpenManagedAssetAsync(
		BookLocation location,
		Guid songFileId,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (this.syncRoot)
		{
			BookState state = this.GetState(location);
			if (!state.Assets.TryGetValue(songFileId, out AssetState? asset))
			{
				throw new KeyNotFoundException("The managed asset does not exist.");
			}

			return Task.FromResult<Stream>(new MemoryStream([.. asset.Content], writable: false));
		}
	}

	/// <inheritdoc />
	public Task<IStagedBookWrite> StageWriteAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (this.syncRoot)
		{
			BookState state = this.GetState(location);
			Dictionary<Guid, AssetState> assets = state.Assets.ToDictionary(
				pair => pair.Key,
				pair => new AssetState(pair.Value.RelativePath, [.. pair.Value.Content]));
			IStagedBookWrite result = new StagedWrite(this, location, state.DatabaseJson, assets, state.Version);
			return Task.FromResult(result);
		}
	}

	/// <inheritdoc />
	public Task<long?> GetAvailableSpaceAsync(BookLocation location, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (this.syncRoot)
		{
			_ = this.GetState(location);
		}

		return Task.FromResult<long?>(null);
	}

	#endregion

	#region Private Methods

	private void Commit(BookLocation location, string databaseJson, Dictionary<Guid, AssetState> assets, long expectedVersion)
	{
		ChordDatabase database;
		try
		{
			database = DatabaseJson.Deserialize(databaseJson);
		}
		catch (DatabaseFormatException exception)
		{
			throw new BookStoreValidationException("The staged database is invalid.", exception);
		}

		if (database.Id != location.Token)
		{
			throw new BookStoreValidationException("The staged database ID does not match the opened book.");
		}

		HashSet<Guid> referencedIds = [.. database.SongFiles.Select(file => file.Id)];
		if (!referencedIds.SetEquals(assets.Keys))
		{
			throw new BookStoreValidationException("The staged assets do not exactly match the database's managed song files.");
		}

		foreach (SongFile file in database.SongFiles)
		{
			AssetState asset = assets[file.Id];
			if (!StringComparer.Ordinal.Equals(file.RelativePath, asset.RelativePath))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} has a path that does not match the database.");
			}

			string hash = Convert.ToHexString(SHA256.HashData(asset.Content)).ToLowerInvariant();
			if (!string.IsNullOrEmpty(file.ContentHash) && !StringComparer.OrdinalIgnoreCase.Equals(file.ContentHash, hash))
			{
				throw new BookStoreValidationException($"Asset {file.Id:D} does not match its content hash.");
			}
		}

		string canonicalJson = DatabaseJson.Serialize(database);
		lock (this.syncRoot)
		{
			BookState current = this.GetState(location);
			if (current.Version != expectedVersion)
			{
				throw new BookStoreConcurrencyException();
			}

			this.books[location.Token] = new BookState(canonicalJson, assets, current.Version + 1);
		}
	}

	private BookState GetState(BookLocation location)
	{
		this.ValidateLocation(location);
		if (!this.books.TryGetValue(location.Token, out BookState? result))
		{
			throw new KeyNotFoundException("The book does not exist.");
		}

		return result;
	}

	private void ValidateLocation(BookLocation location)
	{
		if (location.StoreId != this.storeId)
		{
			throw new ArgumentException("The opaque location belongs to another book store.", nameof(location));
		}
	}

	#endregion

	#region Private Types

	private sealed record AssetState(string RelativePath, byte[] Content);

	private sealed record BookState(string DatabaseJson, Dictionary<Guid, AssetState> Assets, long Version);

	private sealed class StagedWrite : IStagedBookWrite
	{
		private readonly InMemoryBookStore owner;
		private readonly BookLocation location;
		private readonly long expectedVersion;
		private Dictionary<Guid, AssetState>? assets;
		private string databaseJson;

		public StagedWrite(
			InMemoryBookStore owner,
			BookLocation location,
			string databaseJson,
			Dictionary<Guid, AssetState> assets,
			long expectedVersion)
		{
			this.owner = owner;
			this.location = location;
			this.databaseJson = databaseJson;
			this.assets = assets;
			this.expectedVersion = expectedVersion;
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
			Dictionary<Guid, AssetState> activeAssets = this.EnsureActive();
			IReadOnlyList<string> problems = PortableManagedFileName.Validate(relativePath);
			if (problems.Count != 0)
			{
				throw new ArgumentException(string.Join(" ", problems), nameof(relativePath));
			}

			using MemoryStream buffer = new();
			await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
			activeAssets[songFileId] = new AssetState(relativePath, buffer.ToArray());
		}

		public Task RenameManagedAssetAsync(
			Guid songFileId,
			string relativePath,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<Guid, AssetState> activeAssets = this.EnsureActive();
			IReadOnlyList<string> problems = PortableManagedFileName.Validate(relativePath);
			if (problems.Count != 0)
			{
				throw new ArgumentException(string.Join(" ", problems), nameof(relativePath));
			}

			if (!activeAssets.TryGetValue(songFileId, out AssetState? asset))
			{
				throw new KeyNotFoundException("The managed asset does not exist.");
			}

			activeAssets[songFileId] = asset with { RelativePath = relativePath };
			return Task.CompletedTask;
		}

		public Task DeleteManagedAssetAsync(Guid songFileId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!this.EnsureActive().Remove(songFileId))
			{
				throw new KeyNotFoundException("The managed asset does not exist.");
			}

			return Task.CompletedTask;
		}

		public Task CommitAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<Guid, AssetState> activeAssets = this.EnsureActive();
			this.owner.Commit(this.location, this.databaseJson, activeAssets, this.expectedVersion);
			this.assets = null;
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync()
		{
			this.assets = null;
			return ValueTask.CompletedTask;
		}

		private Dictionary<Guid, AssetState> EnsureActive() => this.assets
			?? throw new InvalidOperationException("The staged write has already completed or been disposed.");
	}

	#endregion
}
