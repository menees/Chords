#region Using Directives

using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Confirmed, durable synchronization of one local book with one cloud replica.</summary>
public static class BookSyncCoordinator
{
	#region Private Data

	private const int MaximumTransferConcurrency = 16;

	#endregion

	#region Public Methods

	/// <summary>Resumes a valid interrupted transfer or preserves its sheets and compares current books afresh.</summary>
	public static async Task<BookSyncPreview> PrepareAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		SyncDirection direction,
		Guid recoveryEpoch,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(lease);
		RequireTarget(replica, lease);
		SyncTransferJournal journal = new(lease);
		SyncTransferDocument? pending = await journal.LoadAsync(cancellationToken).ConfigureAwait(false);
		if (pending is not null)
		{
			string localJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			CloudBookSnapshot? cloud = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			bool sameCloud = pending.CloudCatalog is null ? cloud is null
				: cloud is not null && cloud.DatabaseItem.Id.Value == pending.CloudCatalog.Id
					&& cloud.DatabaseItem.Version.Value == pending.CloudCatalog.Version;
			bool committedCloud = pending.Direction != SyncDirection.UpdateThisDevice
				&& cloud is not null && CatalogEquals(cloud, pending.TargetJson);
			if (pending.Direction != direction || pending.RecoveryEpoch != recoveryEpoch
				|| (localJson != pending.LocalJson && localJson != pending.TargetJson)
				|| (!sameCloud && !committedCloud))
			{
				await BookSyncRecovery.RefreshReviewAsync(store, location, replica, lease, cancellationToken).ConfigureAwait(false);
			}
		}

