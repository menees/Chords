using System.Threading;
using System.Threading.Tasks;

namespace Menees.Chords.Sync.OneDrive;

/// <summary>Supplies account-bound Graph tokens. Interactive sign-in and protected caching belong to the host.</summary>
public interface IOneDriveTokenProvider
{
	string? AccountId { get; }

	Task AuthenticateAsync(CancellationToken cancellationToken);

	Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

	Task DisconnectAsync(CancellationToken cancellationToken);
}
