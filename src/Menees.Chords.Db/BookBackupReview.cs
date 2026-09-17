#region Using Directives

using System.IO;
using System.IO.Compression;

#endregion

namespace Menees.Chords.Db;

/// <summary>A validated archive kept open and read-locked while the user reviews recovery.</summary>
public sealed class BookBackupReview : IDisposable
{
	#region Private Data

	private readonly Stream input;
	private readonly ZipArchive archive;
	private readonly Dictionary<string, ZipArchiveEntry> entries;
	private readonly ChordDatabase database;
	private bool disposed;

	#endregion

	#region Constructors

	internal BookBackupReview(Stream input, ZipArchive archive, Dictionary<string, ZipArchiveEntry> entries, ChordDatabase database)
	{
		this.input = input;
		this.archive = archive;
		this.entries = entries;
		this.database = database;
	}

	#endregion

	#region Public Properties

	/// <summary>Gets the original book identity.</summary>
	public Guid BookId => this.database.Id;

	/// <summary>Gets the archived book name.</summary>
	public string Name => this.database.Name;

	/// <summary>Gets the number of songs.</summary>
	public int SongCount => this.database.Songs.Count;

	/// <summary>Gets the number of sheets, including archived/recovery sheets.</summary>
	public int SheetCount => this.database.SongFiles.Count;

	/// <summary>Gets the number of setlists.</summary>
	public int SetlistCount => this.database.Setlists.Count;

	#endregion

	#region Public Methods

	/// <inheritdoc />
	public void Dispose()
	{
		this.disposed = true;
		this.archive.Dispose();
		this.input.Dispose();
	}

	#endregion

	#region Internal Methods

	internal ChordDatabase GetDatabase()
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return this.database;
	}

	internal Stream OpenAsset(string path)
	{
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return this.entries[path].Open();
	}

	#endregion
}