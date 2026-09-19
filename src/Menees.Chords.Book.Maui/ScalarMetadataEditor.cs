using Menees.Chords.Book.Application;

namespace Menees.Chords.Book.Maui;

/// <summary>Compact scalar catalog controls. Unedited multi-value fields retain their original values.</summary>
public sealed partial class ScalarMetadataEditor : ContentView
{
	private static readonly (string Name, string Label)[] Fields =
	[
		("key", "Key"), ("album", "Album"), ("tempo", "Tempo (bpm)"), ("year", "Year"),
		("capo", "Capo"), ("copyright", "Copyright"), ("time", "Time signature"), ("duration", "Duration (m:ss)"),
	];

	private readonly Label validation = new() { TextColor = Colors.Red, IsVisible = false };

	private readonly Dictionary<string, Microsoft.Maui.Controls.Entry> entries = new(StringComparer.Ordinal);
	private IReadOnlyDictionary<string, IReadOnlyList<string>> original = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	public ScalarMetadataEditor()
	{
		const int Columns = 2;
		const int ColumnGap = 12;
		const int RowGap = 4;
		Grid grid = new() { ColumnSpacing = ColumnGap, RowSpacing = RowGap };
		grid.ColumnDefinitions.Add(new(GridLength.Star));
		grid.ColumnDefinitions.Add(new(GridLength.Star));
		for (int index = 0; index < Fields.Length; index++)
		{
			if (index % Columns == 0)
			{
				grid.RowDefinitions.Add(new(GridLength.Auto));
			}

			var (name, label) = Fields[index];
			Microsoft.Maui.Controls.Entry entry = new();
			SemanticProperties.SetDescription(entry, label);
			this.entries.Add(name, entry);
			entry.Unfocused += (_, _) => this.ValidateFields();
			VerticalStackLayout cell = new() { Spacing = RowGap, Children = { new Label { Text = label }, entry } };
			grid.Add(cell, index % Columns, index / Columns);
		}

		this.Content = new VerticalStackLayout { Children = { grid, this.validation } };
	}

	public bool HasChanges => this.entries.Any(pair => pair.Value.Text != this.OriginalText(pair.Key));

	public void Load(SongEditDocument document)
	{
		this.original = document.Metadata;
		foreach ((string name, Microsoft.Maui.Controls.Entry entry) in this.entries)
		{
			entry.Text = this.OriginalText(name);
		}
	}

	public IReadOnlyDictionary<string, IReadOnlyList<string>> GetValues()
	{
		Dictionary<string, IReadOnlyList<string>> result = new(this.original, StringComparer.Ordinal);
		foreach ((string name, Microsoft.Maui.Controls.Entry entry) in this.entries)
		{
			if (entry.Text != this.OriginalText(name))
			{
				result[name] = (entry.Text ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			}
		}

		SongMetadataValidation.Validate(result, this.original);
		return result;
	}

	private void ValidateFields()
	{
		try
		{
			_ = this.GetValues();
			this.validation.IsVisible = false;
		}
		catch (ArgumentException exception)
		{
			this.validation.Text = exception.Message;
			this.validation.IsVisible = true;
		}
	}

	private string OriginalText(string name) => this.original.TryGetValue(name, out IReadOnlyList<string>? values) ? string.Join("; ", values) : string.Empty;
}
