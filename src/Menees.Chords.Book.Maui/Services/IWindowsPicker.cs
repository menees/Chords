namespace Menees.Chords.Book.Maui.Services;

public interface IWindowsPicker
{
	Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken);

	Task<string?> PickFolderAsync(CancellationToken cancellationToken);

	Task<string?> PickFileAsync(string extension, CancellationToken cancellationToken);

	Task<string?> SaveFileAsync(string suggestedName, string extension, string description, CancellationToken cancellationToken);

	Task OpenFolderAsync(string path, CancellationToken cancellationToken);
}
