namespace Menees.Chords.Book.Maui.Services;

public interface IWindowsPicker
{
	Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken);

	Task<string?> PickFolderAsync(CancellationToken cancellationToken);

	Task OpenFolderAsync(string path, CancellationToken cancellationToken);
}
