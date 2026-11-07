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

	public ImportReviewRow(BookImportFailure failure)
	{
		this.Failure = failure;
		this.decision = new(Guid.Empty, BookImportAction.Skip);
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public BookImportItem? Item { get; }

	public BookImportFailure? Failure { get; }

	public Guid? SelectedCandidateSheetId { get; set; }

	public string Title => this.Item is BookImportItem item
		? item.Analysis.Title + (item.Analysis.Artists.Count > 0 ? " — " + string.Join(", ", item.Analysis.Artists) : string.Empty)
		: this.Failure!.Name;

	public string Description => this.Item is BookImportItem item ? item.Name + " · " + item.Conflict switch
	{
		BookImportConflict.NewSong => "New song",
		BookImportConflict.ExactDuplicate => "Same title and identical content",
		BookImportConflict.IdenticalContent => "Identical content with another title",
		_ => "Possible version; check artists and content",
	} : this.Failure!.SourcePath + Environment.NewLine + this.Failure.Message;

	public string DecisionCaption => this.Failure is not null ? "Could not process; excluded from import" : this.decision.Action switch
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