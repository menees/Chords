using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Book.Application;

/// <summary>Edits an unsaved song buffer without exposing browser types or accessing book storage.</summary>
public interface ISongTextEditor : IDisposable
{
	event EventHandler? TextChanged;

	/// <summary>Raised for edits or movement to a different preview section while preview is enabled.</summary>
	event EventHandler? PreviewChanged;

	Task SetPreviewEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

	/// <summary>Gets the current section with preceding musical context, without changing the saved buffer.</summary>
	Task<string> GetPreviewTextAsync(CancellationToken cancellationToken = default);

	Task FindAsync(bool replace, CancellationToken cancellationToken = default);

	Task UndoAsync(CancellationToken cancellationToken = default);

	Task RedoAsync(CancellationToken cancellationToken = default);

	Task LoadAsync(string text, CancellationToken cancellationToken = default);

	Task<string> GetTextAsync(CancellationToken cancellationToken = default);

	/// <summary>Replaces the buffer as one undoable edit.</summary>
	Task ReplaceTextAsync(string text, CancellationToken cancellationToken = default);

	Task SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default);
}
