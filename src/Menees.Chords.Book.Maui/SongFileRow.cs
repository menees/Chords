using Menees.Chords.Book.Application;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui;

public sealed class SongFileRow
{
	private readonly SongFileCatalogItem file;

	public SongFileRow(SongFileCatalogItem file) => this.file = file;

	public Guid Id => this.file.Id;

	public bool IsArchived => this.file.IsArchived;

	public bool CanEdit => !this.file.IsArchived && !this.file.IsRecoveryVersion
		&& this.file.MediaKind == MediaKind.Text && this.file.SourceFormat != SourceFormat.OpenSongXml;

	public string Name => this.file.Name.Replace($" [{this.file.Id:D}]", string.Empty, StringComparison.OrdinalIgnoreCase);

	public string Details => string.Join(" · ", new[]
	{
		this.file.MediaKind == MediaKind.Pdf ? "PDF" : this.file.SourceFormat switch
		{
			SourceFormat.ChordPro => "ChordPro",
			SourceFormat.ChordOverText => "Chords over text",
			SourceFormat.Mixed => "Mixed text",
			SourceFormat.OpenSongXml => "OpenSong (source read-only)",
			_ => "Text",
		},
		this.file.IsDefault ? "Default sheet" : string.Empty,
		this.file.IsArchived ? "Archived" : string.Empty,
		this.file.IsRecoveryVersion ? "Recovery version" : string.Empty,
	}.Where(value => value.Length > 0));
}
