#region Using Directives

using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class DisplaySettingsTests
{
	#region Public Properties

	public TestContext TestContext { get; set; } = null!;

	#endregion

	#region Public Methods

	[TestMethod]
	public void LegacyProfilesUseAutomaticColumnsUntilAnExplicitLimitIsSelected()
	{
		DisplayProfile legacy = System.Text.Json.JsonSerializer.Deserialize<DisplayProfile>("{\"Columns\":1}")!;
		legacy.AutoColumns.ShouldBeTrue();
		Document document = Document.Parse("[C]Words");
		SongDisplaySettings.Render(document, legacy).ShouldContain("--maximum-columns:0");
		DisplayProfile limited = SongDisplaySettings.Resolve(legacy, new() { AutoColumns = false, Columns = 2 });
		SongDisplaySettings.Render(document, limited).ShouldContain("--maximum-columns:2");
		SongDisplaySettings.CreatePatch(limited, legacy).AutoColumns.ShouldBe(false);
		new DisplayOverride { AutoColumns = false }.HasValues.ShouldBeTrue();
	}

	[TestMethod]
	public async Task DisplaySettingsPersistSparseOverridesAndRenderWithoutChangingSource()
	{
		var token = this.TestContext.CancellationToken;
		Guid device = Guid.NewGuid();
		InMemoryBookStore store = new();
		BookLocation location = await store.CreateBookAsync("Display", device, token);
		BookApplicationSession session = new();
		await session.ActivateAsync(store, location, token);
		Guid id = await session.CreateSongAsync("Song", [], [], "[C]Words", device, token);
		SongFile file = session.Database!.SongFiles.Single();
		string hash = file.ContentHash;
		DisplayProfile profile = session.GetDisplaySettings(id);
		profile.FontSize = 24;
		profile.Theme = "Dark";
		profile.ShowChords = false;
		await session.SaveDisplaySettingsAsync(id, profile, device, token);
		session.Search("display:true").Single().HasDisplayOverride.ShouldBeTrue();
		session.Database.Songs.Single().DisplayOverride!.Columns.ShouldBeNull();
		BookSongPresentation presentation = await session.GetPresentationAsync(id, cancellationToken: token);
		presentation.Html!.ShouldContain("font-size: 24px");
		presentation.Html!.ShouldContain("color-scheme:dark");
		presentation.Html!.ShouldContain("display:none !important");
		DisplayProfile defaults = session.GetDisplaySettings();
		defaults.Columns = 2;
		await session.SaveDisplaySettingsAsync(null, defaults, device, token);
		await session.ReloadAsync(token);
		session.GetDisplaySettings(id).Columns.ShouldBe(2);
		session.GetDisplaySettings(id).FontSize.ShouldBe(24);
		await session.SaveDisplaySettingsAsync(id, null, device, token);
		session.Search("display:true").ShouldBeEmpty();
		session.GetDisplaySettings(id).FontSize.ShouldBe(defaults.FontSize);
		session.Database!.SongFiles.Single().ContentHash.ShouldBe(hash);
		(await session.GetSongEditAsync(id, token)).Text.ShouldBe("[C]Words");
	}

	[TestMethod]
	[DataRow("Nashville", ">1</span>")]
	[DataRow("Roman", ">I</span>")]
	public void NumberedNotationUsesSharedMusicalProjection(string notation, string expected)
	{
		Document document = Document.Parse("{key: C}\r\n[C]Words");
		string html = SongDisplaySettings.Render(document, new() { NotationSystem = notation });
		html.ShouldContain(expected);
	}

	[TestMethod]
	public void InvalidDisplayValuesAreRejected()
	{
		Should.Throw<ArgumentException>(() => SongDisplaySettings.Validate(new() { FontSize = double.NaN }));
		Should.Throw<ArgumentException>(() => SongDisplaySettings.Validate(new() { LineSpacing = double.PositiveInfinity }));
		Should.Throw<ArgumentException>(() => SongDisplaySettings.Validate(new() { Columns = 0 }));
		Should.Throw<ArgumentException>(() => SongDisplaySettings.Validate(new() { Theme = "</style>" }));
		Should.Throw<ArgumentException>(() => SongDisplaySettings.Validate(new() { NotationSystem = "Unsupported" }));
	}

	#endregion
}
