#region Using Directives

using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class NativeBookWriterTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task CreateWritesCompleteBookWithoutMutatingSourceIdentity()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		InMemoryBookStore store = new();
		ChordDatabase source = TestData.CreateDatabase();
		Guid sourceId = source.Id;
		SongFile sourceFile = source.SongFiles.Single();
		NativeBookAsset[] assets = [CreateAsset(sourceFile.Id, TestData.OpenSongBytes())];

		NativeBookWriteResult result = await NativeBookWriter.CreateAsync(
			store,
			source,
			assets,
			Guid.NewGuid(),
			cancellationToken);

		source.Id.ShouldBe(sourceId);
		result.Database.Id.ShouldNotBe(sourceId);
		ChordDatabase committed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(result.Location, cancellationToken));
		committed.Id.ShouldBe(result.Database.Id);
		committed.Songs.Count.ShouldBe(source.Songs.Count);
		using Stream content = await store.OpenManagedAssetAsync(result.Location, sourceFile.Id, cancellationToken);
		content.Length.ShouldBe(TestData.OpenSongBytes().Length);
	}

	[TestMethod]
	public async Task CreateRejectsIncompleteAssetSetBeforeCreatingBook()
	{
		InMemoryBookStore store = new();
		ChordDatabase source = TestData.CreateDatabase();

		await Should.ThrowAsync<BookStoreValidationException>(() => NativeBookWriter.CreateAsync(
			store,
			source,
			[],
			Guid.NewGuid(),
			this.TestContext.CancellationToken));
	}

	#endregion

	#region Private Methods

	private static NativeBookAsset CreateAsset(Guid id, byte[] content)
		=> new(id, cancellationToken =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
		});

	#endregion
}
