namespace Menees.Chords.Book.Application;

/// <summary>A captured comparison; its resolution requires the same catalog and source bytes.</summary>
public sealed class SongVersionComparison
{
	internal SongVersionComparison(Guid bookId, Guid songId, string expectedJson, SongVersionSide left, SongVersionSide right, bool canResolve = true)
	{
		this.BookId = bookId;
		this.SongId = songId;
		this.ExpectedJson = expectedJson;
		this.Left = left;
		this.Right = right;
		this.CanResolve = canResolve;
	}

	public bool CanResolve { get; }

	public Guid BookId { get; }

	public Guid SongId { get; }

	public SongVersionSide Left { get; }

	public SongVersionSide Right { get; }

	internal string ExpectedJson { get; }

	/// <summary>Places older source content on the left without rereading either captured sheet.</summary>
	public SongVersionComparison WithChronologicalOrder()
		=> this.Left.ModifiedUtc <= this.Right.ModifiedUtc ? this
			: new(this.BookId, this.SongId, this.ExpectedJson, this.Right, this.Left, this.CanResolve);
}
