namespace Menees.Chords.Sync;

/// <summary>Preserves the text progress contract while forwarding structured phase updates.</summary>
internal sealed class BookSyncProgressAdapter : IProgress<string>, IProgress<BookSyncProgress>
{
	private readonly IProgress<BookSyncProgress> progress;

	public BookSyncProgressAdapter(IProgress<BookSyncProgress> progress)
	{
		ArgumentNullException.ThrowIfNull(progress);
		this.progress = progress;
	}

	public void Report(string value) => this.progress.Report(new(value));

	public void Report(BookSyncProgress value) => this.progress.Report(value);

	internal static void Report(IProgress<string>? progress, BookSyncProgress value)
	{
		if (progress is IProgress<BookSyncProgress> structured)
		{
			structured.Report(value);
		}
		else
		{
			progress?.Report(value.Message);
		}
	}
}
