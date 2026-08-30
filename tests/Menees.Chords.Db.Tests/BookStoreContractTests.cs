#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

/// <summary>Runs the portable store contract against every shipping implementation.</summary>
[TestClass]
public sealed class BookStoreContractTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	[DataRow("memory")]
	[DataRow("filesystem")]
	public async Task CreateCommitReadAndDeleteRoundTrips(string storeKind)
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		await using StoreFixture fixture = CreateFixture(storeKind);
		IBookStore store = fixture.Store;
		BookLocation location = await store.CreateBookAsync("Contract Book", Guid.NewGuid(), cancellationToken);
		(await store.ExistsAsync(location, cancellationToken)).ShouldBeTrue();
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddAsset(database);

		await CommitAsync(store, location, database, cancellationToken);

		ChordDatabase committed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		committed.Songs.Single().Title.ShouldBe("Contract Song");
		List<ManagedAssetDescriptor> assets = [];
		await foreach (ManagedAssetDescriptor asset in store.EnumerateManagedAssetsAsync(location, cancellationToken))
		{
			assets.Add(asset);
		}

		assets.Single().ContentHash.ShouldBe(database.SongFiles.Single().ContentHash);
		using Stream stream = await store.OpenManagedAssetAsync(location, assets.Single().SongFileId, cancellationToken);
		using MemoryStream copy = new();
		await stream.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
		await store.DeleteBookAsync(location, cancellationToken);
		(await store.ExistsAsync(location, cancellationToken)).ShouldBeFalse();
	}

	[TestMethod]
	[DataRow("memory")]
	[DataRow("filesystem")]
	public async Task InvalidOrAbandonedWritesRemainInvisible(string storeKind)
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		await using StoreFixture fixture = CreateFixture(storeKind);
		IBookStore store = fixture.Store;
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		string original = await store.ReadDatabaseJsonAsync(location, cancellationToken);
		ChordDatabase invalid = DatabaseJson.Deserialize(original);
		AddAsset(invalid);

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(invalid), cancellationToken);
			await Should.ThrowAsync<BookStoreValidationException>(() => write.CommitAsync(cancellationToken));
		}

		(await store.ReadDatabaseJsonAsync(location, cancellationToken)).ShouldBe(original);
		await using (IStagedBookWrite abandoned = await store.StageWriteAsync(location, cancellationToken))
		{
			ChordDatabase changed = DatabaseJson.Deserialize(original);
			changed.Name = "Invisible";
			await abandoned.WriteDatabaseJsonAsync(DatabaseJson.Serialize(changed), cancellationToken);
		}

		(await store.ReadDatabaseJsonAsync(location, cancellationToken)).ShouldBe(original);
	}

	[TestMethod]
	[DataRow("memory")]
	[DataRow("filesystem")]
	public async Task StaleWriterCannotReplaceNewerCommit(string storeKind)
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		await using StoreFixture fixture = CreateFixture(storeKind);
		IBookStore store = fixture.Store;
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		await using IStagedBookWrite first = await store.StageWriteAsync(location, cancellationToken);
		await using IStagedBookWrite stale = await store.StageWriteAsync(location, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		database.Name = "Winner";
		await first.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await first.CommitAsync(cancellationToken);
		database.Name = "Loser";
		await stale.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);

		await Should.ThrowAsync<BookStoreConcurrencyException>(() => stale.CommitAsync(cancellationToken));

		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken)).Name.ShouldBe("Winner");
	}

	[TestMethod]
	[DataRow("memory")]
	[DataRow("filesystem")]
	public async Task OpaqueLocationsAreStoreSpecific(string storeKind)
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		await using StoreFixture first = CreateFixture(storeKind);
		await using StoreFixture second = CreateFixture(storeKind);
		BookLocation location = await first.Store.CreateBookAsync("First", Guid.NewGuid(), cancellationToken);

		await Should.ThrowAsync<ArgumentException>(() => second.Store.ReadDatabaseJsonAsync(location, cancellationToken));
	}

	#endregion

	#region Private Methods

	private static void AddAsset(ChordDatabase database)
	{
		byte[] content = TestData.OpenSongBytes();
		Song song = new()
		{
			Id = Guid.CreateVersion7(),
			Title = "Contract Song",
			Revision = RevisionStamp.Initial(Guid.NewGuid()),
		};
		Guid fileId = Guid.CreateVersion7();
		database.Songs.Add(song);
		database.SongFiles.Add(new SongFile
		{
			Id = fileId,
			SongId = song.Id,
			RelativePath = PortableManagedFileName.Create(song.Title, fileId, ".cho"),
			MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.ChordPro,
			ContentHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
			ObservedLength = content.Length,
			ContentRevision = 1,
			Revision = RevisionStamp.Initial(Guid.NewGuid()),
		});
	}

	private static async Task CommitAsync(
		IBookStore store,
		BookLocation location,
		ChordDatabase database,
		CancellationToken cancellationToken)
	{
		await using IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken);
		SongFile file = database.SongFiles.Single();
		await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(TestData.OpenSongBytes()), cancellationToken);
		await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await write.CommitAsync(cancellationToken);
	}

	private static StoreFixture CreateFixture(string storeKind)
	{
		StoreFixture result;
		if (storeKind == "memory")
		{
			result = new StoreFixture(new InMemoryBookStore(), null);
		}
		else
		{
			string directory = Path.Combine(Path.GetTempPath(), nameof(BookStoreContractTests), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			result = new StoreFixture(new FileSystemBookStore(directory), directory);
		}

		return result;
	}

	#endregion

	#region Private Types

	private sealed class StoreFixture(IBookStore store, string? directory) : IAsyncDisposable
	{
		public IBookStore Store { get; } = store;

		public ValueTask DisposeAsync()
		{
			(this.Store as IDisposable)?.Dispose();
			if (directory is not null && Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}

			return ValueTask.CompletedTask;
		}
	}

	#endregion
}
