using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Db;

/// <summary>Provides portable access to complete chord books and their managed assets.</summary>
public interface IBookStore
{
	/// <summary>Gets the optional operations supported by the store.</summary>
	BookStoreCapabilities Capabilities { get; }

	/// <summary>Creates a book and returns its opaque location.</summary>
	Task<BookLocation> CreateBookAsync(string name, Guid deviceId, CancellationToken cancellationToken = default);

	/// <summary>Determines whether a book still exists.</summary>
	Task<bool> ExistsAsync(BookLocation location, CancellationToken cancellationToken = default);

	/// <summary>Deletes a complete book.</summary>
	Task DeleteBookAsync(BookLocation location, CancellationToken cancellationToken = default);

	/// <summary>Reads the exact canonical database JSON.</summary>
	Task<string> ReadDatabaseJsonAsync(BookLocation location, CancellationToken cancellationToken = default);

	/// <summary>Enumerates only assets referenced by the chord database.</summary>
	IAsyncEnumerable<ManagedAssetDescriptor> EnumerateManagedAssetsAsync(
		BookLocation location,
		CancellationToken cancellationToken = default);

	/// <summary>Opens an independent readable stream for an asset.</summary>
	Task<Stream> OpenManagedAssetAsync(
		BookLocation location,
		Guid songFileId,
		CancellationToken cancellationToken = default);

	/// <summary>Begins an isolated, failure-safe staged write.</summary>
	Task<IStagedBookWrite> StageWriteAsync(BookLocation location, CancellationToken cancellationToken = default);

	/// <summary>Gets available bytes when the store supports capacity reporting.</summary>
	Task<long?> GetAvailableSpaceAsync(BookLocation location, CancellationToken cancellationToken = default);
}
