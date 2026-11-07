using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>A device view's sort preference. A null field restores the view's default ordering.</summary>
public sealed record SongViewSort(string? Field = null, bool Descending = false)
{
	public bool IsValid => this.Field is null || BookGroupingIndex.SortFields.ContainsKey(this.Field);
}
