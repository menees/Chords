namespace Menees.Chords.Book.Maui;

/// <summary>A shortcut for the shared archive icon toggle.</summary>
public sealed partial class ArchiveToggle : FluentIconToggle
{
	public ArchiveToggle()
	{
		this.Icon = "Archive";
		this.Description = "Show Archived";
	}
}
