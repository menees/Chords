#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Durable confirmed payload, staged bytes and constant-size receipts under the exclusive replica lease.</summary>
internal sealed class SyncTransferJournal
{
	#region Private Data

	private const int Sha256HexLength = 64;
	private const long MaximumDocumentBytes = 128 * 1024 * 1024;
	private readonly ReplicaStateLease lease;
	private readonly string planPath;

	#endregion

	#region Constructors

	public SyncTransferJournal(ReplicaStateLease lease)
	{
		this.lease = lease;
		this.planPath = Path.Combine(lease.DirectoryPath, "transfer.json");
	}

	#endregion

	#region Public Methods

	public async Task<SyncTransferDocument?> LoadAsync(CancellationToken cancellationToken)
	{
		SyncTransferDocument? document = await ReplicaJsonFile.ReadAsync<SyncTransferDocument>(this.planPath, cancellationToken).ConfigureAwait(false);
		if (document is not null)
		{
			this.RequireIdentity(document);
			document.LocalJson = await this.ReadCatalogAsync(document, document.LocalCatalogHash, cancellationToken).ConfigureAwait(false);
			document.TargetJson = document.TargetCatalogHash == document.LocalCatalogHash ? document.LocalJson
				: await this.ReadCatalogAsync(document, document.TargetCatalogHash, cancellationToken).ConfigureAwait(false);
			document.CloudJson = document.CloudCatalogHash is null ? null
				: document.CloudCatalogHash == document.LocalCatalogHash ? document.LocalJson
				: await this.ReadCatalogAsync(document, document.CloudCatalogHash, cancellationToken).ConfigureAwait(false);
			this.Validate(document);
		}

		return document;
	}

	public async Task SaveAsync(SyncTransferDocument document, CancellationToken cancellationToken)
	{
		this.Validate(document);
		document.LocalCatalogHash = await this.SaveCatalogAsync(document, document.LocalJson, cancellationToken).ConfigureAwait(false);
		document.TargetCatalogHash = await this.SaveCatalogAsync(document, document.TargetJson, cancellationToken).ConfigureAwait(false);
		document.CloudCatalogHash = document.CloudJson is null ? null
			: await this.SaveCatalogAsync(document, document.CloudJson, cancellationToken).ConfigureAwait(false);
		await ReplicaJsonFile.WriteAsync(this.planPath, document, cancellationToken).ConfigureAwait(false);
	}

	public string AssetPath(SyncTransferDocument document, Guid fileId) => Path.Combine(this.WorkDirectory(document), fileId.ToString("N") + ".asset");

	public Task<SyncItemReceipt?> ReadReceiptAsync(SyncTransferDocument document, Guid fileId, CancellationToken cancellationToken)
		=> ReplicaJsonFile.ReadAsync<SyncItemReceipt>(this.ReceiptPath(document, fileId), cancellationToken);

	public Task SaveReceiptAsync(SyncTransferDocument document, Guid fileId, CloudReplicaItem item, CancellationToken cancellationToken)
		=> ReplicaJsonFile.WriteAsync(this.ReceiptPath(document, fileId), SyncItemReceipt.From(item), cancellationToken);

	public async Task RetireAsync(SyncTransferDocument document, CancellationToken cancellationToken)
	{
		this.Validate(document);
		await ReplicaJsonFile.WriteAsync(Path.Combine(this.WorkDirectory(document), "retired-review.json"), document, cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		File.Delete(this.planPath);
	}

	public void Clear(SyncTransferDocument document)
	{
		string work = this.WorkDirectory(document);
		File.Delete(this.planPath);

		// Only this validated transfer's GUID directory is owned by the journal.
		if (Directory.Exists(work))
		{
			Directory.Delete(work, recursive: true);
		}
	}

	#endregion

	#region Private Methods

	private static bool ValidReceipt(SyncItemReceipt receipt)
		=> !string.IsNullOrWhiteSpace(receipt.Id) && !string.IsNullOrWhiteSpace(receipt.Name)
			&& !string.IsNullOrWhiteSpace(receipt.Version) && receipt.Length >= 0;

	private string WorkDirectory(SyncTransferDocument document) => Path.Combine(this.lease.DirectoryPath, document.Id.ToString("N"));

	private string ReceiptPath(SyncTransferDocument document, Guid fileId) => Path.Combine(this.WorkDirectory(document), fileId.ToString("N") + ".json");

	private async Task<string> SaveCatalogAsync(SyncTransferDocument document, string json, CancellationToken cancellationToken)
	{
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
		string path = Path.Combine(this.WorkDirectory(document), hash + ".catalog");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (!File.Exists(path))
		{
			string temporary = path + ".tmp";
			try
			{
				await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					const int BufferSize = 8192;
					using StreamWriter writer = new(stream, new UTF8Encoding(false, true), BufferSize, leaveOpen: true);
					await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
					await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
					stream.Flush(flushToDisk: true);
				}

				cancellationToken.ThrowIfCancellationRequested();
				File.Move(temporary, path, overwrite: true);
			}
			finally
			{
				File.Delete(temporary);
			}
		}

		return hash;
	}

