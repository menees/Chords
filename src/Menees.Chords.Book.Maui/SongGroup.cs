namespace Menees.Chords.Book.Maui;

public sealed partial class SongGroup : List<SongRow>
{
	public SongGroup(string key, IEnumerable<SongRow> songs)
		: base(songs)
	{
		this.Key = key;
	}

	public string Key { get; }
}
