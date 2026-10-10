namespace Menees.Chords.Book.Application;

/// <summary>A captured comparison; its resolution requires the same catalog and source bytes.</summary>
public sealed class SongVersionComparison
{
	internal SongVersionComparison(Guid bookId, Guid songId, string expectedJson, SongVersionSide left, SongVersionSide right)
	{
		this.BookId = bookId;
		this.SongId = songId;
		this.ExpectedJson = expectedJson;
		this.Left = left;
		this.Right = right;
	}

	public Guid BookId { get; }

	public Guid SongId { get; }

	public SongVersionSide Left { get; }

	public SongVersionSide Right { get; }

	internal string ExpectedJson { get; }
}
