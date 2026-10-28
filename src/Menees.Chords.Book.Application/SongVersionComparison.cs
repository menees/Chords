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

	/// <summary>Describes the selected resolution and only the sheet usages it will redirect.</summary>
	public (string Title, string Message, string Accept) GetConfirmation(SongVersionChoice choice)
	{
		if (!this.CanResolve || !Enum.IsDefined(choice))
		{
			throw new ArgumentException("Choose a resolution for a writable comparison.", nameof(choice));
		}

		(string Title, string Message, string Accept) result;
		if (choice == SongVersionChoice.KeepBoth)
		{
			result = ("Keep Both Sheets?", "Make both sheets active. Neither sheet will be deleted.", "Keep Both");
		}
		else
		{
			bool keepLeft = choice == SongVersionChoice.KeepLeft;
			string kept = keepLeft ? "left" : "right";
			string deleted = keepLeft ? "right" : "left";
			SongVersionSide discarded = keepLeft ? this.Right : this.Left;
			string message = $"Keep the {kept} sheet as this song's default sheet and permanently delete the {deleted} sheet.";
			string? users = (discarded.UsedBySetlists, discarded.UsedByInstruments) switch
			{
				(true, true) => "Setlist entries and instrument settings",
				(true, false) => "Setlist entries",
				(false, true) => "Instrument settings",
				_ => null,
			};
			if (users is not null)
			{
				message += $"\n\n{users} that use the {deleted} sheet will use the {kept} sheet instead.";
			}

			result = (keepLeft ? "Keep Left and Delete Right?" : "Keep Right and Delete Left?", message, keepLeft ? "Keep Left" : "Keep Right");
		}

		return result;
	}
}
