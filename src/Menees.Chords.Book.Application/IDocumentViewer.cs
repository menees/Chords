using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Book.Application;

/// <summary>Controls the current document without exposing browser or PDF implementation types.</summary>
public interface IDocumentViewer
{
	Task LoadAsync(DocumentViewerContent content, int generation, bool startAtEnd, CancellationToken cancellationToken = default);

	Task<DocumentViewerState?> GetStateAsync(CancellationToken cancellationToken = default);

	Task MoveViewportAsync(int direction, CancellationToken cancellationToken = default);

	Task RestorePositionAsync(DocumentViewerPosition position, CancellationToken cancellationToken = default);

	void Clear();
}
