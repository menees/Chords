using System.ComponentModel;

namespace Menees.Chords.Book.Maui;

public sealed partial class SetlistEntryRow : INotifyPropertyChanged
{
	public SetlistEntryRow(Guid entryId, SongRow song, int position, bool isEditing, bool canMoveUp, bool canMoveDown)
	{
		this.EntryId = entryId;
		this.Song = song;
		this.Position = position;
		this.IsEditing = isEditing;
		this.CanMoveUp = canMoveUp;
		this.CanMoveDown = canMoveDown;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public Guid EntryId { get; }

	public SongRow Song { get; }

	public int Position { get; private set; }

	public bool IsEditing { get; }

	public bool IsViewing => !this.IsEditing;

	public bool CanMoveUp { get; private set; }

	public bool CanMoveDown { get; private set; }

	public string DisplayText => this.Song.DisplayText;

	public void UpdatePosition(int position, int count)
	{
		if (this.Position != position)
		{
			this.Position = position;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Position)));
		}

		bool canMoveUp = position > 1;
		if (this.CanMoveUp != canMoveUp)
		{
			this.CanMoveUp = canMoveUp;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CanMoveUp)));
		}

		bool canMoveDown = position < count;
		if (this.CanMoveDown != canMoveDown)
		{
			this.CanMoveDown = canMoveDown;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CanMoveDown)));
		}
	}
}
