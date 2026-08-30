#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class RepresentativeBookFixtureTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public async Task FixtureMatchesCurrentLibraryShapeAndRoundTrips()
	{
		CancellationToken cancellationToken = this.TestContext.CancellationToken;
		RepresentativeBookFixture fixture = RepresentativeBookFixture.Create();
		fixture.Database.Songs.Count.ShouldBe(500);
		fixture.Database.SongFiles.Count(file => file.MediaKind == MediaKind.Pdf).ShouldBe(17);
		fixture.Database.SongFiles.ShouldContain(file => file.SourceFormat == SourceFormat.ChordPro);
		fixture.Database.SongFiles.ShouldContain(file => file.SourceFormat == SourceFormat.ChordOverText);
		fixture.Database.SongFiles.ShouldContain(file => file.SourceFormat == SourceFormat.Mixed);
		fixture.Database.SongFiles.ShouldContain(file => file.SourceFormat == SourceFormat.OpenSongXml && Path.GetExtension(file.RelativePath).Length == 0);
		fixture.TotalAssetBytes.ShouldBeInRange(7_500_000, 8_500_000);
		InMemoryBookStore store = new();

		NativeBookWriteResult result = await NativeBookWriter.CreateAsync(
			store,
			fixture.Database,
			fixture.Assets,
			Guid.NewGuid(),
			cancellationToken);

		ChordDatabase reopened = DatabaseJson.Deserialize(await store.ReadDatabaseJsonAsync(result.Location, cancellationToken));
		reopened.Songs.Count.ShouldBe(500);
		reopened.SongFiles.Count.ShouldBe(517);
	}

	[TestMethod]
	public void FixtureSearchIsWithinRoutineBudget()
	{
		RepresentativeBookFixture fixture = RepresentativeBookFixture.Create();
		BookSearchIndex index = new(fixture.Database);
		Stopwatch stopwatch = Stopwatch.StartNew();

		IReadOnlyList<BookSearchHit> results = index.Search("artist 07", maximumResults: 500);

		stopwatch.Stop();
		results.ShouldNotBeEmpty();
		stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
	}

	#endregion
}
