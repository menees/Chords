using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Collects database and asset changes that become visible together on commit.</summary>
public interface IStagedBookWrite : IAsyncDisposable
{
	/// <summary>Stages canonical database JSON.</summary>
	Task WriteDatabaseJsonAsync(string json, CancellationToken cancellationToken = default);

	/// <summary>Stages a new or replacement managed asset.</summary>
	Task WriteManagedAssetAsync(
		Guid songFileId,
		string relativePath,
		Stream content,
		CancellationToken cancellationToken = default);

	/// <summary>Stages a managed asset rename without changing its bytes.</summary>
	Task RenameManagedAssetAsync(
		Guid songFileId,
		string relativePath,
		CancellationToken cancellationToken = default);

	/// <summary>Stages deletion of a managed asset.</summary>
	Task DeleteManagedAssetAsync(Guid songFileId, CancellationToken cancellationToken = default);

	/// <summary>Validates and atomically publishes all staged changes.</summary>
	Task CommitAsync(CancellationToken cancellationToken = default);
}
