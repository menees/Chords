namespace Menees.Chords.Book.Maui;

public sealed partial class OpenBookSplitButton : FluentIconSplitButton
{
	public static readonly BindableProperty RecentBooksProperty = BindableProperty.Create(
		nameof(RecentBooks),
		typeof(IReadOnlyList<RecentBook>),
		typeof(OpenBookSplitButton),
		Array.Empty<RecentBook>(),
		propertyChanged: (view, _, _) => ((OpenBookSplitButton)view).UpdateMenu());

	public OpenBookSplitButton()
	{
		this.Icon = "Book";
		this.Description = "Open Book";
		this.UpdateMenu();
	}

	public event EventHandler<RecentBook>? RecentBookSelected;

	public IReadOnlyList<RecentBook> RecentBooks
	{
		get => (IReadOnlyList<RecentBook>)this.GetValue(RecentBooksProperty);
		set => this.SetValue(RecentBooksProperty, value);
	}

	private void UpdateMenu()
	{
		this.MenuItems = this.RecentBooks.Count == 0
			? [new("No recent books", () => { }, IsEnabled: false)]
			: [.. this.RecentBooks.Select(book => new FluentMenuItem(book.Name, () => this.RecentBookSelected?.Invoke(this, book), book.Path))];
	}
}
