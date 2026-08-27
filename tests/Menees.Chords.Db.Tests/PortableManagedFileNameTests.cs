using System.IO;
using System.Text;

namespace Menees.Chords.Db.Tests;

[TestClass]
public sealed class PortableManagedFileNameTests
{
	[TestMethod]
	public void ExtensionlessOpenSongNameEndsAtGuidSuffix()
	{
		Guid id = Guid.Parse("018f0000-0000-7000-8000-000000000001");

		string filename = PortableManagedFileName.Create("Blessed Assurance", id, extension: null);

		filename.ShouldBe("Blessed Assurance [018f0000-0000-7000-8000-000000000001]");
		Path.GetExtension(filename).ShouldBeEmpty();
		PortableManagedFileName.TryGetSongFileId(filename, out Guid parsed).ShouldBeTrue();
		parsed.ShouldBe(id);
	}

	[TestMethod]
	public void ExtensionAndGuidSurviveUnicodeTruncation()
	{
		Guid id = Guid.Parse("018f0000-0000-7000-8000-000000000002");
		string longDescription = string.Concat(Enumerable.Repeat("🎸é", 200));

		string filename = PortableManagedFileName.Create(longDescription, id, ".chopro");

		filename.ShouldEndWith(" [018f0000-0000-7000-8000-000000000002].chopro");
		filename.Length.ShouldBeLessThanOrEqualTo(PortableManagedFileName.MaxUtf16Length);
		Encoding.UTF8.GetByteCount(filename).ShouldBeLessThanOrEqualTo(PortableManagedFileName.MaxUtf8Length);
		filename.IsNormalized(NormalizationForm.FormC).ShouldBeTrue();
	}

	[TestMethod]
	public void UnsafeCharactersAreReplaced()
	{
		Guid id = Guid.Parse("018f0000-0000-7000-8000-000000000003");

		string filename = PortableManagedFileName.Create("A/B:C*D?", id, "cho");

		filename.ShouldStartWith("A_B_C_D_");
		PortableManagedFileName.Validate(filename).ShouldBeEmpty();
	}

	[TestMethod]
	[DataRow("../song.cho")]
	[DataRow("folder/song.cho")]
	[DataRow("folder\\song.cho")]
	[DataRow("CON")]
	[DataRow("song.cho.")]
	public void UnsafeManagedPathsAreRejected(string filename)
	{
		PortableManagedFileName.Validate(filename).ShouldNotBeEmpty();
	}
}
