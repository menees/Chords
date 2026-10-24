namespace Menees.Chords.Sync;

/// <summary>Preserves the text progress contract while forwarding structured phase updates.</summary>
internal sealed class BookSyncProgressAdapter : IProgress<string>, IProgress<BookSyncProgress>
{
	private readonly IProgress<BookSyncProgress> progress;

	public BookSyncProgressAdapter(IProgress<BookSyncProgress> progress)
	{
		this.progress = progress;
	}

	public void Report(string value) => this.progress.Report(new(value));

	public void Report(BookSyncProgress value) => this.progress.Report(value);
}