	private async Task<string> ReadCatalogAsync(SyncTransferDocument document, string hash, CancellationToken cancellationToken)
	{
		if (hash is null || hash.Length != Sha256HexLength || !hash.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("The captured catalog reference is invalid. The transfer was retained.");
		}

		string path = Path.Combine(this.WorkDirectory(document), hash + ".catalog");
		if (new FileInfo(path).Length > MaximumDocumentBytes)
		{
			throw new InvalidDataException("The captured catalog exceeds its size limit.");
		}

		string json = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
		string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
		if (!StringComparer.OrdinalIgnoreCase.Equals(hash, actual))
		{
			throw new InvalidDataException("A captured catalog is corrupt. The transfer and sheets were retained for recovery.");
		}

		return json;
	}

	private void RequireIdentity(SyncTransferDocument document)
	{
		CloudReplicaKey key = this.lease.Key;
		if (document.SchemaVersion != 2 || document.Id == Guid.Empty || document.Target is null
			|| !key.Equals(new CloudReplicaKey(document.BookId, document.DeviceId, document.Target))
			|| !Enum.IsDefined(document.Direction) || document.Assets is null || document.Conflicts is null
			|| document.LocalJson is null || document.TargetJson is null || document.RecoveryCount < 0
			|| (document.Direction == SyncDirection.UpdateThisDevice && document.CloudCatalog is null))
		{
			throw new InvalidDataException("The confirmed transfer is unsupported or belongs to another replica.");
		}
	}

	private void Validate(SyncTransferDocument document)
	{
		this.RequireIdentity(document);
		CloudReplicaKey key = this.lease.Key;
		ChordDatabase local = document.GetLocalDatabase();
		ChordDatabase target = document.GetTargetDatabase();
		Dictionary<Guid, SongFile> files = target.SongFiles.ToDictionary(file => file.Id);
		HashSet<Guid> assetIds = [];
		ChordDatabase? cloud = document.GetCloudDatabase();
		if ((document.CloudCatalog is null) != (document.CloudJson is null)
			|| local.Id != key.BookId || target.Id != key.BookId || (cloud is not null && cloud.Id != key.BookId)
			|| (document.CloudCatalog is not null && (document.CloudCatalog.Name != "database.json" || !ValidReceipt(document.CloudCatalog))))
		{
			throw new InvalidDataException("The confirmed catalogs belong to another book.");
		}

		foreach (SyncTransferAsset asset in document.Assets)
		{
			if (asset is null || !assetIds.Add(asset.FileId) || !files.TryGetValue(asset.FileId, out SongFile? file)
				|| asset.Name != file.RelativePath || !StringComparer.OrdinalIgnoreCase.Equals(asset.ContentHash, file.ContentHash)
				|| asset.Length < 0 || asset.ContentHash is null || asset.ContentHash.Length != Sha256HexLength || !asset.ContentHash.All(Uri.IsHexDigit)
				|| (asset.SourceItem is not null && !ValidReceipt(asset.SourceItem))
				|| (document.Direction == SyncDirection.UpdateThisDevice && asset.Upload)
				|| (asset.Upload && !asset.Stage) || (asset.LocalSourceId == Guid.Empty && asset.SourceItem is null))
			{
				throw new InvalidDataException("The confirmed transfer has invalid sheet sources or integrity metadata.");
			}
		}

		if (assetIds.Count != files.Count)
		{
			throw new InvalidDataException("The confirmed transfer does not describe every referenced sheet.");
		}
	}

	#endregion
}
