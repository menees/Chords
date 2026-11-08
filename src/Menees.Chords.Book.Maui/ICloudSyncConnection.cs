using Menees.Chords.Sync;

namespace Menees.Chords.Book.Maui;

/// <summary>Provides account access and book discovery for the shared cloud sync page.</summary>
public interface ICloudSyncConnection : IDisposable
{
	string ProviderName { get; }

	string AccountLabel { get; }

	string AccountName { get; }

	bool IsConnected { get; }

	string StateDirectory { get; }

	int MaximumConcurrentTransfers { get; }

	Task LoadAsync(CancellationToken cancellationToken);

	Task EnsureConnectedAsync(CancellationToken cancellationToken);

	Task ConnectAsync(CancellationToken cancellationToken);

	Task DisconnectAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<CloudBookChoice>> ListBooksAsync(CancellationToken cancellationToken);

	/// <summary>Opens the book's replica. The caller disposes the returned replica if it implements IDisposable.</summary>
	Task<ICloudReplica> OpenBookAsync(Guid bookId, CancellationToken cancellationToken);

	/// <summary>Opens a discovered replica. The caller disposes the returned replica if it implements IDisposable.</summary>
	ICloudReplica OpenFolder(string folderId);
}
