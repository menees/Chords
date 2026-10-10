using Menees.Chords.Book.Application;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui;

// The production comparison page needs only these session boundaries.
// Use real application/storage operations without constructing the Windows preferences/book-lifecycle service.
internal sealed class BookSession
{
	private readonly BookApplicationSession application;
	private readonly Guid device;
	public BookSession(BookApplicationSession application, string directory, Guid device)
	{
		this.application = application;
		this.DirectoryPath = directory;
		this.device = device;
	}

	public string DirectoryPath { get; }

	public Task ResolveSongVersionsAsync(SongVersionComparison comparison, SongVersionChoice choice, CancellationToken token)
		=> Task.Run(() => this.application.ResolveSongVersionsAsync(comparison, choice, this.device, token), token);
}
