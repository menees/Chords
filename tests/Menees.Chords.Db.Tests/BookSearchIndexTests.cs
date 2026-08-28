#region Using Directives

using System.Diagnostics;

#endregion

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class BookSearchIndexTests
{
	#region Private Data

	private const int GeneratedSongCount = 10_000;
	private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(100);

	#endregion

	#region Public Methods

	[TestMethod]
	public void SearchIsCaseAndDiacriticInsensitive()
	{
		ChordDatabase database = CreateGeneratedDatabase(3);
		database.Songs[1].Title = "Déjà Vu";
		BookSearchIndex index = new(database);

		IReadOnlyList<BookSearchHit> matches = index.Search("DEJA");

		matches.Single().Title.ShouldBe("Déjà Vu");
	}

	[TestMethod]
	public void TenThousandSongSearchMeetsBudget()
	{
		ChordDatabase database = CreateGeneratedDatabase(GeneratedSongCount);
		database.Songs[^1].Title = "Résumé Finale";
		BookSearchIndex index = new(database);
		_ = index.Search("warmup");
		Stopwatch stopwatch = Stopwatch.StartNew();

		IReadOnlyList<BookSearchHit> matches = index.Search("RESUME FINALE");

		stopwatch.Stop();
		matches.Single().Title.ShouldBe("Résumé Finale");
		stopwatch.Elapsed.ShouldBeLessThan(SearchBudget);
	}

	#endregion

	#region Private Methods

	private static ChordDatabase CreateGeneratedDatabase(int count)
	{
		Guid deviceId = Guid.NewGuid();
		ChordDatabase result = ChordDatabase.Create("Generated", deviceId);
		for (int index = 0; index < count; index++)
		{
			result.Songs.Add(new()
			{
				Id = Guid.CreateVersion7(),
				Title = $"Generated Song {index:D5}",
				Artists = [$"Artist {index % 100:D2}"],
				Tags = [index % 2 == 0 ? "even" : "odd"],
				Revision = RevisionStamp.Initial(deviceId),
			});
		}

		return result;
	}

	#endregion
}
