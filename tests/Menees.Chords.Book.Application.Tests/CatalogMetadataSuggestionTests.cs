#region Using Directives

using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class CatalogMetadataSuggestionTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void TenThousandSongSuggestionsAreDetachedSortedAndBoundedToDistinctValues()
	{
		const int SongCount = 10_000;
		const int DistinctArtists = 100;
		ChordDatabase database = new();
		for (int index = 0; index < SongCount; index++)
		{
			Song song = new() { Id = Guid.NewGuid(), Artists = [$"Artist {index % DistinctArtists:D3}"], Tags = ["Practice"] };
			song.MetadataOverrides["album"] = ["Album"];
			database.Songs.Add(song);
		}

		long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
		BookMetadataValueIndex values = new(database);
		values.GetValues("artist").Count.ShouldBe(DistinctArtists);
		values.GetValues("tag").ShouldBe(["Practice"]);
		values.GetValues("album").ShouldBe(["Album"]);
		values.GetValues("artist").ShouldBeSameAs(values.GetValues("artist"));
		this.TestContext.WriteLine($"10,000-song suggestions: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms, "
			+ $"{GC.GetAllocatedBytesForCurrentThread() - allocated:N0} allocated bytes.");
	}

	[TestMethod]
	public void QuotedValuesRoundTripAndSelectionsDoNotDuplicateNormalizedValues()
	{
		string[] values = ["AC; DC", "A \"quoted\" artist", " Café ", "Plain"];
		CatalogValueList.Parse(CatalogValueList.Format(values)).ShouldBe(values);
		CatalogValueList.Parse(" one ; two;; \"three;four\" ; ").ShouldBe(["one", "two", "three;four"]);
		CatalogValueList.Append("  Cafe\u0301; Other ", "CAFÉ").ShouldBe("  Cafe\u0301; Other ");
		CatalogValueList.Parse(CatalogValueList.Append("One", "A;B")).ShouldBe(["One", "A;B"]);
		Should.Throw<ArgumentException>(() => CatalogValueList.Parse("\"Unclosed"));
		Should.Throw<ArgumentException>(() => CatalogValueList.Parse("\"Closed\" trailing"));
		Should.Throw<ArgumentException>(() => CatalogValueList.Parse("\"A\" \"B\""));
	}

	[TestMethod]
	public void CatalogIndexHonorsOverridesAndIncludesArchivedValuesWithoutMutatingSongs()
	{
		ChordDatabase database = new();
		Song first = new() { Artists = [" Café ", "Alpha"], Tags = ["Practice", string.Empty], IsArchived = true };
		first.SourceMetadata["album"] = [new() { Value = "Source album", SourceName = "album" }];
		first.SourceMetadata["time"] = [new() { Value = "6/8", SourceName = "time" }];
		first.MetadataOverrides["album"] = ["Catalog album"];
		Song second = new() { Artists = ["Cafe\u0301", "beta"], Tags = ["practice", "New"] };
		database.Songs.AddRange([first, second]);
		BookMetadataValueIndex index = new(database);
		index.GetValues("artist").ShouldBe(["Alpha", "beta", "Café"]);
		index.GetValues("tag").ShouldBe(["New", "Practice"]);
		index.GetValues("album").ShouldBe(["Catalog album"]);
		index.GetValues("time").ShouldBe(["6/8"]);
		first.Artists[0].ShouldBe(" Café ");
		first.MetadataOverrides["album"] = [];
		new BookMetadataValueIndex(database).GetValues("album").ShouldBeEmpty();
		Should.Throw<ArgumentException>(() => index.GetValues("unsupported"));
	}

	[TestMethod]
	public async Task CachedSuggestionsAvoidAssetReadsAndInvalidateAfterCatalogChanges()
	{
		var token = this.TestContext.CancellationToken;
		string root = Path.Combine(Path.GetTempPath(), "ChordSuggestion-" + Guid.NewGuid().ToString("N"));
		try
		{
			using FileSystemBookStore store = new(root);
			Guid device = Guid.NewGuid();
			BookLocation book = await store.CreateBookAsync("Suggestions", device, token);
			BookApplicationSession app = new();
			await app.ActivateAsync(store, book, token);
			Guid song = await app.CreateSongAsync("Song", ["Artist"], ["Tag"], "[C]Words", device, token);
			SongFile file = app.Database!.SongFiles.Single();
			using FileStream locked = File.Open(Path.Combine(store.GetDirectory(book), file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None);
			BookMetadataValueIndex first = app.GetMetadataValueSuggestions();
			first.GetValues("artist").ShouldBe(["Artist"]);
			app.GetMetadataValueSuggestions().ShouldBeSameAs(first);
			await app.SetSongsArchivedAsync([song], true, device, token);
			await app.CreateSongAsync("Second", ["New artist"], ["Other"], "[D]Words", device, token);
			BookMetadataValueIndex next = app.GetMetadataValueSuggestions();
			next.ShouldNotBeSameAs(first);
			next.GetValues("artist").ShouldBe(["Artist", "New artist"]);
			first.GetValues("artist").ShouldBe(["Artist"]);
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	#endregion
}
