#region Using Directives

using System.Text;

#endregion

namespace Menees.Chords.Db;

/// <summary>Validates schema-v1 identities, relationships, and managed paths.</summary>
public static class DatabaseValidation
{
	#region Public Methods

	/// <summary>Returns every detected validation problem without modifying the database.</summary>
	public static IReadOnlyList<ValidationProblem> Validate(ChordDatabase database)
	{
		List<ValidationProblem> problems = [];
		if (database.BookSettings is null || database.Songs is null || database.SongFiles is null
			|| database.InstrumentProfiles is null || database.SongInstrumentSettings is null
			|| database.Setlists is null || database.CustomTabs is null || database.Tombstones is null)
		{
			problems.Add(new("$", "Required schema-v1 objects and collections cannot be null."));
		}
		else
		{
			if (database.SchemaVersion != ChordDatabase.CurrentSchemaVersion)
			{
				problems.Add(new("schemaVersion", $"Expected {ChordDatabase.CurrentSchemaVersion}."));
			}

			RequireId(database.Id, "id", problems);
			if (string.IsNullOrWhiteSpace(database.Name))
			{
				problems.Add(new("name", "A book name is required."));
			}

			ValidateUniqueIds(database.Songs.Select(item => item.Id), "songs", problems);
			ValidateUniqueIds(database.SongFiles.Select(item => item.Id), "songFiles", problems);
			ValidateUniqueIds(database.InstrumentProfiles.Select(item => item.Id), "instrumentProfiles", problems);
			ValidateUniqueIds(database.SongInstrumentSettings.Select(item => item.Id), "songInstrumentSettings", problems);
			ValidateUniqueIds(database.Setlists.Select(item => item.Id), "setlists", problems);
			ValidateUniqueIds(database.CustomTabs.Select(item => item.Id), "customTabs", problems);

			HashSet<Guid> songIds = [.. database.Songs.Select(item => item.Id)];
			HashSet<Guid> fileIds = [.. database.SongFiles.Select(item => item.Id)];
			HashSet<Guid> profileIds = [.. database.InstrumentProfiles.Select(item => item.Id)];
			HashSet<string> paths = new(PortableManagedFileName.Comparer);
			for (int index = 0; index < database.SongFiles.Count; index++)
			{
				SongFile file = database.SongFiles[index];
				string path = $"songFiles[{index}]";
				RequireId(file.Id, path + ".id", problems);
				if (!songIds.Contains(file.SongId))
				{
					problems.Add(new(path + ".songId", "The referenced song does not exist."));
				}

				foreach (string error in PortableManagedFileName.Validate(file.RelativePath))
				{
					problems.Add(new(path + ".relativePath", error));
				}

				if (!paths.Add(file.RelativePath.Normalize(NormalizationForm.FormC)))
				{
					problems.Add(new(path + ".relativePath", "The managed filename is not portably unique."));
				}
			}

			for (int index = 0; index < database.SongInstrumentSettings.Count; index++)
			{
				SongInstrumentSetting setting = database.SongInstrumentSettings[index];
				string path = $"songInstrumentSettings[{index}]";
				if (!songIds.Contains(setting.SongId))
				{
					problems.Add(new(path + ".songId", "The referenced song does not exist."));
				}

				if (!profileIds.Contains(setting.InstrumentProfileId))
				{
					problems.Add(new(path + ".instrumentProfileId", "The referenced instrument profile does not exist."));
				}

				ValidateOptionalFile(setting.PreferredSongFileId, setting.SongId, fileIds, database.SongFiles, path, problems);
			}

			foreach ((Setlist setlist, int setlistIndex) in database.Setlists.Select((value, index) => (value, index)))
			{
				if (setlist.Entries is null)
				{
					problems.Add(new($"setlists[{setlistIndex}].entries", "The ordered entries collection cannot be null."));
					continue;
				}

				ValidateUniqueIds(setlist.Entries.Select(item => item.Id), $"setlists[{setlistIndex}].entries", problems);
				foreach ((SetlistEntry entry, int entryIndex) in setlist.Entries.Select((value, index) => (value, index)))
				{
					string path = $"setlists[{setlistIndex}].entries[{entryIndex}]";
					if (!songIds.Contains(entry.SongId))
					{
						problems.Add(new(path + ".songId", "The referenced song does not exist."));
					}

					if (entry.InstrumentProfileId is Guid profileId && !profileIds.Contains(profileId))
					{
						problems.Add(new(path + ".instrumentProfileId", "The referenced instrument profile does not exist."));
					}

					ValidateOptionalFile(entry.PreferredSongFileId, entry.SongId, fileIds, database.SongFiles, path, problems);
				}
			}
		}

		return problems;
	}

	/// <summary>Throws when the database contains any validation problem.</summary>
	public static void ThrowIfInvalid(ChordDatabase database)
	{
		IReadOnlyList<ValidationProblem> problems = Validate(database);
		if (problems.Count != 0)
		{
			throw new DatabaseValidationException(problems);
		}
	}

	#endregion

	#region Private Methods

	private static void RequireId(Guid id, string path, List<ValidationProblem> problems)
	{
		if (id == Guid.Empty)
		{
			problems.Add(new(path, "A non-empty ID is required."));
		}
	}

	private static void ValidateUniqueIds(IEnumerable<Guid> ids, string path, List<ValidationProblem> problems)
	{
		HashSet<Guid> found = [];
		foreach (Guid id in ids)
		{
			RequireId(id, path, problems);
			if (!found.Add(id))
			{
				problems.Add(new(path, $"ID {id:D} is duplicated."));
			}
		}
	}

	private static void ValidateOptionalFile(
		Guid? preferredFileId,
		Guid songId,
		HashSet<Guid> fileIds,
		IReadOnlyList<SongFile> files,
		string path,
		List<ValidationProblem> problems)
	{
		if (preferredFileId is Guid fileId)
		{
			if (!fileIds.Contains(fileId))
			{
				problems.Add(new(path + ".preferredSongFileId", "The referenced song file does not exist."));
			}
			else if (files.Single(file => file.Id == fileId).SongId != songId)
			{
				problems.Add(new(path + ".preferredSongFileId", "The referenced file belongs to a different song."));
			}
		}
	}

	#endregion
}
