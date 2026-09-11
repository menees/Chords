using System.Globalization;

namespace Menees.Chords.Book.Maui;

public sealed record SetlistRow(
	Guid Id,
	string Name,
	DateOnly? Date,
	string? Notes,
	int EntryCount,
	int KnownDurationCount,
	int TotalDurationSeconds,
	bool IsArchived)
{
	public string DisplayText
	{
		get => $"{this.Name} · {this.MetadataText}";
	}

	public string MetadataText
	{
		get
		{
			List<string> metadata = [$"{this.EntryCount:N0} song{(this.EntryCount == 1 ? string.Empty : "s")}"];
			if (this.IsArchived)
			{
				metadata.Add("Archived");
			}

			if (this.KnownDurationCount > 0)
			{
				TimeSpan duration = TimeSpan.FromSeconds(this.TotalDurationSeconds);
				string value = duration.TotalHours >= 1
					? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
					: duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
				metadata.Add(this.KnownDurationCount == this.EntryCount
					? $"duration {value}"
					: $"duration {value} ({this.KnownDurationCount:N0}/{this.EntryCount:N0} timed)");
			}

			if (this.Date is DateOnly date)
			{
				metadata.Add(date.ToString("d", CultureInfo.CurrentCulture));
			}

			return string.Join(" · ", metadata);
		}
	}

	public string SearchText => $"{this.Name} {this.Notes} {this.Date:yyyy-MM-dd}";

	public Color RowTextColor => this.IsArchived ? Colors.Gray : Color.FromArgb("#25232A");

	public FontAttributes RowFontAttributes => this.IsArchived ? FontAttributes.Italic : FontAttributes.None;
}
