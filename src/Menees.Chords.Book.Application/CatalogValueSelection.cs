#region Using Directives

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

#endregion

namespace Menees.Chords.Book.Application;

/// <summary>A detached, searchable selection draft shared by catalog field dialogs.</summary>
public sealed class CatalogValueSelection
{
	#region Private Data

	private readonly Dictionary<string, string> choices = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> selected = [];
	private readonly HashSet<string> selectedKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly ReadOnlyCollection<string> selection;
	private IReadOnlyList<string> ordered = [];

	#endregion

	#region Public Members

	public CatalogValueSelection(IEnumerable<string> available, IEnumerable<string> current, bool multiple)
	{
		ArgumentNullException.ThrowIfNull(available);
		ArgumentNullException.ThrowIfNull(current);
		this.Multiple = multiple;
		this.selection = this.selected.AsReadOnly();
		foreach (string value in current)
		{
			string key = Key(value);
			if (key.Length > 0 && this.choices.TryAdd(key, value))
			{
				this.selected.Add(value);
				this.selectedKeys.Add(key);
			}
		}

		foreach (string value in available)
		{
			string key = Key(value);
			if (key.Length > 0)
			{
				this.choices.TryAdd(key, value);
			}
		}

		this.SortChoices();
	}

	public bool Multiple { get; }

	public IReadOnlyList<string> Values => this.selection;

	public bool IsSelected(string value) => this.selectedKeys.Contains(Key(value));

	public IReadOnlyList<string> Filter(string? query)
	{
		query = Key(query ?? string.Empty);
		return query.Length == 0 ? this.ordered : Array.AsReadOnly(this.ordered.Where(value =>
			CultureInfo.InvariantCulture.CompareInfo.IndexOf(value, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToArray());
	}

	public void SetSelected(string value, bool selected)
	{
		if (!this.choices.TryGetValue(Key(value), out string? choice))
		{
			throw new ArgumentException("Choose an available value or add it first.", nameof(value));
		}

		if (selected && !this.IsSelected(choice))
		{
			if (!this.Multiple)
			{
				this.selected.Clear();
				this.selectedKeys.Clear();
			}

			this.selected.Add(choice);
			this.selectedKeys.Add(Key(choice));
		}
		else if (!selected)
		{
			this.selectedKeys.Remove(Key(choice));
			this.selected.RemoveAll(item => StringComparer.OrdinalIgnoreCase.Equals(Key(item), Key(choice)));
		}
	}

	public string Add(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		string key = Key(value);
		if (!this.choices.TryGetValue(key, out string? choice))
		{
			choice = key;
			this.choices.Add(key, choice);
		}

		this.SetSelected(choice, true);
		this.SortChoices();
		return choice;
	}

	#endregion

	#region Private Methods

	private static string Key(string value) => value.Trim().Normalize(NormalizationForm.FormC);

	private void SortChoices()
	{
		string[] sorted = [.. this.choices.Values.OrderByDescending(this.IsSelected).ThenBy(value => value, StringComparer.OrdinalIgnoreCase)];
		this.ordered = Array.AsReadOnly(sorted);
	}

	#endregion

}
