using System.IO;
using System.Globalization;
using System.Text;

// Minimal, deterministic PDF fixture. Only ASCII text and vector marks; no external generator.
internal static class SyntheticPdf
{
	public static byte[] Create(int pageCount)
	{
		var objects = new List<string>
		{
			"<< /Type /Catalog /Pages 2 0 R >>",
			$"<< /Type /Pages /Count {pageCount} /Kids [{string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{4 + (i * 2)} 0 R"))}] >>",
			"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
		};
		for (int index = 0; index < pageCount; index++)
		{
			objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (index * 2)} 0 R >>");
			string content = $"0.2 0.3 0.5 RG 4 w 20 20 572 752 re S BT /F1 30 Tf 60 680 Td (Synthetic sheet {index + 1}) Tj ET\n";
			objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
		}

		using var output = new MemoryStream();
		void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
		Write("%PDF-1.4\n");
		var offsets = new List<long>();
		for (int index = 0; index < objects.Count; index++)
		{
			offsets.Add(output.Position);
			Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
		}

		long xref = output.Position;
		Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
		foreach (long offset in offsets) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
		Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
		return output.ToArray();
	}
}
