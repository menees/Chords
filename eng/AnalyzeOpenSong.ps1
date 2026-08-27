param(
	[Parameter(Mandatory = $true)]
	[string] $RootPath,

	[string] $OutputPath = (Join-Path $PWD "OpenSongAnalysis.json"),

	[int] $FailureSampleLimit = 100
)

$ErrorActionPreference = "Stop"

function Add-Count {
	param(
		[hashtable] $Table,
		[string] $Key,
		[long] $Amount = 1
	)

	if ($Table.ContainsKey($Key)) {
		$Table[$Key] += $Amount
	}
	else {
		$Table[$Key] = $Amount
	}
}

function Get-ByteSignature {
	param(
		[byte[]] $Bytes,
		[int] $Count
	)

	if ($Count -ge 4) {
		if ($Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE -and $Bytes[2] -eq 0x00 -and $Bytes[3] -eq 0x00) { return "utf-32-le-bom" }
		if ($Bytes[0] -eq 0x00 -and $Bytes[1] -eq 0x00 -and $Bytes[2] -eq 0xFE -and $Bytes[3] -eq 0xFF) { return "utf-32-be-bom" }
		if ($Bytes[0] -eq 0x3C -and $Bytes[1] -eq 0x00 -and $Bytes[2] -eq 0x00 -and $Bytes[3] -eq 0x00) { return "utf-32-le-no-bom" }
		if ($Bytes[0] -eq 0x00 -and $Bytes[1] -eq 0x00 -and $Bytes[2] -eq 0x00 -and $Bytes[3] -eq 0x3C) { return "utf-32-be-no-bom" }
		if ($Bytes[0] -eq 0x3C -and $Bytes[1] -eq 0x00 -and $Bytes[2] -eq 0x3F -and $Bytes[3] -eq 0x00) { return "utf-16-le-no-bom" }
		if ($Bytes[0] -eq 0x00 -and $Bytes[1] -eq 0x3C -and $Bytes[2] -eq 0x00 -and $Bytes[3] -eq 0x3F) { return "utf-16-be-no-bom" }
	}

	if ($Count -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) { return "utf-8-bom" }
	if ($Count -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) { return "utf-16-le-bom" }
	if ($Count -ge 2 -and $Bytes[0] -eq 0xFE -and $Bytes[1] -eq 0xFF) { return "utf-16-be-bom" }
	return "no-bom-or-unrecognized"
}

function Convert-Counts {
	param([hashtable] $Table)

	return @($Table.GetEnumerator() |
		Sort-Object -Property @{ Expression = "Value"; Descending = $true }, @{ Expression = "Key"; Descending = $false } |
		ForEach-Object { [pscustomobject]@{ Name = [string]$_.Key; Count = [long]$_.Value } })
}

function Get-Percentile {
	param(
		[long[]] $SortedValues,
		[double] $Percentile
	)

	if ($SortedValues.Count -eq 0) { return 0 }
	$index = [Math]::Ceiling(($Percentile / 100.0) * $SortedValues.Count) - 1
	$index = [Math]::Max(0, [Math]::Min($SortedValues.Count - 1, $index))
	return $SortedValues[$index]
}

$resolvedRoot = (Resolve-Path -LiteralPath $RootPath).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
$files = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File)
$stopwatch = [Diagnostics.Stopwatch]::StartNew()

$rootElements = @{}
$rootNamespaces = @{}
$declaredEncodings = @{}
$normalizedEncodings = @{}
$byteSignatures = @{}
$elementOccurrences = @{}
$elementFiles = @{}
$elementEmptyValues = @{}
$attributeOccurrences = @{}
$loadErrorTypes = @{}
$loadErrorMessages = @{}
$folderStats = @{}
$failureSamples = [Collections.Generic.List[object]]::new()
$sizes = [Collections.Generic.List[long]]::new($files.Count)

[long] $totalBytes = 0
[long] $validXmlCount = 0
[long] $openSongCount = 0
[long] $loadFailureCount = 0
[long] $filesWithDeclaration = 0
[long] $filesWithLyrics = 0
[long] $filesWithSlideBreak = 0
[long] $totalLyricsCharacters = 0
[long] $totalLyricsLines = 0
[long] $totalChordLines = 0
[long] $totalLyricLines = 0
[long] $totalSectionLines = 0
[long] $totalSlideBreakTokens = 0
$largestFile = $null
$largestLyrics = $null

