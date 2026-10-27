using System.ComponentModel;
using Menees.Chords.Db;

namespace Menees.Chords.Book.Maui;

/// <summary>One selectable import source and its explicit decision.</summary>
public sealed partial class ImportReviewRow : INotifyPropertyChanged
{
	private BookImportDecision decision;

	public ImportReviewRow(BookImportItem item)
	{
		this.Item = item;
		this.decision = item.DefaultDecision;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public BookImportItem Item { get; }

	public string Title => this.Item.Analysis.Title
		+ (this.Item.Analysis.Artists.Count > 0 ? " — " + string.Join(", ", this.Item.Analysis.Artists) : string.Empty);

	public string Description => this.Item.Name + " · " + this.Item.Conflict switch
	{
		BookImportConflict.NewSong => "New song",
		BookImportConflict.ExactDuplicate => "Same title and identical content",
		BookImportConflict.IdenticalContent => "Identical content with another title",
		_ => "Possible version; check artists and content",
	};

	public string DecisionCaption => this.decision.Action switch
	{
		BookImportAction.ImportNewSong => "Import as separate song",
		BookImportAction.AddSheet => "Add sheet to selected song",
		_ => "Skip",
	};

	public BookImportDecision Decision
	{
		get => this.decision;
		set
		{
			this.decision = value;
			this.PropertyChanged?.Invoke(this, new(nameof(this.DecisionCaption)));
		}
	}
}