namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class SongTemplateTests
{
	[TestMethod]
	public void EvaluateRendersNormalizedAndSourceMetadata()
	{
		Song song = new()
		{
			Title = "Blessed Assurance",
			Artists = ["Fanny Crosby", "Phoebe Knapp"],
			DurationSeconds = 268,
		};
		song.SourceMetadata["key"] = [new SourceMetadataValue { Value = "D", SourceName = "key" }];
		SongInstrumentSetting instrument = new() { CapoFret = 2 };

		string value = SongTemplate.Compile("{title} — {artists} · C:{capo} · K:{keys} · {duration}")
			.Evaluate(new SongTemplateContext(song, instrument));

		value.ShouldBe("Blessed Assurance — Fanny Crosby, Phoebe Knapp · C:2 · K:D · 4:28");
	}

	[TestMethod]
	public void EvaluateSuppressesMissingAndUnknownDecoratedSegments()
	{
		Song song = new() { Title = "Blessed Assurance" };

		string value = SongTemplate.Compile("{title} — {artists} · C:{capo} · X:{unknown}")
			.Evaluate(new SongTemplateContext(song));

		value.ShouldBe("Blessed Assurance");
	}

	[TestMethod]
	public void CompileTreatsLiteralBracesAndUnadornedTextAsText()
	{
		Song song = new() { Title = "Song" };

		string value = SongTemplate.Compile("Now playing: {title} ({not a token})")
			.Evaluate(new SongTemplateContext(song));

		value.ShouldBe("Now playing: Song ({not a token})");
	}
}
