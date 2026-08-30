namespace Menees.Chords.Db;

/// <summary>Represents sparse, app-local settings for one device installation.</summary>
/// <remarks>
/// This type is deliberately not part of <see cref="ChordDatabase"/>. Applications persist it
/// in local preferences rather than in the synchronized book.
/// </remarks>
public sealed class DeviceSettingsOverride
{
	/// <summary>Gets or sets device-specific display overrides.</summary>
	public DisplayOverride? Display { get; set; }
}
