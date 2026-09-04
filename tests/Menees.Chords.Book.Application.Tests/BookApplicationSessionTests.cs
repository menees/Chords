#region Using Directives

using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class BookApplicationSessionTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RenameCommitsNameAndRevision()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Old Name", deviceId, cancellationToken);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, cancellationToken);

		await session.RenameAsync("  New Name  ", deviceId, cancellationToken);

		ChordDatabase committed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		committed.Name.ShouldBe("New Name");
		committed.Revision.Revision.ShouldBe(2);
		committed.Revision.DeviceId.ShouldBe(deviceId);
		ReferenceEquals(session.Database, committed).ShouldBeFalse();
		session.Database!.Name.ShouldBe("New Name");
	}

	[TestMethod]
	public async Task SearchUsesMetadataAndHonorsArchivedVisibility()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Songs", deviceId, cancellationToken);
		ChordDatabase database = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		Song visible = CreateSong("Café Song", "José", "worship", isArchived: false, deviceId);
		visible.SourceMetadata["author"] = [new SourceMetadataValue { Value = "José", SourceName = "author" }];
		visible.SourceMetadata["key"] = [new SourceMetadataValue { Value = "F#", SourceName = "key" }];
		visible.SourceMetadata["tempo"] = [new SourceMetadataValue { Value = "102", SourceName = "tempo" }];
		visible.SourceMetadata["capo"] = [new SourceMetadataValue { Value = "4", SourceName = "capo" }];
		visible.SourceMetadata["album"] = [new SourceMetadataValue { Value = "Come On Over", SourceName = "album" }];
		visible.SourceMetadata["composer"] = [new SourceMetadataValue { Value = "A. Writer", SourceName = "composer" }];
		visible.SourceMetadata["copyright"] = [new SourceMetadataValue { Value = "1997 Example", SourceName = "copyright" }];
		visible.SourceMetadata["t"] = [new SourceMetadataValue { Value = "Café Song", SourceName = "t" }];
		database.Songs.Add(visible);
		database.Songs.Add(CreateSong("Hidden Song", "Someone", "practice", isArchived: true, deviceId));
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(database), cancellationToken);
			await write.CommitAsync(cancellationToken);
		}

		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, cancellationToken);

		SongCatalogItem match = session.Search("jose worship").Single();
		match.Title.ShouldBe("Café Song");
		match.DisplayText.ShouldBe(
			"Café Song — José · C:4 · T:102 · K:F# · A:Come On Over · Com:A. Writer · Cop:1997 Example · Ta:worship");
		session.Search("hidden").ShouldBeEmpty();
		session.Search("hidden", includeArchived: true).Single().IsArchived.ShouldBeTrue();
	}

	#endregion

	#region Private Methods

	private static Song CreateSong(string title, string artist, string tag, bool isArchived, Guid deviceId) => new()
	{
		Id = Guid.CreateVersion7(),
		Title = title,
		Artists = [artist],
		Tags = [tag],
		IsArchived = isArchived,
		Revision = RevisionStamp.Initial(deviceId),
	};

	#endregion
}
