#region Using Directives

using System.Text.Json.Serialization;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Versioned, bound payload. Receipts are separate so progress does not rewrite the catalogs.</summary>
internal sealed class SyncTransferDocument
{
	#region Private Data

	private string localJson = string.Empty;
	private string targetJson = string.Empty;
	private string? cloudJson;
	private ChordDatabase? localDatabase;
	private ChordDatabase? targetDatabase;
	private ChordDatabase? cloudDatabase;

	#endregion

	#region Public Properties

	public int SchemaVersion { get; set; } = 2;

	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid BookId { get; set; }

	public Guid DeviceId { get; set; }

	public Guid RecoveryEpoch { get; set; }

	public CloudReplicaIdentity? Target { get; set; }

	public SyncDirection Direction { get; set; }

	public string LocalCatalogHash { get; set; } = string.Empty;

	public string TargetCatalogHash { get; set; } = string.Empty;

	public string? CloudCatalogHash { get; set; }

	[JsonIgnore]
	public string LocalJson
	{
		get => this.localJson;
		set
		{
			this.localJson = value;
			this.localDatabase = null;
		}
	}

	[JsonIgnore]
	public string TargetJson
	{
		get => this.targetJson;
		set
		{
			this.targetJson = value;
			this.targetDatabase = null;
		}
	}

	[JsonIgnore]
	public string? CloudJson
	{
		get => this.cloudJson;
		set
		{
			this.cloudJson = value;
			this.cloudDatabase = null;
		}
	}

	public SyncItemReceipt? CloudCatalog { get; set; }

	public List<SyncTransferAsset> Assets { get; set; } = [];

	public List<SyncConflict> Conflicts { get; set; } = [];

	public int RecoveryCount { get; set; }

	#endregion

	#region Internal Methods

	internal ChordDatabase GetLocalDatabase() => this.localDatabase ??= DatabaseJson.Deserialize(this.LocalJson);

	internal ChordDatabase GetTargetDatabase()
		=> this.targetDatabase ??= this.TargetJson == this.LocalJson ? this.GetLocalDatabase() : DatabaseJson.Deserialize(this.TargetJson);

	internal ChordDatabase? GetCloudDatabase()
		=> this.CloudJson is null ? null : this.cloudDatabase
			??= this.CloudJson == this.LocalJson ? this.GetLocalDatabase() : DatabaseJson.Deserialize(this.CloudJson);

	internal void SetParsedTarget(ChordDatabase database) => this.targetDatabase = database;

	#endregion
}
