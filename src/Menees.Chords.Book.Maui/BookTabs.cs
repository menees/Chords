namespace Menees.Chords.Book.Maui;

public sealed partial class BookTabs : View
{
	public static readonly BindableProperty TitlesProperty = BindableProperty.Create(
		nameof(Titles), typeof(IReadOnlyList<string>), typeof(BookTabs), new[] { "Songs", "Setlists", "Recent", "Artists" });

	public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
		nameof(SelectedIndex),
		typeof(int),
		typeof(BookTabs),
		0,
		propertyChanged: (bindable, oldValue, newValue) => ((BookTabs)bindable).SelectionChanged?.Invoke(bindable, EventArgs.Empty));

	public event EventHandler? SelectionChanged;

	public IReadOnlyList<string> Titles
	{
		get => (IReadOnlyList<string>)this.GetValue(TitlesProperty);
		set => this.SetValue(TitlesProperty, value);
	}

	public int SelectedIndex
	{
		get => (int)this.GetValue(SelectedIndexProperty);
		set => this.SetValue(SelectedIndexProperty, value);
	}
}
