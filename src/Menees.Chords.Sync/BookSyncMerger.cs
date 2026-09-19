#region Using Directives

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Sync;

/// <summary>Pure, provider-neutral, whole-entity comparison. Does not read assets or mutate either input.</summary>
public sealed class BookSyncMerger
{
	#region Private Data

	private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
	private readonly BookMergeBase baseline;
	private readonly SyncDirection direction;
	private readonly DateTimeOffset now;
	private readonly Guid deviceId;
	private readonly List<SyncConflict> conflicts = [];
	private readonly List<SyncRecoverySource> recoveries = [];
	private readonly Dictionary<string, Tombstone> tombstones = new(StringComparer.Ordinal);
	private readonly List<SongFile> recoveryFiles = [];

	#endregion

	#region Constructors

	private BookSyncMerger(BookMergeBase baseline, SyncDirection direction, Guid deviceId, DateTimeOffset now)
	{
		this.baseline = baseline;
		this.direction = direction;
		this.deviceId = deviceId;
		this.now = now;
	}

	#endregion

	#region Public Methods

	public static BookMergeResult Merge(
		ChordDatabase local, ChordDatabase cloud, BookMergeBase? baseline, SyncDirection direction, Guid deviceId, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(local);
		ArgumentNullException.ThrowIfNull(cloud);
		if (!Enum.IsDefined(direction) || deviceId == Guid.Empty)
		{
			throw new ArgumentException("Choose a valid sync direction and device identity.");
		}

		DatabaseValidation.ThrowIfInvalid(local);
		DatabaseValidation.ThrowIfInvalid(cloud);
		if (local.Id != cloud.Id || (baseline is not null && baseline.BookId != local.Id))
		{
			throw new SyncMergeException("The local book, cloud book and merge base must have the same book identity.");
		}

		BookSyncMerger merger = new(baseline ?? new() { BookId = local.Id }, direction, deviceId, now);
		return merger.MergeCore(local, cloud);
	}

	public static BookMergeBase CreateBase(ChordDatabase database)
	{
		ArgumentNullException.ThrowIfNull(database);
		BookMergeBase result = new() { BookId = database.Id };
		void Add<T>(IEnumerable<T> values, Func<T, Guid> id)
		{
			foreach (T value in values)
			{
				result.Entities.Add(Key<T>(id(value)), SyncEntityFingerprint.Create(value));
			}
		}

		Add(database.Songs, value => value.Id);
		Add(database.SongFiles, value => value.Id);
		Add(database.Setlists, value => value.Id);
		Add(database.InstrumentProfiles, value => value.Id);
		Add(database.SongInstrumentSettings, value => value.Id);
		Add(database.CustomTabs, value => value.Id);
		result.Entities[Key<BookSettings>(database.Id)] = SyncEntityFingerprint.Create(database.BookSettings);
		result.Entities[Key<BookName>(database.Id)] = SyncEntityFingerprint.Create(new BookName(database.Name, database.Revision));
		result.FileHashes = database.SongFiles.ToDictionary(file => file.Id, file => file.ContentHash.ToLowerInvariant());
		return result;
	}

	#endregion

	#region Private Methods

	private static string Key<T>(Guid id) => typeof(T).Name + ":" + id.ToString("D");

