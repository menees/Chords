namespace Menees.Chords.Book.Maui;

public sealed partial class OpenBookSplitButton : View
{
	public static readonly BindableProperty RecentBooksProperty = BindableProperty.Create(
		nameof(RecentBooks), typeof(IReadOnlyList<RecentBook>), typeof(OpenBookSplitButton), Array.Empty<RecentBook>());

	public event EventHandler? Clicked;

	public event EventHandler<RecentBook>? RecentBookSelected;

	public IReadOnlyList<RecentBook> RecentBooks
	{
		get => (IReadOnlyList<RecentBook>)this.GetValue(RecentBooksProperty);
		set => this.SetValue(RecentBooksProperty, value);
	}

	internal void Open() => this.Clicked?.Invoke(this, EventArgs.Empty);

	internal void OpenRecent(RecentBook book) => this.RecentBookSelected?.Invoke(this, book);
}
