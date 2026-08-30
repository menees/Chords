namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class DisplaySettingsResolverTests
{
	[TestMethod]
	public void ResolveAppliesBookSongAndDevicePrecedence()
	{
		BookSettings settings = new()
		{
			DefaultDisplayProfile = new DisplayProfile
			{
				Theme = "Book",
				FontSize = 18,
				Columns = 1,
			},
		};
		DisplayOverride song = new() { Theme = "Song", FontSize = 20 };
		DeviceSettingsOverride device = new() { Display = new DisplayOverride { FontSize = 24, Columns = 2 } };

		DisplayProfile result = DisplaySettingsResolver.Resolve(settings, song, device);

		result.Theme.ShouldBe("Song");
		result.FontSize.ShouldBe(24);
		result.Columns.ShouldBe(2);
		result.ShouldNotBeSameAs(settings.DefaultDisplayProfile);
	}

	[TestMethod]
	public void EmptyOverridesReturnAnIndependentCopyOfBookDefaults()
	{
		BookSettings settings = new();

		DisplayProfile result = DisplaySettingsResolver.Resolve(settings, new DisplayOverride(), new DeviceSettingsOverride());

		result.ShouldBeEquivalentTo(settings.DefaultDisplayProfile);
		result.ShouldNotBeSameAs(settings.DefaultDisplayProfile);
	}
}