	private BookMergeResult MergeCore(ChordDatabase local, ChordDatabase cloud)
	{
		foreach (Tombstone deletion in local.Tombstones.Concat(cloud.Tombstones))
		{
			string key = deletion.EntityType + ":" + deletion.EntityId.ToString("D");
			if (!this.tombstones.TryGetValue(key, out Tombstone? existing) || deletion.Revision.ModifiedUtc > existing.Revision.ModifiedUtc)
			{
				this.tombstones[key] = SyncEntityFingerprint.Clone(deletion);
			}
		}

		ChordDatabase merged = new()
		{
			Id = local.Id,
			Name = this.Select(
				Key<BookName>(local.Id),
				new BookName(local.Name, local.Revision),
				new BookName(cloud.Name, cloud.Revision),
				value => value.Revision).Name,
			BookSettings = this.Select(Key<BookSettings>(local.Id), local.BookSettings, cloud.BookSettings, value => value.Revision),
			Songs = this.MergeEntities(local.Songs, cloud.Songs, value => value.Id, value => value.Revision),
			SongFiles = this.MergeEntities(local.SongFiles, cloud.SongFiles, value => value.Id, value => value.Revision),
			Setlists = this.MergeEntities(local.Setlists, cloud.Setlists, value => value.Id, value => value.Revision),
			InstrumentProfiles = this.MergeEntities(local.InstrumentProfiles, cloud.InstrumentProfiles, value => value.Id, value => value.Revision),
			SongInstrumentSettings = this.MergeEntities(local.SongInstrumentSettings, cloud.SongInstrumentSettings, value => value.Id, value => value.Revision),
			CustomTabs = this.MergeEntities(local.CustomTabs, cloud.CustomTabs, value => value.Id, value => value.Revision),
			Tombstones = [.. this.tombstones.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)],
			Revision = new()
			{
				Revision = checked(Math.Max(local.Revision.Revision, cloud.Revision.Revision) + 1),
				DeviceId = this.deviceId,
				ModifiedUtc = this.now,
			},
		};
		HashSet<Guid> fileIds = [.. merged.SongFiles.Select(file => file.Id)];
		foreach (SongFile recovery in this.recoveryFiles)
		{
			if (fileIds.Add(recovery.Id))
			{
				merged.SongFiles.Add(recovery);
			}
		}

		IReadOnlyList<ValidationProblem> problems = DatabaseValidation.Validate(merged);
		if (problems.Count > 0)
		{
			throw new SyncMergeException("Concurrent changes leave unresolved song, sheet, instrument or setlist references. "
				+ "Neither replica was changed. Resolve the related deletion or reference before syncing. " + problems[0].Message);
		}

