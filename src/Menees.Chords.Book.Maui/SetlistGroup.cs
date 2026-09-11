using System.Collections.ObjectModel;

namespace Menees.Chords.Book.Maui;

public sealed partial class SetlistGroup : ObservableCollection<SetlistRow>
{
	public SetlistGroup(string key, IEnumerable<SetlistRow> setlists)
		: base(setlists)
	{
		this.Key = key;
	}

	public string Key { get; }
}
