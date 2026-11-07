using System.Text.Json;
using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

public sealed partial class BookSession
{
	private readonly Dictionary<string, SongViewSort?> viewSorts = new(StringComparer.Ordinal);
	private Guid? viewSortBookId;

	public SongViewSort? GetViewSort(string view)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(view);
		if (this.viewSortBookId != this.Database?.Id)
		{
			this.viewSorts.Clear();
			this.viewSortBookId = this.Database?.Id;
		}

		if (!this.viewSorts.TryGetValue(view, out SongViewSort? result))
		{
			string? json = Preferences.Default.Get<string?>(this.ViewSortKey(view), null);
			try
			{
				SongViewSort? saved = json is null ? null : JsonSerializer.Deserialize<SongViewSort>(json);
				result = saved is { IsValid: true, Field: not null } ? saved : null;
			}
			catch (JsonException)
			{
				// A damaged device preference must not prevent browsing the book.
			}

			this.viewSorts.Add(view, result);
		}

		return result;
	}

	public void SaveViewSort(string view, SongViewSort sort)
	{
		ArgumentNullException.ThrowIfNull(sort);
		if (!sort.IsValid)
		{
			throw new ArgumentException("Choose a supported song sort field.", nameof(sort));
		}

		_ = this.GetViewSort(view);
		if (sort.Field is null)
		{
			Preferences.Default.Remove(this.ViewSortKey(view));
		}
		else
		{
			Preferences.Default.Set(this.ViewSortKey(view), JsonSerializer.Serialize(sort));
		}

		this.viewSorts[view] = sort.Field is null ? null : sort;
	}

	private string ViewSortKey(string view)
	{
		Guid id = this.Database?.Id ?? throw new InvalidOperationException("No book is open.");
		return "ChordBook.ViewSort." + id.ToString("D") + "." + view;
	}
}
