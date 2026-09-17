namespace Menees.Chords.Book.Application;

/// <summary>Bounds local reading positions independently of book metadata and source assets.</summary>
public sealed class DocumentPositionHistory
{
	private const int MaximumPositions = 100;
	private readonly List<SavedDocumentPosition> positions;

	public DocumentPositionHistory(IEnumerable<SavedDocumentPosition>? saved = null)
	{
		this.positions = [.. (saved ?? []).Where(item => item?.Key is not null && item.Key.SongId != Guid.Empty
			&& item.Position is not null && IsValid(item.Position)).DistinctBy(item => item.Key).Take(MaximumPositions)];
		this.Positions = this.positions.AsReadOnly();
	}

	public IReadOnlyList<SavedDocumentPosition> Positions { get; }

	public bool IsDirty { get; private set; }

	public DocumentViewerPosition? Find(DocumentPositionKey key)
		=> this.positions.Find(item => item.Key == key)?.Position;

	public void Record(DocumentPositionKey key, DocumentViewerPosition position)
	{
		if (key.SongId != Guid.Empty && IsValid(position))
		{
			int index = this.positions.FindIndex(item => item.Key == key);
			if (index != 0 || this.positions[0].Position != position)
			{
				if (index >= 0)
				{
					this.positions.RemoveAt(index);
				}

				this.positions.Insert(0, new(key, position));
				if (this.positions.Count > MaximumPositions)
				{
					this.positions.RemoveAt(MaximumPositions);
				}

				this.IsDirty = true;
			}
		}
	}

	public void MarkSaved() => this.IsDirty = false;

	private static bool IsValid(DocumentViewerPosition position)
		=> position.Page >= 0 && position.Zoom is >= 1 and <= 2
			&& position.HorizontalProgress is >= 0 and <= 1 && position.VerticalProgress is >= 0 and <= 1;
}