for ($index = 0; $index -lt $files.Count; $index++) {
	$file = $files[$index]
	$totalBytes += $file.Length
	$sizes.Add($file.Length)
	if ($null -eq $largestFile -or $file.Length -gt $largestFile.Bytes) {
		$largestFile = [pscustomobject]@{ Path = $file.FullName; Bytes = $file.Length }
	}

	$relativePath = [IO.Path]::GetRelativePath($resolvedRoot, $file.FullName)
	$folderName = $relativePath.Split([IO.Path]::DirectorySeparatorChar, 2)[0]
	if (-not $folderStats.ContainsKey($folderName)) {
		$folderStats[$folderName] = [ordered]@{ Files = [long]0; Bytes = [long]0; ValidXml = [long]0; OpenSong = [long]0; Failed = [long]0 }
	}
	$folder = $folderStats[$folderName]
	$folder.Files++
	$folder.Bytes += $file.Length

	$stream = $null
	try {
		$stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
		$prefix = [byte[]]::new(4)
		$prefixCount = $stream.Read($prefix, 0, $prefix.Length)
		$signature = Get-ByteSignature -Bytes $prefix -Count $prefixCount
		Add-Count $byteSignatures $signature
		$stream.Position = 0

		$document = [Xml.Linq.XDocument]::Load($stream, [Xml.Linq.LoadOptions]::PreserveWhitespace)
		$validXmlCount++
		$folder.ValidXml++

		$declared = $document.Declaration.Encoding
		if ([string]::IsNullOrWhiteSpace($declared)) {
			Add-Count $declaredEncodings "(none)"
			$effective = switch ($signature) {
				"utf-8-bom" { "utf-8" }
				"utf-16-le-bom" { "utf-16le" }
				"utf-16-be-bom" { "utf-16be" }
				"utf-16-le-no-bom" { "utf-16le" }
				"utf-16-be-no-bom" { "utf-16be" }
				"utf-32-le-bom" { "utf-32le" }
				"utf-32-be-bom" { "utf-32be" }
				"utf-32-le-no-bom" { "utf-32le" }
				"utf-32-be-no-bom" { "utf-32be" }
				default { "utf-8 (XML default)" }
			}
		}
		else {
			$filesWithDeclaration++
			Add-Count $declaredEncodings $declared.ToLowerInvariant()
			try {
				$effective = [Text.Encoding]::GetEncoding($declared).WebName
			}
			catch {
				$effective = $declared.ToLowerInvariant()
			}
		}
		Add-Count $normalizedEncodings $effective

		$root = $document.Root
		$rootName = if ($null -eq $root) { "(none)" } else { $root.Name.LocalName }
		$rootNamespace = if ($null -eq $root -or [string]::IsNullOrEmpty($root.Name.NamespaceName)) { "(none)" } else { $root.Name.NamespaceName }
		Add-Count $rootElements $rootName
		Add-Count $rootNamespaces $rootNamespace

		if ($null -ne $root -and $root.Name.LocalName -eq "song") {
			$openSongCount++
			$folder.OpenSong++
			$seenElements = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
			foreach ($element in $root.Elements()) {
				$name = $element.Name.LocalName
				Add-Count $elementOccurrences $name
				[void] $seenElements.Add($name)
				if ([string]::IsNullOrWhiteSpace($element.Value)) {
					Add-Count $elementEmptyValues $name
				}

				foreach ($attribute in $element.Attributes()) {
					Add-Count $attributeOccurrences "$name@$($attribute.Name.LocalName)"
				}
			}
			foreach ($name in $seenElements) {
				Add-Count $elementFiles $name
			}

			$lyrics = $root.Elements() | Where-Object { $_.Name.LocalName -eq "lyrics" } | Select-Object -First 1
			if ($null -ne $lyrics) {
				$filesWithLyrics++
				$lyricsText = $lyrics.Value
				$lyricsLength = $lyricsText.Length
				$totalLyricsCharacters += $lyricsLength
				$lines = [regex]::Split($lyricsText, "\r\n|\n|\r")
				$totalLyricsLines += $lines.Count
				$totalChordLines += @($lines | Where-Object { $_.StartsWith(".") }).Count
				$totalLyricLines += @($lines | Where-Object { $_.Length -gt 0 -and [char]::IsWhiteSpace($_[0]) }).Count
				$totalSectionLines += @($lines | Where-Object { $_ -match "^\s*\[[^\]]+\]\s*$" }).Count
				$slideBreakCount = ([regex]::Matches($lyricsText, "\|\|")).Count
				if ($slideBreakCount -gt 0) {
					$filesWithSlideBreak++
					$totalSlideBreakTokens += $slideBreakCount
				}
				if ($null -eq $largestLyrics -or $lyricsLength -gt $largestLyrics.Characters) {
					$largestLyrics = [pscustomobject]@{ Path = $file.FullName; Characters = $lyricsLength; Lines = $lines.Count }
				}
			}
		}
	}
	catch {
		$loadFailureCount++
		$folder.Failed++
		$errorType = $_.Exception.GetType().FullName
		$errorMessage = $_.Exception.Message
		Add-Count $loadErrorTypes $errorType
		Add-Count $loadErrorMessages $errorMessage
		if ($failureSamples.Count -lt $FailureSampleLimit) {
			$failureSamples.Add([pscustomobject]@{ Path = $file.FullName; Bytes = $file.Length; ErrorType = $errorType; Message = $errorMessage })
		}
	}
	finally {
		if ($null -ne $stream) { $stream.Dispose() }
	}

	if (($index + 1) % 10000 -eq 0) {
		Write-Host ("Processed {0:N0}/{1:N0} files in {2:N1}s; failures: {3:N0}" -f ($index + 1), $files.Count, $stopwatch.Elapsed.TotalSeconds, $loadFailureCount)
	}
}