		return await PreviewAsync(store, location, replica, lease, direction, recoveryEpoch, cancellationToken).ConfigureAwait(false);
	}

	public static async Task<BookSyncPreview> PreviewAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		SyncDirection direction,
		Guid recoveryEpoch,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(replica);
		ArgumentNullException.ThrowIfNull(lease);
		RequireTarget(replica, lease);
		SyncTransferJournal journal = new(lease);
		SyncTransferDocument? pending = await journal.LoadAsync(cancellationToken).ConfigureAwait(false);
		BookSyncPreview result;
		if (pending is not null)
		{
			if (pending.RecoveryEpoch != recoveryEpoch)
			{
				throw new SyncMergeException("This book was restored after an interrupted sync. Retain the pending transfer for recovery before reconnecting.");
			}

			result = new(pending, resumed: true);
		}
		else
		{
			string localJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
			ChordDatabase local = DatabaseJson.Deserialize(localJson);
			if (local.Id != lease.Key.BookId)
			{
				throw new ArgumentException("The local book differs from the leased replica.", nameof(location));
			}

			CloudReplicaState state = await lease.LoadStateAsync(cancellationToken).ConfigureAwait(false);
			if (state.PendingJournal is not null)
			{
				throw new SyncMergeException("An older synchronization journal needs recovery before a new transfer can start.");
			}

			CloudBookSnapshot? snapshot = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			if (snapshot is null && direction == SyncDirection.UpdateThisDevice)
			{
				throw new SyncMergeException("The cloud book has not been uploaded yet. Use Two-Way or Update Cloud to publish this book first.");
			}

			BookMergeBase? baseline = state.NeedsFullComparison(recoveryEpoch) || state.MergeBase is null ? null
				: JsonSerializer.Deserialize<BookMergeBase>(state.MergeBase) ?? throw new InvalidDataException("The saved merge base is empty.");
			ChordDatabase? cloud = snapshot?.Database;
			string? canonicalLocal = null;
			string? canonicalCloud = null;
			bool sameCatalog = cloud is not null && (localJson == snapshot!.DatabaseJson
				|| (canonicalLocal = DatabaseJson.Serialize(local)) == (canonicalCloud = DatabaseJson.Serialize(cloud)));
			BookMergeResult merge = cloud is null ? new(local, [], [])
				: sameCatalog ? new(cloud, [], [])
				: BookSyncMerger.Merge(local, cloud, baseline, direction, lease.Key.DeviceId, DateTimeOffset.UtcNow);
			result = new(
				CreateDocument(localJson, local, cloud, snapshot, merge, lease.Key, direction, recoveryEpoch),
				resumed: false,
				canonicalLocal,
				canonicalCloud);
		}

		return result;
	}

	public static Task<BookSyncResult> ExecuteWithProgressAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		BookSyncPreview confirmed,
		IProgress<BookSyncProgress> progress,
		CancellationToken cancellationToken = default)
		=> ExecuteWithProgressAsync(store, location, replica, lease, confirmed, progress, 1, cancellationToken);

	/// <summary>Executes with phase-aware sheet progress; counts advance only after successful work.</summary>
	/// <remarks>A concurrency limit above one requires a replica supporting concurrent independent transfers.</remarks>
	public static Task<BookSyncResult> ExecuteWithProgressAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		BookSyncPreview confirmed,
		IProgress<BookSyncProgress> progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(progress);
		return ExecuteAsync(store, location, replica, lease, confirmed, new BookSyncProgressAdapter(progress), maximumConcurrentTransfers, cancellationToken);
	}

	public static Task<BookSyncResult> ExecuteAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		BookSyncPreview confirmed,
		IProgress<string>? progress = null,
		CancellationToken cancellationToken = default)
		=> ExecuteAsync(store, location, replica, lease, confirmed, progress, 1, cancellationToken);

	public static async Task<BookSyncResult> ExecuteAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		ReplicaStateLease lease,
		BookSyncPreview confirmed,
		IProgress<string>? progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(confirmed);
		ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrentTransfers, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumConcurrentTransfers, MaximumTransferConcurrency);
		RequireTarget(replica, lease);
		RequireCapabilities(replica);
		SyncTransferJournal journal = new(lease);
		SyncTransferDocument document = confirmed.Document;
		SyncTransferDocument? pending = await journal.LoadAsync(cancellationToken).ConfigureAwait(false);
		if (pending is not null && pending.Id != document.Id)
		{
			throw new SyncMergeException("Another confirmed transfer must be resumed first.");
		}

		string localJson = await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false);
		if (localJson != document.LocalJson && localJson != document.TargetJson)
		{
			throw new SyncMergeException("The local book changed after review. Both books were retained. Try syncing again to compare the latest books.");
		}

		if (pending is null)
		{
			await journal.SaveAsync(document, cancellationToken).ConfigureAwait(false);
		}

		await PublishAsync(store, location, replica, journal, document, progress, maximumConcurrentTransfers, cancellationToken).ConfigureAwait(false);

		if (localJson != document.TargetJson)
		{
			progress?.Report("Saving this device’s book…");
			Dictionary<Guid, SyncTransferAsset> assets = document.Assets.ToDictionary(asset => asset.FileId);
			await LocalBookSyncApplier.ApplyAsync(
				store,
				location,
				document.LocalJson,
				document.TargetJson,
				(file, token) => OpenStageAsync(journal, document, assets[file.Id], token),
				Path.Combine(lease.DirectoryPath, "temporary"),
				cancellationToken).ConfigureAwait(false);
		}

		DateTimeOffset completed = await CheckpointAsync(lease, document, cancellationToken).ConfigureAwait(false);
		journal.Clear(document);
		progress?.Report("Sync completed.");
		return new(confirmed.UploadCount, confirmed.DownloadCount, confirmed.RecoveryCount, confirmed.Conflicts.Count, completed);
	}

	#endregion

	#region Private Methods

	private static void ReportSheets(IProgress<string>? progress, string phase, int completed, int total)
	{
		string message = $"{phase}: {completed} of {total} sheets completed.";
		if (progress is IProgress<BookSyncProgress> structured)
		{
			structured.Report(new(message, completed, total));
		}
		else
		{
			progress?.Report(message);
		}
	}

	private static Task PublishAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		SyncTransferJournal journal,
		SyncTransferDocument document,
		IProgress<string>? progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken)
		=> document.Direction == SyncDirection.UpdateThisDevice
			? DownloadOnlyAsync(store, location, replica, journal, document, progress, maximumConcurrentTransfers, cancellationToken)
			: PublishWritableAsync(store, location, replica, journal, document, progress, maximumConcurrentTransfers, cancellationToken);

	private static async Task DownloadOnlyAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		SyncTransferJournal journal,
		SyncTransferDocument document,
		IProgress<string>? progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken)
	{
		SyncItemReceipt? receipt = await journal.ReadReceiptAsync(document, Guid.Empty, cancellationToken).ConfigureAwait(false);
		if (receipt is null)
		{
			CloudBookSnapshot? before = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			RequireCloudBaseline(document, before);
			await StageAssetsAsync(store, location, replica, journal, document, progress, maximumConcurrentTransfers, cancellationToken).ConfigureAwait(false);
			CloudBookSnapshot? after = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
			RequireCloudBaseline(document, after);
			RequireReusedAssets(document, after);
			await journal.SaveReceiptAsync(document, Guid.Empty, after!.DatabaseItem, cancellationToken).ConfigureAwait(false);
		}
		else if (document.CloudCatalog is null || receipt.Id != document.CloudCatalog.Id || receipt.Version != document.CloudCatalog.Version)
		{
			throw new InvalidDataException("The recorded cloud review does not match this transfer.");
		}
	}

	private static async Task StageAssetsAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		SyncTransferJournal journal,
		SyncTransferDocument document,
		IProgress<string>? progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken)
	{
		int total = document.Assets.Count(asset => asset.Stage);
		int completed = 0;
		ReportSheets(progress, "Preparing changed sheets", completed, total);
		object progressGate = new();
		async Task StageAsync(SyncTransferAsset asset, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			string path = journal.AssetPath(document, asset.FileId);
			if (!File.Exists(path))
			{
				await RetryAsync(
					async () =>
					{
						await SyncAssetStager.StageAsync(store, location, replica, asset, path, token).ConfigureAwait(false);
						return true;
					},
					token).ConfigureAwait(false);
			}

			lock (progressGate)
			{
				ReportSheets(progress, "Preparing changed sheets", ++completed, total);
			}
		}

		// Local store contracts remain serialized; only independent cloud downloads overlap.
		await TransferSheetsAsync(
			document.Assets.Where(asset => asset.Stage && asset.LocalSourceId != Guid.Empty),
			1,
			StageAsync,
			cancellationToken).ConfigureAwait(false);
		await TransferSheetsAsync(
			document.Assets.Where(asset => asset.Stage && asset.LocalSourceId == Guid.Empty),
			maximumConcurrentTransfers,
			StageAsync,
			cancellationToken).ConfigureAwait(false);

		// Recheck after staging and before any upload. A stale review never writes a new catalog.
		if (await store.ReadDatabaseJsonAsync(location, cancellationToken).ConfigureAwait(false) != document.LocalJson)
		{
			throw new SyncMergeException("The local book changed during staging. Neither catalog was replaced; the confirmed transfer was retained.");
		}
	}

	private static async Task PublishWritableAsync(
		IBookStore store,
		BookLocation location,
		ICloudReplica replica,
		SyncTransferJournal journal,
		SyncTransferDocument document,
		IProgress<string>? progress,
		int maximumConcurrentTransfers,
		CancellationToken cancellationToken)
	{
		progress?.Report("Checking interrupted cloud operations…");
		CloudBookSnapshot? current = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
		SyncItemReceipt? commitReceipt = await journal.ReadReceiptAsync(document, Guid.Empty, cancellationToken).ConfigureAwait(false);
		if (commitReceipt is not null && (commitReceipt.Name != "database.json"
			|| commitReceipt.Length != Encoding.UTF8.GetByteCount(document.TargetJson)
			|| current is null || current.DatabaseItem.Id.Value != commitReceipt.Id
			|| current.Database.Id != document.BookId))
		{
			throw new InvalidDataException("The recorded cloud commit does not match this book and transfer.");
		}

		bool cloudCommitted = commitReceipt is not null || (current is not null && CatalogEquals(current, document.TargetJson));
		if (!cloudCommitted)
		{
			RequireCloudBaseline(document, current);
			await StageAssetsAsync(store, location, replica, journal, document, progress, maximumConcurrentTransfers, cancellationToken).ConfigureAwait(false);

			CloudChangeSet inventory = await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false);
			ConcurrentDictionary<string, CloudReplicaItem> uploadItems = new(CloudBookReader.IndexItems(inventory), StringComparer.OrdinalIgnoreCase);
			int total = document.Assets.Count(asset => asset.Upload);
			int completed = 0;
			ReportSheets(progress, "Uploading", completed, total);
			object progressGate = new();
			await TransferSheetsAsync(
				document.Assets.Where(asset => asset.Upload),
				maximumConcurrentTransfers,
				async (asset, token) =>
				{
					await RetryAsync(
						async () =>
						{
							await PublishAssetAsync(replica, journal, document, asset, uploadItems, token).ConfigureAwait(false);
							return true;
						},
						token).ConfigureAwait(false);
					lock (progressGate)
					{
						ReportSheets(progress, "Uploading", ++completed, total);
					}
				},
				cancellationToken).ConfigureAwait(false);

			progress?.Report("Saving the cloud book…");
			CloudReplicaItem committed = await RetryAsync(
				async () =>
			{
				CloudBookSnapshot? latest = await CloudBookReader.ReadAsync(replica, cancellationToken).ConfigureAwait(false);
				CloudReplicaItem result;
				if (latest is not null && CatalogEquals(latest, document.TargetJson))
				{
					result = latest.DatabaseItem;
				}
				else
				{
					RequireCloudBaseline(document, latest);
					RequireReusedAssets(document, latest);
					IReadOnlyDictionary<string, CloudReplicaItem> finalItems = latest?.FolderItems
						?? CloudBookReader.IndexItems(await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false));
					await RequireUploadedAssetsAsync(journal, document, finalItems, cancellationToken).ConfigureAwait(false);
					using MemoryStream bytes = new(Encoding.UTF8.GetBytes(document.TargetJson), writable: false);
					result = document.CloudCatalog is null
						? await replica.CreateAsync("database.json", bytes, cancellationToken).ConfigureAwait(false)
						: await replica.ReplaceAsync(
							new(document.CloudCatalog.Id), new(document.CloudCatalog.Version), bytes, cancellationToken).ConfigureAwait(false);
				}

				return result;
			},
				cancellationToken).ConfigureAwait(false);
			await journal.SaveReceiptAsync(document, Guid.Empty, committed, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			// This also covers a committed request whose response or local receipt was lost.
			if (commitReceipt is null)
			{
				await journal.SaveReceiptAsync(document, Guid.Empty, current!.DatabaseItem, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private static async Task<DateTimeOffset> CheckpointAsync(
		ReplicaStateLease lease, SyncTransferDocument document, CancellationToken cancellationToken)
	{
		// If another device published afterward, this base still describes exactly what this device accepted.
		CloudReplicaState state = await lease.LoadStateAsync(cancellationToken).ConfigureAwait(false);
		DateTimeOffset completed = DateTimeOffset.UtcNow;
		state.LastSuccessfulSyncUtc = completed;
		state.LastLocalRevision = document.GetTargetDatabase().Revision.Revision.ToString();
		state.RecoveryEpoch = document.RecoveryEpoch;
		ChordDatabase acceptedCloud = document.Direction == SyncDirection.UpdateThisDevice ? document.GetCloudDatabase()! : document.GetTargetDatabase();
		state.MergeBase = JsonSerializer.Serialize(BookSyncMerger.CreateBase(acceptedCloud));
		state.TrackedItems.Clear();
		await lease.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
		return completed;
	}

	private static SyncTransferDocument CreateDocument(
		string localJson,
		ChordDatabase local,
		ChordDatabase? cloud,
		CloudBookSnapshot? snapshot,
		BookMergeResult merge,
		CloudReplicaKey key,
		SyncDirection direction,
		Guid recoveryEpoch)
	{
		SyncTransferDocument document = new()
		{
			BookId = key.BookId,
			DeviceId = key.DeviceId,
			Target = new(key.ProviderKind, key.AccountId, key.RemoteFolderId),
			Direction = direction,
			LocalJson = localJson,
			RecoveryEpoch = recoveryEpoch,
			CloudCatalog = snapshot is null ? null : SyncItemReceipt.From(snapshot.DatabaseItem),
			CloudJson = snapshot?.DatabaseJson,
			Conflicts = [.. merge.Conflicts],
			RecoveryCount = merge.Recoveries.Count,
		};
		Dictionary<Guid, SongFile> left = local.SongFiles.ToDictionary(file => file.Id);
		Dictionary<Guid, SongFile> right = cloud?.SongFiles.ToDictionary(file => file.Id) ?? [];
		Dictionary<Guid, SyncRecoverySource> recoveries = merge.Recoveries.ToDictionary(source => source.RecoveryFileId);
		Dictionary<Guid, string> titles = merge.Database.Songs.ToDictionary(song => song.Id, song => song.Title);
		const int VersionIdBytes = 8;
		string versionId = Convert.ToHexStringLower(SHA256.HashData(document.Id.ToByteArray()).AsSpan(0, VersionIdBytes));
		foreach (SongFile file in merge.Database.SongFiles)
		{
			left.TryGetValue(file.Id, out SongFile? localFile);
			right.TryGetValue(file.Id, out SongFile? cloudFile);
			bool upload = direction != SyncDirection.UpdateThisDevice && !SameContent(file, cloudFile);
			bool stage = upload || !SameContent(file, localFile);
			Guid localSourceId = Guid.Empty;
			SyncItemReceipt? sourceItem = null;
			if (recoveries.TryGetValue(file.Id, out SyncRecoverySource? recovery))
			{
				if (recovery.Side == SyncSide.Local)
				{
					localSourceId = recovery.OriginalFileId;
					localFile = left[localSourceId];
				}
				else
				{
					sourceItem = SyncItemReceipt.From(snapshot!.ManagedItems[recovery.OriginalFileId]);
				}
			}
			else if (SameContent(file, localFile))
			{
				localSourceId = file.Id;
			}
			else if (SameContent(file, cloudFile))
			{
				sourceItem = SyncItemReceipt.From(snapshot!.ManagedItems[file.Id]);
			}
			else
			{
				throw new SyncMergeException("The selected sheet has no matching source bytes. Neither book was changed.");
			}

			long length = sourceItem?.Length ?? localFile?.ObservedLength
				?? throw new SyncMergeException("A sheet has no recorded length. Validate or reconcile the book before syncing.");
			if (upload)
			{
				string prefix = file.RecoveryVersion is not null ? "Recovered " : file.SavedVersion is not null ? "Previous " : string.Empty;
				string caption = prefix + titles[file.SongId];
				file.RelativePath = PortableManagedFileName.Create(caption + " " + versionId, file.Id, Path.GetExtension(file.RelativePath));
			}
			else if (cloudFile is not null && SameContent(file, cloudFile))
			{
				file.RelativePath = snapshot!.ManagedItems[file.Id].Name;

				// Capture the remote version even when local bytes are the staging source.
				sourceItem ??= SyncItemReceipt.From(snapshot.ManagedItems[file.Id]);
			}

			file.ObservedLength = length;
			document.Assets.Add(new(file.Id, file.RelativePath, file.ContentHash, length, localSourceId, sourceItem, stage, upload));
		}

		document.TargetJson = DatabaseJson.Serialize(merge.Database);
		document.SetParsedTarget(merge.Database);
		return document;
	}

	private static bool SameContent(SongFile file, SongFile? other)
		=> other is not null && !string.IsNullOrWhiteSpace(file.ContentHash)
			&& StringComparer.OrdinalIgnoreCase.Equals(file.ContentHash, other.ContentHash);

	private static bool CatalogEquals(CloudBookSnapshot left, string right) => left.DatabaseJson == right || DatabaseJson.Serialize(left.Database) == right;

	private static void RequireTarget(ICloudReplica replica, ReplicaStateLease lease)
	{
		ArgumentNullException.ThrowIfNull(replica);
		ArgumentNullException.ThrowIfNull(lease);
		if (!lease.Key.Equals(new CloudReplicaKey(lease.Key.BookId, lease.Key.DeviceId, replica.Identity)))
		{
			throw new ArgumentException("The cloud target differs from the leased replica.", nameof(replica));
		}
	}

	private static void RequireCapabilities(ICloudReplica replica)
	{
		if (!replica.IsAuthenticated || !replica.Capabilities.HasFlag(CloudReplicaCapabilities.ConditionalMutation))
		{
			throw new InvalidOperationException("Connect a cloud provider supporting conditional catalog updates before syncing.");
		}
	}

	private static void RequireCloudBaseline(SyncTransferDocument document, CloudBookSnapshot? current)
	{
		if (document.CloudCatalog is null ? current is not null
			: current is null || current.DatabaseItem.Id.Value != document.CloudCatalog.Id
				|| current.DatabaseItem.Version.Value != document.CloudCatalog.Version)
		{
			throw new SyncMergeException("The cloud book changed after review. Both books were retained. Try syncing again to compare the latest books.");
		}
	}

	private static void RequireReusedAssets(SyncTransferDocument document, CloudBookSnapshot? current)
	{
		foreach (SyncTransferAsset asset in document.Assets.Where(asset => !asset.Upload && asset.SourceItem is not null))
		{
			if (current is null || !current.ManagedItems.TryGetValue(asset.FileId, out CloudReplicaItem? item)
				|| asset.SourceItem is null || item.Id.Value != asset.SourceItem.Id || item.Version.Value != asset.SourceItem.Version)
			{
				throw new SyncMergeException("A referenced cloud sheet changed during sync. Neither catalog was replaced.");
			}
		}
	}

	private static async Task RequireUploadedAssetsAsync(
		SyncTransferJournal journal,
		SyncTransferDocument document,
		IReadOnlyDictionary<string, CloudReplicaItem> items,
		CancellationToken cancellationToken)
	{
		foreach (SyncTransferAsset asset in document.Assets.Where(asset => asset.Upload))
		{
			SyncItemReceipt? receipt = await journal.ReadReceiptAsync(document, asset.FileId, cancellationToken).ConfigureAwait(false);
			if (receipt is null || !items.TryGetValue(asset.Name, out CloudReplicaItem? item)
				|| item.Id.Value != receipt.Id || item.Version.Value != receipt.Version || item.Length != asset.Length)
			{
				throw new SyncMergeException("An uploaded cloud sheet changed before commit. Neither catalog was replaced; refresh the review.");
			}
		}
	}

	private static async Task<Stream> OpenStageAsync(
		SyncTransferJournal journal,
		SyncTransferDocument document,
		SyncTransferAsset asset,
		CancellationToken cancellationToken)
	{
		string path = journal.AssetPath(document, asset.FileId);
		await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		return await VerifiedAssetStage.CreateAsync(
			input, asset.ContentHash, asset.Length, Path.GetDirectoryName(path)!, cancellationToken).ConfigureAwait(false);
	}

	private static async Task PublishAssetAsync(
		ICloudReplica replica,
		SyncTransferJournal journal,
		SyncTransferDocument document,
		SyncTransferAsset asset,
		ConcurrentDictionary<string, CloudReplicaItem> items,
		CancellationToken cancellationToken)
	{
		SyncItemReceipt? receipt = await journal.ReadReceiptAsync(document, asset.FileId, cancellationToken).ConfigureAwait(false);
		CloudReplicaItem item;
		if (items.TryGetValue(asset.Name, out CloudReplicaItem? existing))
		{
			if (receipt is null || existing.Id.Value != receipt.Id || existing.Version.Value != receipt.Version)
			{
				// An upload might have succeeded before its response/receipt was persisted. Check the bytes before acknowledging it.
				await using Stream bytes = await replica.DownloadAsync(existing.Id, cancellationToken).ConfigureAwait(false);
				await using Stream verified = await VerifiedAssetStage.CreateAsync(
					bytes,
					asset.ContentHash,
					asset.Length,
					Path.GetDirectoryName(journal.AssetPath(document, asset.FileId))!,
					cancellationToken).ConfigureAwait(false);
			}

			item = existing;
		}
		else
		{
			await using Stream staged = await OpenStageAsync(journal, document, asset, cancellationToken).ConfigureAwait(false);
			try
			{
				item = await replica.CreateAsync(asset.Name, staged, cancellationToken).ConfigureAwait(false);
				items[asset.Name] = item;
			}
			catch (CloudRequestException exception) when (exception.IsTransient || exception.IsConcurrencyFailure)
			{
				CloudChangeSet latest = await replica.ListOrGetChangesAsync(null, cancellationToken).ConfigureAwait(false);
				foreach (KeyValuePair<string, CloudReplicaItem> pair in CloudBookReader.IndexItems(latest))
				{
					items[pair.Key] = pair.Value;
				}

				throw;
			}
		}

		if (item.Name != asset.Name || item.Length != asset.Length)
		{
			throw new InvalidDataException("The cloud sheet does not match the confirmed transfer.");
		}

		await journal.SaveReceiptAsync(document, asset.FileId, item, cancellationToken).ConfigureAwait(false);
	}

	private static async Task TransferSheetsAsync(
		IEnumerable<SyncTransferAsset> assets,
		int maximumConcurrency,
		Func<SyncTransferAsset, CancellationToken, Task> transfer,
		CancellationToken cancellationToken)
	{
		if (maximumConcurrency == 1)
		{
			foreach (SyncTransferAsset asset in assets)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await transfer(asset, cancellationToken).ConfigureAwait(false);
			}
		}
		else
		{
			// Failure cancels siblings and awaits them before catalog mutation or journal cleanup.
			await Parallel.ForEachAsync(
				assets,
				new ParallelOptions { MaxDegreeOfParallelism = maximumConcurrency, CancellationToken = cancellationToken },
				async (asset, token) => await transfer(asset, token).ConfigureAwait(false)).ConfigureAwait(false);
		}
	}

	private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
	{
		const int MaximumRetries = 2;
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				return await operation().ConfigureAwait(false);
			}
			catch (CloudRequestException exception) when (exception.IsTransient && attempt < MaximumRetries)
			{
				TimeSpan delay = exception.RetryAfter ?? TimeSpan.FromSeconds(attempt + 1);
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	#endregion
}
