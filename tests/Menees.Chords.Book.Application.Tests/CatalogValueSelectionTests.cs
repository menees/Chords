#region Using Directives

using System.Diagnostics;

#endregion

namespace Menees.Chords.Book.Application.Tests;

[TestClass]
public sealed class CatalogValueSelectionTests
{
	#region Public Members

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void CurrentValuesArePreselectedAndCancelDoesNotChangeOriginalValues()
	{
		string[] original = ["Cafe\u0301", "AC; DC", "Not in catalog"];
		CatalogValueSelection draft = new(["CAF\u00C9", "Other"], original, multiple: true);
		draft.Values.ShouldBe(original);
		draft.Filter(string.Empty).Count.ShouldBe(4);
		draft.Filter(string.Empty).Take(original.Length).ShouldAllBe(value => draft.IsSelected(value));
		draft.IsSelected("CAF\u00C9").ShouldBeTrue();
		draft.SetSelected("Other", true);
		draft.SetSelected("AC; DC", false);
		original.ShouldBe(["Cafe\u0301", "AC; DC", "Not in catalog"]);
		CatalogValueList.Parse(CatalogValueList.Format(draft.Values)).ShouldBe(draft.Values);
	}

	[TestMethod]
	public void FilteringKeepsHiddenChecksAndAddingUsesLiteralValuesWithoutDuplicates()
	{
		CatalogValueSelection draft = new(["Caf\u00E9", "Other"], ["Caf\u00E9"], multiple: true);
		draft.Filter("cafe").ShouldBe(["Caf\u00E9"]);
		draft.Filter("missing").ShouldBeEmpty();
		draft.IsSelected("Caf\u00E9").ShouldBeTrue();
		draft.Add("  New; Value  ").ShouldBe("New; Value");
		draft.Add("CAF\u00C9").ShouldBe("Caf\u00E9");
		draft.Values.ShouldBe(["Caf\u00E9", "New; Value"]);
		draft.Filter("NEW").ShouldBe(["New; Value"]);
		draft.SetSelected("Caf\u00E9", false);
		draft.Values.ShouldBe(["New; Value"]);
		Should.Throw<ArgumentException>(() => draft.Add(" "));
		Should.Throw<ArgumentException>(() => draft.SetSelected("Missing", true));
	}

	[TestMethod]
	public void SingleSelectPreservesLegacyMultipleValuesUntilAnExplicitReplacement()
	{
		CatalogValueSelection draft = new(["New"], ["First", "Second"], multiple: false);
		draft.Values.ShouldBe(["First", "Second"]);
		draft.SetSelected("New", true);
		draft.Values.ShouldBe(["New"]);
		draft.Add("Final; Album");
		draft.Values.ShouldBe(["Final; Album"]);
		draft.SetSelected("Final; Album", false);
		draft.Values.ShouldBeEmpty();
	}

	[TestMethod]
	public void TenThousandChoicesFilterWithoutChangingOrLosingSelection()
	{
		string[] values = [.. Enumerable.Range(0, 10000).Select(index => $"Artist {index:D5}")];
		Stopwatch elapsed = Stopwatch.StartNew();
		CatalogValueSelection draft = new(values, [values[0], values[^1]], multiple: true);
		for (int index = 0; index < 100; index++)
		{
			draft.Filter("Artist 09999").ShouldBe([values[^1]]);
		}

		draft.Values.ShouldBe([values[0], values[^1]]);
		this.TestContext.WriteLine($"10,000 choices and 100 filtered searches: {elapsed.ElapsedMilliseconds} ms.");
	}
	#endregion

}
