using System.Text;

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class DatabaseValidationTests
{
	[TestMethod]
	public void MissingReferencesAndTraversalAreReported()
	{
		ChordDatabase database = TestData.CreateDatabase();
		database.SongFiles[0].SongId = Guid.NewGuid();
		database.SongFiles[0].RelativePath = "..\\escape.cho";

		IReadOnlyList<ValidationProblem> problems = DatabaseValidation.Validate(database);

		problems.ShouldContain(problem => problem.Path == "songFiles[0].songId");
		problems.ShouldContain(problem => problem.Path == "songFiles[0].relativePath");
	}

	[TestMethod]
	public void PortableCaseInsensitiveFilenameCollisionsAreRejected()
	{
		ChordDatabase database = TestData.CreateDatabase();
		SongFile original = database.SongFiles[0];
		database.SongFiles.Add(new SongFile
		{
			Id = Guid.CreateVersion7(TestData.Now.AddMinutes(1)),
			SongId = original.SongId,
			RelativePath = original.RelativePath.ToUpperInvariant(),
			MediaKind = MediaKind.Text,
			SourceFormat = SourceFormat.OpenSongXml,
		});

		IReadOnlyList<ValidationProblem> problems = DatabaseValidation.Validate(database);

		problems.ShouldContain(problem => problem.Message.Contains("portably unique", StringComparison.Ordinal));
	}

	[TestMethod]
	public void DecomposedUnicodeFilenameIsRejected()
	{
		string decomposed = "Cafe\u0301 [018f0000-0000-7000-8000-000000000000].cho";

		PortableManagedFileName.Validate(decomposed).ShouldContain(problem => problem.Contains("normalization form C", StringComparison.Ordinal));
		decomposed.IsNormalized(NormalizationForm.FormC).ShouldBeFalse();
	}
}
