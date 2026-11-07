using Menees.Chords.Db;

namespace Menees.Chords.Book.Application;

/// <summary>An opened/imported song, exact-content choices, or a captured import review requiring user input.</summary>
public sealed record SongFileOpenResult(
	Guid? SongId, Guid? SongFileId, bool Imported, IReadOnlyList<BookImportCandidate> HashMatches, BookImportReview? Review) : IDisposable
{
	public void Dispose() => this.Review?.Dispose();
}
