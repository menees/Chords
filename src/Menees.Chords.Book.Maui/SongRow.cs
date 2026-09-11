using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Menees.Chords.Book.Maui;

public sealed partial class SongRow : INotifyPropertyChanged
{
	private bool isSelected;
	private bool isSelectionMode;

	public SongRow(Guid id, string title, string displayText, bool isArchived = false)
	{
		this.Id = id;
		this.Title = title;
		this.DisplayText = displayText;
		this.IsArchived = isArchived;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public Guid Id { get; }

	public string Title { get; }

	public string DisplayText { get; }

	public bool IsArchived { get; }

	public Color RowTextColor => this.IsArchived ? Colors.Gray : Color.FromArgb("#25232A");

	public FontAttributes RowFontAttributes => this.IsArchived ? FontAttributes.Italic : FontAttributes.None;

	public bool IsSelected
	{
		get => this.isSelected;
		set => this.SetProperty(ref this.isSelected, value);
	}

	public bool IsSelectionMode
	{
		get => this.isSelectionMode;
		set => this.SetProperty(ref this.isSelectionMode, value);
	}

	private void SetProperty(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
	{
		if (field != value)
		{
			field = value;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
