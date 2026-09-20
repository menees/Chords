#region Using Directives

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Publishes new asset versions before a guarded catalog commit. Never replaces or deletes existing sheet bytes.</summary>
public static class CloudBookPublisher
{
	#region Public Methods

	/// <summary>The caller must supply verified-source candidates from the confirmed merge and retain its local baseline.</summary>
	/// <remarks>
	/// This is one executor step, not end-to-end sync. Failures may leave unreferenced uploads, which must be reconciled by a durable executor.
	/// A successful cloud commit must be checkpointed even if later local application fails; never blindly replay a stale publication.
	/// </remarks>
	public static async Task<CloudBookPublication> PublishAsync(
		ICloudReplica replica,
		CloudBookComparison comparison,
		Func<SongFile, CancellationToken, Task<Stream>> openSource,
		string stagingDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(replica);
		ArgumentNullException.ThrowIfNull(comparison);
		ArgumentNullException.ThrowIfNull(openSource);
		ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
		cancellationToken.ThrowIfCancellationRequested();
		if (!replica.IsAuthenticated || !replica.Capabilities.HasFlag(CloudReplicaCapabilities.ConditionalMutation))
		{
			throw new InvalidOperationException("Connect a cloud provider supporting conditional catalog updates before publishing.");
		}

		Guid bookId = comparison.Merge.Database.Id;
		CloudReplicaKey expected = new(bookId, Guid.Empty, comparison.Target);
		if (!expected.Equals(new CloudReplicaKey(bookId, Guid.Empty, replica.Identity)) || comparison.CloudDatabase.Id != bookId)
		{
			throw new ArgumentException("The confirmed comparison belongs to another cloud replica.", nameof(comparison));
		}

		// Detach the catalog because physical version filenames must not mutate the caller's confirmed merge.
		ChordDatabase database = DatabaseJson.Deserialize(DatabaseJson.Serialize(comparison.Merge.Database));
		Dictionary<Guid, SongFile> previous = comparison.CloudDatabase.SongFiles.ToDictionary(file => file.Id);
		Dictionary<Guid, CloudReplicaItem> assets = [];
		foreach (SongFile file in database.SongFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (previous.TryGetValue(file.Id, out SongFile? old) && SameContent(old, file))
			{
				if (!comparison.ManagedItems.TryGetValue(file.Id, out CloudReplicaItem? existing)
					|| !PortableManagedFileName.Comparer.Equals(existing.Name, old.RelativePath))
				{
					throw new InvalidDataException("The comparison is missing a reusable cloud sheet.");
				}

				file.RelativePath = existing.Name;
				assets.Add(file.Id, existing);
			}
			else
			{
				await using Stream source = await openSource(file, cancellationToken).ConfigureAwait(false);
				long length = source.CanSeek ? source.Length - source.Position
					: file.ObservedLength ?? throw new InvalidDataException("The source sheet has no known length for integrity verification.");
				await using Stream staged = await VerifiedAssetStage.CreateAsync(
					source, file.ContentHash, length, stagingDirectory, cancellationToken).ConfigureAwait(false);
				string name = PortableManagedFileName.Create("Sync " + Guid.NewGuid().ToString("N"), file.Id, Path.GetExtension(file.RelativePath));
				CloudReplicaItem uploaded = await replica.CreateAsync(name, staged, cancellationToken).ConfigureAwait(false);
				if (!StringComparer.Ordinal.Equals(uploaded.Name, name) || uploaded.Length != staged.Length)
				{
					throw new InvalidDataException("The provider did not create the exact staged sheet version.");
				}

				file.RelativePath = name;
				file.ObservedLength = staged.Length;
				assets.Add(file.Id, uploaded);
			}
		}

		string json = DatabaseJson.Serialize(database);
		using MemoryStream catalog = new(Encoding.UTF8.GetBytes(json), writable: false);
		CloudReplicaItem committed = await replica.ReplaceAsync(
			comparison.DatabaseItem.Id, comparison.DatabaseItem.Version, catalog, cancellationToken).ConfigureAwait(false);
		return new(json, committed, new ReadOnlyDictionary<Guid, CloudReplicaItem>(assets));
	}

	#endregion

	#region Private Methods

	private static bool SameContent(SongFile left, SongFile right)
		=> !string.IsNullOrWhiteSpace(left.ContentHash) && StringComparer.OrdinalIgnoreCase.Equals(left.ContentHash, right.ContentHash);

	#endregion
}