		return new(merged, this.conflicts, this.recoveries);
	}

	private List<T> MergeEntities<T>(IReadOnlyList<T> local, IReadOnlyList<T> cloud, Func<T, Guid> id, Func<T, RevisionStamp> stamp)
		where T : class
	{
		Dictionary<Guid, T> left = local.ToDictionary(id);
		Dictionary<Guid, T> right = cloud.ToDictionary(id);
		List<T> result = [];
		foreach (Guid identity in left.Keys.Union(right.Keys).Order())
		{
			string key = Key<T>(identity);
			left.TryGetValue(identity, out T? localValue);
			right.TryGetValue(identity, out T? cloudValue);
			if (this.tombstones.ContainsKey(key))
			{
				foreach (T value in new[] { localValue, cloudValue }.OfType<T>())
				{
					if (!this.baseline.Entities.TryGetValue(key, out string? prior) || SyncEntityFingerprint.Create(value) != prior)
					{
						throw new SyncMergeException($"{key} was deleted on one replica and edited on the other. Resolve the deletion before syncing.");
					}
				}
			}
			else if (localValue is not null && cloudValue is not null)
			{
				result.Add(this.Select(key, localValue, cloudValue, stamp));
			}
			else
			{
				if (this.baseline.Entities.ContainsKey(key))
				{
					throw new SyncMergeException($"{key} disappeared without a deletion record. Reconcile the book before syncing.");
				}

				// A single merged database cannot safely represent additions excluded by a directional run.
				bool keep = this.direction == SyncDirection.TwoWay
					|| (this.direction == SyncDirection.UpdateCloud ? localValue is not null : cloudValue is not null);
				if (keep)
				{
					result.Add(SyncEntityFingerprint.Clone(localValue ?? cloudValue!));
				}
				else
				{
					throw new SyncMergeException("A directional sync would discard a newly added entity. Use Two-Way to retain both books' additions first.");
				}
			}
		}

		return result;
	}

	private T Select<T>(string key, T local, T cloud, Func<T, RevisionStamp> stamp)
	{
		string left = SyncEntityFingerprint.Create(local), right = SyncEntityFingerprint.Create(cloud);
		this.baseline.Entities.TryGetValue(key, out string? prior);
		bool localChanged = left != prior, cloudChanged = right != prior;
		SyncSide winner = this.direction switch
		{
			SyncDirection.UpdateThisDevice => SyncSide.Cloud,
			SyncDirection.UpdateCloud => SyncSide.Local,
			_ => left == right || !cloudChanged ? SyncSide.Local : !localChanged ? SyncSide.Cloud : this.Choose(stamp(local), stamp(cloud), left, right),
		};
		if (left != right && ((localChanged && cloudChanged) || this.direction != SyncDirection.TwoWay))
		{
			SyncConflictUnit unit = local switch
			{
				Setlist => SyncConflictUnit.WholeOrderedSetlist,
				SongFile => SyncConflictUnit.WholeFile,
				_ => SyncConflictUnit.WholeEntity,
			};
			this.conflicts.Add(new(key, unit, winner, winner == SyncSide.Local ? SyncSide.Cloud : SyncSide.Local));
		}

		if (local is SongFile localFile && cloud is SongFile cloudFile
			&& !StringComparer.OrdinalIgnoreCase.Equals(localFile.ContentHash, cloudFile.ContentHash))
		{
			SongFile loser = winner == SyncSide.Local ? cloudFile : localFile;
			if (!this.baseline.FileHashes.TryGetValue(loser.Id, out string? oldHash) || !StringComparer.OrdinalIgnoreCase.Equals(oldHash, loser.ContentHash))
			{
				this.Preserve(loser, winner == SyncSide.Local ? localFile : cloudFile, winner == SyncSide.Local ? SyncSide.Cloud : SyncSide.Local);
			}
		}

		return SyncEntityFingerprint.Clone(winner == SyncSide.Local ? local : cloud);
	}

	private SyncSide Choose(RevisionStamp local, RevisionStamp cloud, string left, string right)
	{
		bool plausible = local.ModifiedUtc > DateTimeOffset.UnixEpoch && cloud.ModifiedUtc > DateTimeOffset.UnixEpoch
			&& local.ModifiedUtc <= this.now + FutureTolerance && cloud.ModifiedUtc <= this.now + FutureTolerance;
		SyncSide result = plausible && (local.ModifiedUtc - cloud.ModifiedUtc).Duration() > ClockSkewTolerance
			? (local.ModifiedUtc > cloud.ModifiedUtc ? SyncSide.Local : SyncSide.Cloud)
			: (StringComparer.Ordinal.Compare(left, right) >= 0 ? SyncSide.Local : SyncSide.Cloud);
		return result;
	}

	private void Preserve(SongFile loser, SongFile winner, SyncSide side)
	{
		const int GuidByteCount = 16;
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{loser.Id:D}:{winner.ContentHash.ToLowerInvariant()}:{loser.ContentHash.ToLowerInvariant()}"));
		Guid id = new(hash.AsSpan(0, GuidByteCount));
		SongFile recovery = SyncEntityFingerprint.Clone(loser);
		recovery.Id = id;
		recovery.RelativePath = PortableManagedFileName.Create(
			"Recovered " + Path.GetFileNameWithoutExtension(loser.RelativePath), id, Path.GetExtension(loser.RelativePath));
		recovery.IsArchived = true;
		recovery.DisplayPriority = int.MaxValue;
		recovery.RecoveryVersion = new() { OriginalConflictingFileId = loser.Id, WinningFileId = winner.Id, RecoveredFromSyncUtc = this.now };
		recovery.Revision = RevisionStamp.Initial(this.deviceId, this.now);
		this.recoveryFiles.Add(recovery);
		this.recoveries.Add(new(id, loser.Id, side, loser.ContentHash));
	}

	#endregion

	#region Private Types

	private sealed record BookName(string Name, RevisionStamp Revision);

	#endregion
}
