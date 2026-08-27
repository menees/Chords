using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Optionally inspects externally editable storage without coupling core logic to a filesystem.</summary>
public interface IExternalBookReconciler
{
	/// <summary>Reports external problems without modifying the book.</summary>
	Task<IReadOnlyList<ExternalBookProblem>> InspectAsync(
		BookLocation location,
		CancellationToken cancellationToken = default);
}
