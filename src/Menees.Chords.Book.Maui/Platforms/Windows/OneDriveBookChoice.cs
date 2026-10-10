namespace Menees.Chords.Book.Maui.Platforms.Windows;

internal sealed record OneDriveBookChoice(Guid Id, string FolderId, string Name, int SongCount)
{
	public override string ToString() => this.Name + " (" + this.SongCount + " songs)";
}