$stopwatch.Stop()
$sortedSizes = [long[]]($sizes | Sort-Object)
$folders = @($folderStats.GetEnumerator() | Sort-Object Key | ForEach-Object {
	[pscustomobject]@{
		Name = $_.Key
		Files = $_.Value.Files
		Bytes = $_.Value.Bytes
		ValidXml = $_.Value.ValidXml
		OpenSong = $_.Value.OpenSong
		Failed = $_.Value.Failed
	}
})

$summary = [ordered]@{
	GeneratedUtc = [DateTime]::UtcNow.ToString("O")
	RootPath = $resolvedRoot
	ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
	Files = [ordered]@{
		Total = $files.Count
		TotalBytes = $totalBytes
		ValidXml = $validXmlCount
		OpenSongRoots = $openSongCount
		LoadFailures = $loadFailureCount
		Largest = $largestFile
		SizeBytes = [ordered]@{
			Minimum = if ($sortedSizes.Count -eq 0) { 0 } else { $sortedSizes[0] }
			Median = Get-Percentile $sortedSizes 50
			P90 = Get-Percentile $sortedSizes 90
			P95 = Get-Percentile $sortedSizes 95
			P99 = Get-Percentile $sortedSizes 99
			Maximum = if ($sortedSizes.Count -eq 0) { 0 } else { $sortedSizes[-1] }
			Average = if ($files.Count -eq 0) { 0 } else { [Math]::Round($totalBytes / $files.Count, 2) }
		}
	}
	Encoding = [ordered]@{
		FilesWithXmlDeclarationEncoding = $filesWithDeclaration
		Declared = Convert-Counts $declaredEncodings
		NormalizedEffective = Convert-Counts $normalizedEncodings
		ByteSignatures = Convert-Counts $byteSignatures
	}
	Xml = [ordered]@{
		RootElements = Convert-Counts $rootElements
		RootNamespaces = Convert-Counts $rootNamespaces
		DirectSongElementsByOccurrence = Convert-Counts $elementOccurrences
		DirectSongElementsByFile = Convert-Counts $elementFiles
		EmptyDirectSongElementValues = Convert-Counts $elementEmptyValues
		DirectSongElementAttributes = Convert-Counts $attributeOccurrences
	}
	Lyrics = [ordered]@{
		FilesWithLyricsElement = $filesWithLyrics
		TotalCharacters = $totalLyricsCharacters
		TotalLines = $totalLyricsLines
		ChordLinesBeginningWithPeriod = $totalChordLines
		LyricLinesBeginningWithWhitespace = $totalLyricLines
		SectionHeaderLines = $totalSectionLines
		FilesContainingSlideBreakToken = $filesWithSlideBreak
		TotalSlideBreakTokens = $totalSlideBreakTokens
		Largest = $largestLyrics
	}
	Failures = [ordered]@{
		ByExceptionType = Convert-Counts $loadErrorTypes
		ByMessage = Convert-Counts $loadErrorMessages
		Samples = @($failureSamples)
	}
	Folders = $folders
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrEmpty($outputDirectory)) {
	[void] [IO.Directory]::CreateDirectory($outputDirectory)
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-Host ("Completed {0:N0} files in {1:N1}s. Valid XML: {2:N0}; OpenSong: {3:N0}; failures: {4:N0}." -f $files.Count, $stopwatch.Elapsed.TotalSeconds, $validXmlCount, $openSongCount, $loadFailureCount)
Write-Host "Summary: $OutputPath"
