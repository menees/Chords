namespace Menees.Chords.Book.Maui;

[Flags]
public enum CatalogColumn
{
	None = 0,
	Artist = 1 << 0,
	Key = 1 << 1,
	Tempo = 1 << 2,
	Capo = 1 << 3,
	Tags = 1 << 4,
	Songs = 1 << 5,
	Date = 1 << 6,
	Duration = 1 << 7,
	Compact = 1 << 8,
}
