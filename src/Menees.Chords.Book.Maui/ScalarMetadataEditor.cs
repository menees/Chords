#region Using Directives

using Menees.Chords.Book.Application;
using Menees.Chords.Db;

#endregion

namespace Menees.Chords.Book.Maui;

/// <summary>Compact scalar catalog controls. Unedited multi-value fields retain their original values.</summary>
public sealed partial class ScalarMetadataEditor : ContentView
{
	#region Private Data

	private const int ColumnGap = 12;
	private const int RowGap = 4;
	private const int ControlGap = 8;
	private const int SelectorWidth = 150;
	private const int ValidationRow = 8;
	private const int EditorColumns = 3;

	private static readonly (string Name, string Label)[] Fields =
	[
		("key", "Key"), ("album", "Album"), ("tempo", "Tempo (bpm)"), ("year", "Year"),
		("capo", "Capo"), ("copyright", "Copyright"), ("time", "Time signature"), ("duration", "Duration (m:ss)"),
	];

	private readonly Grid fieldsGrid = new() { ColumnSpacing = ColumnGap, RowSpacing = RowGap };
	private readonly Dictionary<string, MetadataValuePicker> selectors = new(StringComparer.Ordinal);
	private readonly Label validation = new() { TextColor = Colors.Red, IsVisible = false };

	private readonly Dictionary<string, Microsoft.Maui.Controls.Entry> entries = new(StringComparer.Ordinal);
	private IReadOnlyDictionary<string, IReadOnlyList<string>> original = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	#endregion

	#region Public Members

	public ScalarMetadataEditor()
	{
		const int Columns = 2;
		Grid grid = this.fieldsGrid;
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
			Grid cell = new() { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], RowSpacing = RowGap, ColumnSpacing = ControlGap };
			cell.ColumnDefinitions.Add(new(GridLength.Star));
			cell.Add(new Label { Text = label });
			cell.Add(entry, 0, 1);
			if (name is "album" or "time")
			{
				string caption = "Choose existing " + label.ToLowerInvariant();
				MetadataValuePicker picker = new() { TargetEntry = entry, WidthRequest = SelectorWidth, AutomationId = name + "Choices" };
				SemanticProperties.SetDescription(picker, caption);
				cell.ColumnDefinitions.Add(new(GridLength.Auto));
				cell.Add(new Label { Text = caption }, 1);
				cell.Add(picker, 1, 1);
				picker.SelectionRejected += message =>
				{
					this.validation.Text = message;
					this.validation.IsVisible = true;
				};
				this.selectors.Add(name, picker);
			}

			grid.Add(cell, index % Columns, index / Columns);
		}

		this.Content = new VerticalStackLayout { Children = { grid, this.validation } };
	}

	public bool HasChanges => this.entries.Any(pair => pair.Value.Text != this.OriginalText(pair.Key));

	/// <summary>Places scalar cells in the same rows as the editor's title, artist and tag cells.</summary>
	public void AttachTo(Grid target)
	{
		foreach (View cell in this.fieldsGrid.Children.Cast<View>().ToArray())
		{
			int column = Grid.GetColumn(cell);
			int row = Grid.GetRow(cell);
			Grid field = (Grid)cell;
			Grid labels = new() { ColumnSpacing = field.ColumnSpacing };
			Grid controls = new() { ColumnSpacing = field.ColumnSpacing };
			foreach (ColumnDefinition definition in field.ColumnDefinitions)
			{
				labels.ColumnDefinitions.Add(new(definition.Width));
				controls.ColumnDefinitions.Add(new(definition.Width));
			}

			foreach (View child in field.Children.Cast<View>().ToArray())
			{
				int childColumn = Grid.GetColumn(child);
				bool isLabel = Grid.GetRow(child) == 0;
				field.Remove(child);
				(isLabel ? labels : controls).Add(child, childColumn);
			}

			this.fieldsGrid.Remove(cell);
			target.Add(labels, column + 1, row * 2);
			target.Add(controls, column + 1, (row * 2) + 1);
		}

		((VerticalStackLayout)this.Content).Remove(this.validation);
		target.Add(this.validation, 0, ValidationRow);
		Grid.SetColumnSpan(this.validation, EditorColumns);
	}

	public void Load(SongEditDocument document)
	{
		this.original = document.Metadata;
		foreach ((string name, Microsoft.Maui.Controls.Entry entry) in this.entries)
		{
			entry.Text = this.OriginalText(name);
		}
	}

	public void LoadSuggestions(BookMetadataValueIndex suggestions)
	{
		foreach ((string name, MetadataValuePicker picker) in this.selectors)
		{
			picker.LoadValues(suggestions.GetValues(name));
		}
	}

	public IReadOnlyDictionary<string, IReadOnlyList<string>> GetValues()
	{
		Dictionary<string, IReadOnlyList<string>> result = new(this.original, StringComparer.Ordinal);
		foreach ((string name, Microsoft.Maui.Controls.Entry entry) in this.entries)
		{
			if (entry.Text != this.OriginalText(name))
			{
				result[name] = CatalogValueList.Parse(entry.Text);
			}
		}

		SongMetadataValidation.Validate(result, this.original);
		return result;
	}

	#endregion

	#region Private Methods

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

	private string OriginalText(string name)
		=> this.original.TryGetValue(name, out IReadOnlyList<string>? values) ? CatalogValueList.Format(values) : string.Empty;

	#endregion
}
