# Runs the production copy script against disposable folders, never the OneDrive target.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ChordBookMirror-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$target = Join-Path $fixture 'acceptance'
New-Item -ItemType Directory -Path $source | Out-Null
$script = [IO.File]::ReadAllText((Join-Path $repo 'eng/Copy-ChordBookAcceptance.ps1'))
$fixedTarget = 'C:\Users\Bill\OneDrive\Documents\Music\Creations\ChordBookPrototypes\Maui.Acceptance'
if (!$script.Contains($fixedTarget)) { throw 'Production target binding not found.' }
# Substitute only the fixed path so all production validation/mirroring executes unchanged.
$copy = [ScriptBlock]::Create($script.Replace($fixedTarget, $target))
function Write-FixtureFile([string] $Path, [string] $Text)
{
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}
function Assert([bool] $Condition, [string] $Message)
{
    if (!$Condition) { throw $Message }
}
foreach ($relative in @('Menees.Chords.Book.Maui.exe', 'Editor\editor.html', 'Viewer\viewer.html',
    'Viewer\pdfjs\build\pdf.worker.mjs', 'ONEDRIVE.md', 'WINDOWS_DEPLOYMENT.md', 'RECOVERY.md'))
{
    Write-FixtureFile (Join-Path $source $relative) 'fixture'
}
# Hundreds of byte-identical dependencies represent a publish with fresh timestamps.
foreach ($index in 1..800)
{
    Write-FixtureFile (Join-Path $source "dependencies\$index.dll") ('dependency' * 100)
}
New-Item -ItemType Directory -Path (Join-Path $source 'empty') | Out-Null
& $copy -SourceDirectory $source
Assert (Test-Path -LiteralPath (Join-Path $target 'empty') -PathType Container) 'Empty directory was not mirrored.'
$unchanged = Join-Path $target 'dependencies\1.dll'
$stamp = [DateTime]::UtcNow.AddDays(-2)
[IO.File]::SetLastWriteTimeUtc($unchanged, $stamp)
$before = [IO.File]::GetLastWriteTimeUtc($unchanged)
$timer = [Diagnostics.Stopwatch]::StartNew()
$noOp = & $copy -SourceDirectory $source
$timer.Stop()
Assert ($noOp -match '0 added/updated, 0 removed, 807 unchanged') 'No-op mirror performed writes.'
Assert ([IO.File]::GetLastWriteTimeUtc($unchanged) -eq $before) 'Unchanged destination timestamp changed.'
Write-FixtureFile (Join-Path $source 'Editor\editor.html') 'changed' # Same size as fixture.
Write-FixtureFile (Join-Path $source 'new\nested.dll') 'new'
Write-FixtureFile (Join-Path $target 'stale\nested\old.dll') 'old'
(Get-Item -LiteralPath (Join-Path $target 'stale\nested\old.dll')).Attributes = [IO.FileAttributes]::Hidden
$incremental = & $copy -SourceDirectory $source
Assert ($incremental -match '2 added/updated, 1 removed, 806 unchanged') 'Incremental work counts were unexpected.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'Editor\editor.html')) -eq 'changed') 'Same-length changed bytes were not copied.'
Assert (!(Test-Path -LiteralPath (Join-Path $target 'stale'))) 'Stale directories remained.'
Assert ([IO.File]::GetLastWriteTimeUtc($unchanged) -eq $before) 'Incremental mirror rewrote unchanged dependency.'
# A locked file must refuse the entire mirror before even copying another changed file.
Write-FixtureFile (Join-Path $source 'Editor\editor.html') 'blocked'
$locked = [IO.File]::Open($unchanged, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try
{
    $refused = $false
    try { & $copy -SourceDirectory $source | Out-Null } catch { $refused = $_.Exception.Message -like '*in use or unavailable*' }
    Assert $refused 'Locked target did not refuse the mirror.'
    Assert ([IO.File]::ReadAllText((Join-Path $target 'Editor\editor.html')) -eq 'changed') 'Lock refusal modified destination.'
}
finally { $locked.Dispose() }
# Missing required assets and overlap must also fail without altering acceptance.
Remove-Item -LiteralPath (Join-Path $source 'Menees.Chords.Book.Maui.exe')
$refused = $false
try { & $copy -SourceDirectory $source | Out-Null } catch { $refused = $_.Exception.Message -like '*Published asset is missing*' }
Assert $refused 'Incomplete source did not refuse the mirror.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'Editor\editor.html')) -eq 'changed') 'Incomplete source modified destination.'
$refused = $false
try { & $copy -SourceDirectory $target | Out-Null } catch { $refused = $_.Exception.Message -like '*separate folders*' }
Assert $refused 'Overlapping target was allowed.'
# File/directory conflicts and redirected children must also refuse before writes.
Write-FixtureFile (Join-Path $source 'Menees.Chords.Book.Maui.exe') 'fixture'
Write-FixtureFile (Join-Path $source 'conflict') 'file'
New-Item -ItemType Directory -Path (Join-Path $target 'conflict') | Out-Null
$refused = $false
try { & $copy -SourceDirectory $source | Out-Null } catch { $refused = $_.Exception.Message -like '*type changed*' }
Assert $refused 'File/directory conflict was allowed.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'Editor\editor.html')) -eq 'changed') 'Type conflict modified destination.'
Remove-Item -LiteralPath (Join-Path $source 'conflict')
[IO.Directory]::Delete((Join-Path $target 'conflict'), $false)
$outside = Join-Path $fixture 'outside'
New-Item -ItemType Directory -Path $outside | Out-Null
Write-FixtureFile (Join-Path $outside 'keep.txt') 'keep'
New-Item -ItemType Junction -Path (Join-Path $target 'redirected') -Target $outside | Out-Null
$refused = $false
try { & $copy -SourceDirectory $source | Out-Null } catch { $refused = $_.Exception.Message -like '*Symbolic links and junctions*' }
Assert $refused 'Redirected child was allowed.'
Assert ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -eq 'keep') 'Redirected child modified outside files.'
Assert ([IO.File]::ReadAllText((Join-Path $target 'Editor\editor.html')) -eq 'changed') 'Redirected child modified destination.'
Write-Output "PASS: initial, no-op, changed timestamps, same-size changes, additions, hidden stale files/directories, empty directories, locks, incomplete source and overlap."
Write-Output "807-file no-op mirror: $($timer.ElapsedMilliseconds) ms, zero writes. Disposable fixture: $fixture"

