namespace Menees.Chords.Db;

/// <summary>Resolves portable book and song display settings with app-local device overrides.</summary>
public static class DisplaySettingsResolver
{
	/// <summary>Creates the effective display profile.</summary>
	/// <param name="bookSettings">The synchronized book settings.</param>
	/// <param name="songOverride">The optional synchronized song override.</param>
	/// <param name="deviceOverride">The optional app-local device override.</param>
	/// <returns>A new, independently mutable display profile.</returns>
	/// <remarks>Precedence is book, then song, then device.</remarks>
	public static DisplayProfile Resolve(
		BookSettings bookSettings,
		DisplayOverride? songOverride = null,
		DeviceSettingsOverride? deviceOverride = null)
	{
		ArgumentNullException.ThrowIfNull(bookSettings);
		DisplayProfile source = bookSettings.DefaultDisplayProfile;
		DisplayProfile result = new()
		{
			Theme = source.Theme,
			FontSize = source.FontSize,
			LineSpacing = source.LineSpacing,
			Columns = source.Columns,
			ShowChords = source.ShowChords,
			NotationSystem = source.NotationSystem,
		};
		Apply(result, songOverride);
		Apply(result, deviceOverride?.Display);
		return result;
	}

	private static void Apply(DisplayProfile profile, DisplayOverride? patch)
	{
		if (patch is not null)
		{
			profile.Theme = patch.Theme ?? profile.Theme;
			profile.FontSize = patch.FontSize ?? profile.FontSize;
			profile.LineSpacing = patch.LineSpacing ?? profile.LineSpacing;
			profile.Columns = patch.Columns ?? profile.Columns;
			profile.ShowChords = patch.ShowChords ?? profile.ShowChords;
			profile.NotationSystem = patch.NotationSystem ?? profile.NotationSystem;
		}
	}
}
