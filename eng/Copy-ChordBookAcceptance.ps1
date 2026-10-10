param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$acceptanceDirectory = 'C:\Users\Bill\OneDrive\Documents\Music\Creations\ChordBookPrototypes\Maui.Acceptance'
$destinationPath = [IO.Path]::GetFullPath($acceptanceDirectory).TrimEnd('\')
$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).ProviderPath.TrimEnd('\')
$sourcePath = [IO.Path]::GetFullPath($sourcePath)
$comparison = [StringComparison]::OrdinalIgnoreCase
if ($destinationPath -ne $acceptanceDirectory -or
    $sourcePath.Equals($destinationPath, $comparison) -or
    $sourcePath.StartsWith($destinationPath + '\', $comparison) -or
    $destinationPath.StartsWith($sourcePath + '\', $comparison))
{
    throw 'The published source and fixed acceptance directory must be separate folders.'
}

foreach ($required in @('Menees.Chords.Book.Maui.exe', 'Editor\editor.html', 'Viewer\viewer.html',
    'Viewer\pdfjs\build\pdf.worker.mjs', 'ONEDRIVE.md', 'WINDOWS_DEPLOYMENT.md', 'RECOVERY.md'))
{
    if (!(Test-Path -LiteralPath (Join-Path $sourcePath $required) -PathType Leaf))
    {
        throw "Published asset is missing: $required. Acceptance contents were not changed."
    }
}

if (Test-Path -LiteralPath $destinationPath)
{
    $destination = Get-Item -LiteralPath $destinationPath -Force
    if (!$destination.PSIsContainer -or $destination.LinkType)
    {
        throw 'The acceptance target must be a directory, not a symbolic link or junction.'
    }
}
else
{
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
}

# Check the resolved target before deleting anything. OneDrive placeholders are allowed;
# filesystem symbolic links and junctions must not redirect replacement elsewhere.
$resolvedDestination = (Resolve-Path -LiteralPath $destinationPath).ProviderPath.TrimEnd('\')
if (!$resolvedDestination.Equals($acceptanceDirectory, $comparison))
{
    throw 'The resolved acceptance directory does not match the authorized target.'
}

$running = @(Get-Process -Name 'Menees.Chords.Book.Maui' -ErrorAction SilentlyContinue | Where-Object {
    !$_.Path -or $_.Path.StartsWith($destinationPath + '\\', $comparison)
})
if ($running.Count)
{
    throw 'Save edits and close ChordBook running from Maui.Acceptance before replacing it. Acceptance contents were not changed.'
}

# Reject locked browser/application files before removing any old content.
foreach ($file in Get-ChildItem -LiteralPath $destinationPath -File -Recurse -Force)
{
    try
    {
        $probe = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $probe.Dispose()
    }
    catch
    {
        throw "Acceptance file is in use or unavailable: $($file.FullName). Close the app and retry. Acceptance contents were not changed."
    }
}

$existing = @(Get-ChildItem -LiteralPath $destinationPath -Force)
foreach ($child in $existing)
{
    $childPath = [IO.Path]::GetFullPath($child.FullName)
    if (!$childPath.StartsWith($destinationPath + '\', $comparison))
    {
        throw 'An acceptance child resolved outside the authorized target.'
    }
}

# Source validation is complete. Clear all previous files, including hidden/stale files.
foreach ($child in $existing)
{
    Remove-Item -LiteralPath $child.FullName -Recurse -Force
}
foreach ($child in Get-ChildItem -LiteralPath $sourcePath -Force)
{
    Copy-Item -LiteralPath $child.FullName -Destination $destinationPath -Recurse -Force
}

$sourceFiles = @(Get-ChildItem -LiteralPath $sourcePath -File -Recurse -Force)
$destinationFiles = @(Get-ChildItem -LiteralPath $destinationPath -File -Recurse -Force)
if ($sourceFiles.Count -ne $destinationFiles.Count)
{
    throw 'Acceptance copy has an unexpected file count. Do not launch it until copying succeeds.'
}
foreach ($file in $sourceFiles)
{
    $relative = $file.FullName.Substring($sourcePath.Length + 1)
    $copied = Get-Item -LiteralPath (Join-Path $destinationPath $relative)
    if ($copied.Length -ne $file.Length)
    {
        throw "Acceptance copy has an unexpected length: $relative."
    }
}
Write-Output "Replaced acceptance contents with $($sourceFiles.Count) published files: $destinationPath"
