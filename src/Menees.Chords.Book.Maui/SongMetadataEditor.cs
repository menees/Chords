#region Using Directives

using System.Globalization;
using Menees.Chords.Book.Application;
using Menees.Chords.Book.Maui.Platforms.Windows;
using Menees.Chords.Db;
using MauiEntry = Microsoft.Maui.Controls.Entry;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Responsive catalog editing with short scalar fields and detached value-selection dialogs.</summary>
public sealed partial class SongMetadataEditor : ContentView
{
	#region Private Data

	private const int Gap = 8;
	private const int LabelGap = 2;
	private const int EditGap = 2;
	private const int SingleRowWidth = 1600;
	private const int TwoRowWidth = 900;
	private const int ThreeRowWidth = 560;
	private const int ThreeRowMode = 3;
	private const int NarrowMode = 5;
	private static readonly (string Name, string Label)[] Fields =
	[
		("title", "Title"), ("artist", "Artists"), ("key", "Key"), ("tempo", "Tempo (bpm)"), ("capo", "Capo"),
		("tag", "Tags"), ("album", "Album"), ("time", "Time signature"), ("year", "Year"), ("duration", "Duration"),
	];

	private readonly Grid rows = new() { RowSpacing = Gap };
	private readonly Dictionary<string, Grid> cells = new(StringComparer.Ordinal);
	private readonly Dictionary<string, MauiEntry> entries = new(StringComparer.Ordinal);
	private readonly Label validation = new() { TextColor = Colors.Red, IsVisible = false };
	private SongEditDocument? original;
	private BookMetadataValueIndex? suggestions;
	private int layoutMode;
	private bool selecting;

	#endregion

	#region Public Members

	public SongMetadataEditor()
	{
		foreach ((string name, string label) in Fields)
		{
			MauiEntry entry = new() { AutomationId = "Metadata." + name, Placeholder = Placeholder(name) };
			ConfigurePlaceholder(entry);
			SemanticProperties.SetDescription(entry, label);
			entry.Unfocused += (_, _) => this.ValidateFields();
			this.entries.Add(name, entry);
			Grid cell = new() { RowSpacing = LabelGap, ColumnSpacing = Gap, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)] };
			cell.ColumnDefinitions.Add(new(GridLength.Star));
			Label caption = new() { Text = label, LineBreakMode = LineBreakMode.NoWrap };
			cell.Add(caption);
			cell.Add(entry, 0, 1);
			if (name is "artist" or "tag" or "album" or "time")
			{
				FluentIconButton edit = new() { Icon = "EditLineHorizontal3", Description = "Edit " + label, AutomationId = "Metadata.Edit." + name };
				edit.Clicked += async (_, _) => await this.EditValuesAsync(name, label).ConfigureAwait(true);
				cell.ColumnSpacing = EditGap;
				cell.ColumnDefinitions.Add(new(GridLength.Auto));
				cell.Add(edit, 1, 1);
				Grid.SetColumnSpan(caption, 2);
			}

