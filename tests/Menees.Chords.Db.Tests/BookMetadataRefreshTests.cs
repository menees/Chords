#region Using Directives

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookMetadataRefreshTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task RefreshBackfillsOldImportsOnlyOnce()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		Guid deviceId = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Old Imports", deviceId, cancellationToken);
		const string Text = "{title: 9 To 5}\r\n{artist: Dolly Parton}\r\n{key: F#}\r\n{tempo: 102}\r\n{capo: 4}\r\n\r\n[D]Tumble outta bed";
		BookImportResult imported = await BookImportService.ImportAsync(
			store,
			location,
			"9 To 5.cho",
			new MemoryStream(Encoding.UTF8.GetBytes(Text), writable: false),
			deviceId,
			cancellationToken);
		imported.Analysis.Artists.ShouldBe(["Dolly Parton"]);
		ChordDatabase oldDatabase = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		oldDatabase.Songs.Single().Artists.Clear();
		oldDatabase.Songs.Single().SourceMetadata.Clear();
		oldDatabase.SongFiles.Single().AnalysisVersion = 0;
		await using (IStagedBookWrite write = await store.StageWriteAsync(location, cancellationToken))
		{
			await write.WriteDatabaseJsonAsync(DatabaseJson.Serialize(oldDatabase), cancellationToken);
			await write.CommitAsync(cancellationToken);
		}

		BookMetadataRefreshResult first = await BookMetadataRefresh.RefreshAsync(store, location, deviceId, cancellationToken);
		ChordDatabase refreshed = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken));
		Song song = refreshed.Songs.Single();
		first.ShouldBe(new BookMetadataRefreshResult(1, 1));
		song.Artists.ShouldBe(["Dolly Parton"]);
		song.SourceMetadata["key"].Single().Value.ShouldBe("F#");
		song.SourceMetadata["tempo"].Single().Value.ShouldBe("102");
		song.SourceMetadata["capo"].Single().Value.ShouldBe("4");
		refreshed.SongFiles.Single().AnalysisVersion.ShouldBe(SongFileAnalyzer.CurrentAnalysisVersion);
		using Stream source = await store.OpenManagedAssetAsync(location, imported.SongFileId, cancellationToken);
		using StreamReader reader = new(source, Encoding.UTF8);
		(await reader.ReadToEndAsync(cancellationToken)).ShouldBe(Text);

		long revision = refreshed.Revision.Revision;
		BookMetadataRefreshResult second = await BookMetadataRefresh.RefreshAsync(store, location, deviceId, cancellationToken);
		second.ShouldBe(new BookMetadataRefreshResult(0, 0));
		DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(location, cancellationToken))
			.Revision.Revision.ShouldBe(revision);
	}

	#endregion
}
