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

# Check the resolved target before mirroring anything. OneDrive placeholders are allowed;
# filesystem symbolic links and junctions must not redirect replacement elsewhere.
$resolvedDestination = (Resolve-Path -LiteralPath $destinationPath).ProviderPath.TrimEnd('\')
if (!$resolvedDestination.Equals($acceptanceDirectory, $comparison))
{
    throw 'The resolved acceptance directory does not match the authorized target.'
}

$running = @(Get-Process -Name 'Menees.Chords.Book.Maui' -ErrorAction SilentlyContinue | Where-Object {
    !$_.Path -or $_.Path.StartsWith($destinationPath + '\', $comparison)
})
if ($running.Count)
{
    throw 'Save edits and close ChordBook running from Maui.Acceptance before replacing it. Acceptance contents were not changed.'
}

# Compare bytes rather than publish timestamps: a rebuild can touch unchanged dependencies.
# Reject redirected children before hashing, deleting, or copying outside the fixed target.
$sourceItems = @(Get-ChildItem -LiteralPath $sourcePath -Recurse -Force)
$destinationItems = @(Get-ChildItem -LiteralPath $destinationPath -Recurse -Force)
foreach ($item in @((Get-Item -LiteralPath $sourcePath -Force)) + $sourceItems + $destinationItems)
{
    if ($item.LinkType)
    {
        throw "Symbolic links and junctions are not supported in acceptance builds: $($item.FullName)"
    }
}

# Reject locked browser/application files before changing any old content.
foreach ($file in $destinationItems | Where-Object { !$_.PSIsContainer })
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

$sourceFiles = @($sourceItems | Where-Object { !$_.PSIsContainer })
$changedFiles = [Collections.Generic.List[IO.FileInfo]]::new()
$sourceEntries = @{}
foreach ($item in $sourceItems)
{
    $relative = $item.FullName.Substring($sourcePath.Length + 1)
    $sourceEntries[$relative] = $item
}
foreach ($file in $sourceFiles)
{
    $relative = $file.FullName.Substring($sourcePath.Length + 1)
    $target = Join-Path $destinationPath $relative
    if (Test-Path -LiteralPath $target)
    {
        $existingFile = Get-Item -LiteralPath $target -Force
        if ($existingFile.PSIsContainer)
        {
            throw "Acceptance file/directory type changed: $relative. Acceptance contents were not changed."
        }
        if ($existingFile.Length -eq $file.Length -and
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
        {
            continue
        }
    }
    $changedFiles.Add($file)
}
foreach ($directory in $sourceItems | Where-Object PSIsContainer)
{
    $target = Join-Path $destinationPath $directory.FullName.Substring($sourcePath.Length + 1)
    if ((Test-Path -LiteralPath $target) -and !(Get-Item -LiteralPath $target -Force).PSIsContainer)
    {
        throw "Acceptance file/directory type changed: $target. Acceptance contents were not changed."
    }
}

# Validation and comparison are complete. Retain unchanged files and their timestamps.
foreach ($directory in $sourceItems | Where-Object PSIsContainer)
{
    $target = Join-Path $destinationPath $directory.FullName.Substring($sourcePath.Length + 1)
    if (!(Test-Path -LiteralPath $target))
    {
        New-Item -ItemType Directory -Path $target | Out-Null
    }
}
foreach ($file in $changedFiles)
{
    $target = Join-Path $destinationPath $file.FullName.Substring($sourcePath.Length + 1)
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
$removedFiles = 0
foreach ($file in $destinationItems | Where-Object { !$_.PSIsContainer })
{
    $relative = $file.FullName.Substring($destinationPath.Length + 1)
    if (!$sourceEntries.ContainsKey($relative))
    {
        Remove-Item -LiteralPath $file.FullName -Force
        $removedFiles++
    }
}
# Remove stale directories only after their files, deepest first; never recursively delete.
foreach ($directory in $destinationItems | Where-Object PSIsContainer | Sort-Object { $_.FullName.Length } -Descending)
{
    $relative = $directory.FullName.Substring($destinationPath.Length + 1)
    if (!$sourceEntries.ContainsKey($relative))
    {
        [IO.Directory]::Delete($directory.FullName, $false)
    }
}

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
Write-Output "Mirrored acceptance: $($changedFiles.Count) added/updated, $removedFiles removed, $($sourceFiles.Count - $changedFiles.Count) unchanged; $($sourceFiles.Count) verified published files: $destinationPath"
