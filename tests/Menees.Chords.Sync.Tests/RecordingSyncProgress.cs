namespace Menees.Chords.Sync.Tests;

internal sealed class RecordingSyncProgress : IProgress<BookSyncProgress>
{
	public List<BookSyncProgress> Values { get; } = [];

	public void Report(BookSyncProgress value) => this.Values.Add(value);
}
