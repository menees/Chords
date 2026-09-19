using Menees.Chords.Db;

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class EditorControlTests
{
	[TestMethod]
	[DataRow("key", "C")]
	[DataRow("key", "H")]
	[DataRow("key", "F#m")]
	[DataRow("key", "Bbm")]
	[DataRow("tempo", "1")]
	[DataRow("tempo", "300")]
	[DataRow("capo", "1")]
	[DataRow("capo", "24")]
	[DataRow("duration", "120:04")]
	[DataRow("duration", "0:00")]
	[DataRow("year", "1000")]
	public void ValidMetadataIsAccepted(string name, string value) => SongMetadataValidation.GetError(name, value).ShouldBeNull();

	[TestMethod]
	[DataRow("key", "C7")]
	[DataRow("key", "Q")]
	[DataRow("tempo", "0")]
	[DataRow("tempo", "301")]
	[DataRow("tempo", "120.5")]
	[DataRow("capo", "0")]
	[DataRow("capo", "25")]
	[DataRow("duration", "4:60")]
	[DataRow("duration", "240")]
	[DataRow("year", "999")]
	[DataRow("year", "9999")]
	public void InvalidMetadataIsRejected(string name, string value) => SongMetadataValidation.GetError(name, value).ShouldNotBeNull();

	[TestMethod]
	public void ExistingSourceValuesCanBeRetainedButChangedValuesAreValidated()
	{
		Dictionary<string, IReadOnlyList<string>> original = new() { ["tempo"] = ["Allegro"] };
		SongMetadataValidation.Validate(original, original);
		Dictionary<string, IReadOnlyList<string>> changed = new() { ["tempo"] = ["0"] };
		Should.Throw<ArgumentException>(() => SongMetadataValidation.Validate(changed, original));
		SongMetadataValidation.Validate(new Dictionary<string, IReadOnlyList<string>> { ["tempo"] = [] }, original);
	}

	[TestMethod]
	[DataRow("C", -1, "B (-1)")]
	[DataRow("C", -2, "Bb (-2)")]
	[DataRow("C", 1, "C# (+1)")]
	[DataRow("Am", 1, "A#m (+1)")]
	[DataRow("Bb", 0, "Bb (0)")]
	[DataRow(null, 2, "+2")]
	public void TransposeChoicesShowKeysAndSignedOffsets(string? key, int offset, string expected)
		=> TransposeChoice.GetLabel(key, offset).ShouldBe(expected);

	[TestMethod]
	public void ContinuousPreviewOptsOutOfPaginationButPerformanceKeepsIt()
	{
		Document document = Document.Parse("{key: C}\n{start_of_verse: Verse}\n[C]Words\n{end_of_verse}");
		SongDisplaySettings.Render(document, new DisplayProfile(), responsivePages: false).ShouldContain("data-responsive-pages=\"off\"");
		SongDisplaySettings.Render(document, new DisplayProfile()).ShouldNotContain("data-responsive-pages=\"off\"");
	}
}
