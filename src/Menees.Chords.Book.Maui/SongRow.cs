using System.ComponentModel;
using System.Runtime.CompilerServices;
using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

public sealed partial class SongRow : INotifyPropertyChanged
{
	private bool isSelected;
	private bool isSelectionMode;

	public SongRow(SongCatalogItem song)
	{
		this.Id = song.Id;
		this.Title = song.Title;
		this.IsArchived = song.IsArchived;
		this.Artist = song.Artist;
		this.Key = song.Key;
		this.Tempo = song.Tempo;
		this.Capo = song.Capo;
		this.Tags = song.Tags;
		this.Indicators = song.Indicators;
		this.Columns =
			(this.Artist.Length > 0 ? CatalogColumn.Artist : CatalogColumn.None)
			| (this.Key.Length > 0 ? CatalogColumn.Key : CatalogColumn.None)
			| (this.Tempo.Length > 0 ? CatalogColumn.Tempo : CatalogColumn.None)
			| (this.Capo.Length > 0 ? CatalogColumn.Capo : CatalogColumn.None)
			| (this.Tags.Length > 0 ? CatalogColumn.Tags : CatalogColumn.None);
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public Guid Id { get; }

	public string Title { get; }

	public string Artist { get; }

	public string Key { get; }

	public string Tempo { get; }

	public string Capo { get; }

	public string Tags { get; }

	public string Indicators { get; }

	public CatalogColumn Columns { get; }

	public bool IsArchived { get; }

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
