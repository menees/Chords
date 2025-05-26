namespace Menees.Chords;

[TestClass]
public class ChordProDirectiveNameTests
{
	#region Public Methods

	[TestMethod]
	public void ToStringTest()
	{
		Test("start_of_verse", "start_of_verse", null, false);
		Test("define-tenor", "define", "tenor", false);
		Test("define-!soprano", "define", "soprano", true);

		static void Test(string text, string name, string? selector, bool invertSelection)
		{
			var qualifiedName = Create(text);
			qualifiedName.Name.ShouldBe(name);
			qualifiedName.Selector.ShouldBe(selector);
			qualifiedName.InvertSelection.ShouldBe(invertSelection);
			qualifiedName.ToString().ShouldBe(text);
		}
	}

	[TestMethod]
	public void GetHashCodeTest()
	{
		var name1 = Create("test");
		var name2 = Create("test");
		var name3 = Create("test3");
		name1.GetHashCode().ShouldBe(name2.GetHashCode());
		name1.GetHashCode().ShouldNotBe(name3.GetHashCode());
	}

	[TestMethod]
	public void EqualsTest()
	{
		var name1 = Create("test");
		var name2 = Create("test");
		name1.ShouldBe(name2);

		var name3 = Create("test3");
		name3.ShouldBe(name3);
		var name4 = Create("test3-bass");
		name3.ShouldNotBe(name4);
		var name5 = Create("test3-!bass");
		name3.ShouldNotBe(name5);
	}

	#endregion

	#region Private Methods

	private static ChordProDirectiveName Create(string name)
		=> ChordProDirectiveLineTests.Parse($"{{{name}}}").QualifiedName;

	#endregion
}