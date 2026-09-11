namespace Menees.Chords.Book.Maui;

public sealed partial class BookTabs : View
{
	public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
		nameof(SelectedIndex),
		typeof(int),
		typeof(BookTabs),
		0,
		propertyChanged: (bindable, oldValue, newValue) => ((BookTabs)bindable).SelectionChanged?.Invoke(bindable, EventArgs.Empty));

	public event EventHandler? SelectionChanged;

	public int SelectedIndex
	{
		get => (int)this.GetValue(SelectedIndexProperty);
		set => this.SetValue(SelectedIndexProperty, value);
	}
}