			this.cells.Add(name, cell);
		}

		this.Content = new VerticalStackLayout { Spacing = LabelGap, Children = { this.rows, this.validation } };
		this.SizeChanged += (_, _) => this.ArrangeFields();
		this.ArrangeFields();
	}

	public bool IsSelecting => this.selecting;

	public MauiEntry TitleEntry => this.entries["title"];

	public bool HasChanges => this.original is not null && this.entries.Any(pair => pair.Value.Text != this.OriginalText(pair.Key));

	public void Load(SongEditDocument document)
	{
		this.original = document;
		foreach ((string name, MauiEntry entry) in this.entries)
		{
			entry.Text = this.OriginalText(name);
		}
	}

	public void FillBlankTitleAndArtists(string? title, IReadOnlyList<string> artists)
	{
		if (title is not null && string.IsNullOrWhiteSpace(this.TitleEntry.Text))
		{
			this.TitleEntry.Text = title;
		}

		if (string.IsNullOrWhiteSpace(this.entries["artist"].Text))
		{
			this.entries["artist"].Text = CatalogValueList.Format(artists);
		}
	}

	public void LoadSuggestions(BookMetadataValueIndex values) => this.suggestions = values;

	public SongEditMetadata GetMetadata()
	{
		IReadOnlyDictionary<string, IReadOnlyList<string>> prior = this.original?.Metadata ?? new Dictionary<string, IReadOnlyList<string>>();
		Dictionary<string, IReadOnlyList<string>> scalars = new(prior, StringComparer.Ordinal);
		foreach ((string name, MauiEntry entry) in this.entries.Where(pair => pair.Key is not ("title" or "artist" or "tag")))
		{
			if (entry.Text != this.OriginalText(name))
			{
				scalars[name] = CatalogValueList.Parse(entry.Text);
			}
		}

		SongMetadataValidation.Validate(scalars, this.original?.Metadata);
		return new(this.TitleEntry.Text ?? string.Empty, CatalogValueList.Parse(this.entries["artist"].Text), CatalogValueList.Parse(this.entries["tag"].Text))
		{
			Scalars = scalars,
		};
	}

	#endregion

	#region Private Methods

	private static void ConfigurePlaceholder(MauiEntry entry)
	{
		if (entry.Placeholder is not null)
		{
			entry.SetDynamicResource(MauiEntry.PlaceholderColorProperty, "AppPlaceholder");
			WindowsEntryPlaceholder.Configure(entry);
		}
	}

	private static string? Placeholder(string name) => name switch
	{
		"key" => "Dm",
		"tempo" => "###",
		"capo" => "#",
		"time" => "n/m",
		"year" => "yyyy",
		"duration" => "m:ss",
		"tag" => "Practice; Live",
		_ => null,
	};

	private static GridLength ParseWidth(string width)
	{
		bool star = width.EndsWith('*');
		string number = star ? width[..^1] : width;
		double value = number.Length == 0 ? 1 : double.Parse(number, CultureInfo.InvariantCulture);
		return new(value, star ? GridUnitType.Star : GridUnitType.Absolute);
	}

	private string OriginalText(string name) => this.original is null ? string.Empty : name switch
	{
		"title" => this.original.Title,
		"artist" => CatalogValueList.Format(this.original.Artists),
		"tag" => CatalogValueList.Format(this.original.Tags),
		_ => this.original.Metadata.TryGetValue(name, out IReadOnlyList<string>? values) ? CatalogValueList.Format(values) : string.Empty,
	};

	private void ValidateFields()
	{
		try
		{
			_ = this.GetMetadata();
			this.validation.IsVisible = false;
		}
		catch (ArgumentException exception)
		{
			this.validation.Text = exception.Message;
			this.validation.IsVisible = true;
		}
	}

	private async Task EditValuesAsync(string name, string label)
	{
		if (!this.selecting)
		{
			this.selecting = true;
			try
			{
				MauiEntry entry = this.entries[name];
				IReadOnlyList<string> current = CatalogValueList.Parse(entry.Text);
				CatalogValueSelection draft = new(this.suggestions?.GetValues(name) ?? [], current, multiple: name is "artist" or "tag");
				IReadOnlyList<string>? selected = await WindowsCatalogValueDialog.ShowAsync(this, "Edit " + label, draft).ConfigureAwait(true);
				if (selected is not null && !selected.SequenceEqual(current, StringComparer.Ordinal))
				{
					entry.Text = CatalogValueList.Format(selected);
				}
			}
			catch (ArgumentException exception)
			{
				this.validation.Text = exception.Message;
				this.validation.IsVisible = true;
			}
			finally
			{
				this.selecting = false;
			}
		}
	}

	private void ArrangeFields()
	{
		int mode = this.Width >= SingleRowWidth ? 1 : this.Width >= TwoRowWidth ? 2 : this.Width >= ThreeRowWidth ? ThreeRowMode : NarrowMode;
		if (mode != this.layoutMode)
		{
			this.layoutMode = mode;
			foreach (Grid row in this.rows.Children.Cast<Grid>())
			{
				row.Children.Clear();
			}

			this.rows.Clear();
			this.rows.RowDefinitions.Clear();
			if (mode == 1)
			{
				this.AddRow(
					["title", "artist", "key", "tempo", "capo", "tag", "album", "time", "year", "duration"],
					["*", "*", "76", "104", "72", "*", "*", "136", "80", "104"]);
			}
			else if (mode == 2)
			{
				this.AddRow(["title", "artist", "key", "tempo", "capo"], ["*", "*", "136", "104", "104"]);
				this.AddRow(["tag", "album", "time", "year", "duration"], ["*", "*", "136", "104", "104"]);
			}
			else if (mode == ThreeRowMode)
			{
				this.AddRow(["title", "artist"], ["*", "*"]);
				this.AddRow(["key", "tempo", "capo", "time", "year"], ["*", "*", "*", "1.5*", "*"]);
				this.AddRow(["tag", "album", "duration"], ["*", "*", "104"]);
			}
			else
			{
				this.AddRow(["title"], ["*"]);
				this.AddRow(["artist"], ["*"]);
				this.AddRow(["key", "tempo", "capo"], ["*", "*", "*"]);
				this.AddRow(["tag", "album"], ["*", "*"]);
				this.AddRow(["time", "year", "duration"], ["1.4*", "*", "*"]);
			}
		}
	}

	private void AddRow(string[] names, string[] widths)
	{
		Grid row = new() { ColumnSpacing = Gap };
		for (int index = 0; index < names.Length; index++)
		{
			row.ColumnDefinitions.Add(new(ParseWidth(widths[index])));
			row.Add(this.cells[names[index]], index);
		}

		int next = this.rows.RowDefinitions.Count;
		this.rows.RowDefinitions.Add(new(GridLength.Auto));
		this.rows.Add(row, 0, next);
	}
	#endregion

}
