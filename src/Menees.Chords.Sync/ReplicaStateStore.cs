using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Menees.Chords.Sync;

/// <summary>Device-local replica checkpoints. The supplied directory must be outside the book and its backups.</summary>
public sealed class ReplicaStateStore
{
	private readonly string directory;

	public ReplicaStateStore(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		this.directory = Path.GetFullPath(directory);
	}

	/// <summary>Exclusively leases one replica across processes; another provider or account is independent.</summary>
	public ReplicaStateLease Acquire(CloudReplicaKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (key.BookId == Guid.Empty || key.DeviceId == Guid.Empty)
		{
			throw new ArgumentException("Book and device identities must be nonempty.", nameof(key));
		}

		// Serialize components separately so opaque provider IDs cannot create path traversal or key collisions.
		byte[] identity = JsonSerializer.SerializeToUtf8Bytes(new[]
		{
			key.BookId.ToString("D"), key.DeviceId.ToString("D"), key.ProviderKind, key.AccountId, key.RemoteFolderId,
		});
		string path = Path.Combine(this.directory, Convert.ToHexString(SHA256.HashData(identity)));
		Directory.CreateDirectory(path);
		FileStream lease = new(Path.Combine(path, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		return new ReplicaStateLease(key, path, lease);
	}
}
