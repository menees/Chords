#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class InMemoryBookStoreTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task ValidAssetAndDatabaseCommitTogether()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		Guid deviceId = Guid.NewGuid();
		BookLocation location = await store.CreateBookAsync("Test Book", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		AddOpenSong(database);

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			SongFile file = database.SongFiles.Single();
			await write.WriteManagedAssetAsync(file.Id, file.RelativePath, new MemoryStream(TestData.OpenSongBytes()), cancellationToken);
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
			await write.CommitAsync(cancellationToken);
		}

		ChordDatabase committed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		committed.SongFiles.Single().SourceFormat.ShouldBe(SourceFormat.OpenSongXml);
		List<ManagedAssetDescriptor> assets = [];
		await foreach (ManagedAssetDescriptor asset in store.EnumerateManagedAssetsAsync(location, cancellationToken))
		{
			assets.Add(asset);
		}

		assets.Single().RelativePath.ShouldNotContain('.');
		using Stream content = await store.OpenManagedAssetAsync(location, committed.SongFiles.Single().Id, cancellationToken);
		using MemoryStream copy = new();
		await content.CopyToAsync(copy, cancellationToken);
		copy.ToArray().ShouldBe(TestData.OpenSongBytes());
	}

	[TestMethod]
	public async Task FailedCommitLeavesPriorDatabaseAndAssetsUntouched()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		string originalJson = await store.ReadDatabaseJsonAsync(location, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(originalJson);
		AddOpenSong(database);

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
			await Should.ThrowAsync<BookStoreValidationException>(() => write.CommitAsync(cancellationToken));
		}

		(await store.ReadDatabaseJsonAsync(location, cancellationToken)).ShouldBe(originalJson);
		int count = 0;
		await foreach (ManagedAssetDescriptor unused in store.EnumerateManagedAssetsAsync(location, cancellationToken))
		{
			_ = unused;
			count++;
		}

		count.ShouldBe(0);
	}

	[TestMethod]
	public async Task AbandonedWriteIsInvisible()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		string originalJson = await store.ReadDatabaseJsonAsync(location, cancellationToken);

		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			ChordDatabase changed = DatabaseJson.Deserialize(originalJson);
			changed.Name = "Uncommitted";
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(changed), cancellationToken);
		}

		(await store.ReadDatabaseJsonAsync(location, cancellationToken)).ShouldBe(originalJson);
	}

	[TestMethod]
	public async Task StaleWriterCannotOverwriteNewerCommit()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Original", Guid.NewGuid(), cancellationToken);
		await using IStagedBookWrite first = await store.StageWriteAsync(location, cancellationToken);
		await using IStagedBookWrite stale = await store.StageWriteAsync(location, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		database.Name = "First";
		await first.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
		await first.CommitAsync(cancellationToken);

		database.Name = "Stale";
		await stale.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);

		await Should.ThrowAsync<BookStoreConcurrencyException>(() => stale.CommitAsync(cancellationToken));
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken)).Name.ShouldBe("First");
	}

	#endregion

	#region Private Methods

	private static void AddOpenSong(ChordDatabase database)
	{
		byte[] bytes = TestData.OpenSongBytes();
		Song song = new()
		{
			Id = Guid.CreateVersion7(TestData.Now),
			Title = "Blessed Assurance",
			Revision = RevisionStamp.Initial(Guid.NewGuid(), TestData.Now),
		};
		Guid fileId = Guid.CreateVersion7(TestData.Now.AddMilliseconds(1));
		database.Songs.Add(song);
		database.SongFiles.Add(new SongFile
		{
			Id = fileId,
			SongId = song.Id,
			RelativePath = PortableManagedFileName.Create(song.Title, fileId, extension: null),
			MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.OpenSongXml,
			ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
			ObservedLength = bytes.Length,
			Revision = RevisionStamp.Initial(Guid.NewGuid(), TestData.Now),
		});
	}

	#endregion
}
