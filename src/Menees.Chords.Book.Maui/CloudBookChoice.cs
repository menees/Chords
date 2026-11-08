namespace Menees.Chords.Book.Maui;

public sealed record CloudBookChoice(Guid Id, string FolderId, string Name, int SongCount)
{
	public override string ToString() => this.SongCount < 0 ? this.Name : this.Name + " (" + this.SongCount + " songs)";
}
