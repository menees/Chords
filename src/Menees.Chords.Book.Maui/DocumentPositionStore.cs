using System.Text.Json;
using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

/// <summary>Stores bounded per-book/device positions outside the synchronized book.</summary>
internal sealed class DocumentPositionStore
{
	private Guid? bookId;
	private DocumentPositionHistory history = new();

	public DocumentPositionHistory GetHistory(Guid id)
	{
		if (this.bookId != id)
		{
			this.Flush();
			this.bookId = id;
			SavedDocumentPosition[]? saved = null;
			string? json = Preferences.Default.Get<string?>("ChordBook.ViewerPositions." + id.ToString("D"), null);
			try
			{
				saved = json is null ? null : JsonSerializer.Deserialize<SavedDocumentPosition[]>(json);
			}
			catch (JsonException)
			{
				// Local preferences must not prevent opening a valid book.
			}

			this.history = new(saved);
		}

		return this.history;
	}

	public void Flush()
	{
		if (this.bookId is Guid id && this.history.IsDirty)
		{
			Preferences.Default.Set("ChordBook.ViewerPositions." + id.ToString("D"), JsonSerializer.Serialize(this.history.Positions));
			this.history.MarkSaved();
		}
	}
}
