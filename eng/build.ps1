param(
    [bool] $build = $true,
    [bool] $test = $false,
    [string[]] $configurations = @('Debug', 'Release'),
	[bool] $publish = $false,
    [string] $msBuildVerbosity = 'minimal',
    [string] $nugetApiKey = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptPath = [IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Definition)
$repoPath = Resolve-Path (Join-Path $scriptPath '..')
$slnPath = @(Get-ChildItem -Path $repoPath -Filter '*.sln')
if (!$slnPath)
{
	throw "Solution not found at $repoPath."
}

$slnPath = $slnPath[0].FullName
$productName = [IO.Path]::GetFileNameWithoutExtension($slnPath)

function GetXmlPropertyValue($fileName, $propertyName)
{
	$result = Get-Content $fileName |`
		Where-Object {$_ -like "*<$propertyName>*</$propertyName>*"} |`
		ForEach-Object {$_.Replace("<$propertyName>", '').Replace("</$propertyName>", '').Trim()}
	return $result
}

if ($build)
{
    foreach ($configuration in $configurations)
    {
        # Restore NuGet packages first
        msbuild $slnPath /p:Configuration=$configuration /t:Restore /v:$msBuildVerbosity /nologo
        msbuild $slnPath /p:Configuration=$configuration /v:$msBuildVerbosity /nologo
    }
}

if ($test)
{
    foreach ($configuration in $configurations)
    {
    	dotnet test $slnPath
    }
}

if ($publish)
{
	$buildPropsFile = "$repoPath\src\Directory.Build.props"
	$version = GetXmlPropertyValue $buildPropsFile 'Version'

    $published = $false
    if ($version)
    {
        $artifactsPath = "$repoPath\artifacts"
		if (Test-Path $artifactsPath)
		{
			Remove-Item -Recurse -Force $artifactsPath
		}

		$ignore = mkdir $artifactsPath
		if ($ignore) { } # For PSUseDeclaredVarsMoreThanAssignments

        foreach ($configuration in $configurations)
        {
            if ($configuration -like '*Release*')
            {
				Write-Host "Publishing version $version $configuration profiles to $artifactsPath"
				$profiles = @(Get-ChildItem -r "$repoPath\src\**\Properties\PublishProfiles\*.pubxml")
				foreach ($profile in $profiles)
				{
					$profileName = [IO.Path]::GetFileNameWithoutExtension($profile)
					Write-Host "Publishing $profileName"

					# The Publish target in "C:\Program Files\dotnet\sdk\3.1.101\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Sdk.CrossTargeting.targets"
					# throws an exception if the .csproj uses <TargetFrameworks>. We have to override that and force a specific <TargetFramework> instead.
					# In .NET SDK 7.0.306 we'll get a NETSDK1198 warning due to an SDK bug: https://github.com/Azure/azure-functions-dotnet-worker/issues/1798
					$targetFramework = GetXmlPropertyValue $buildPropsFile "MeneesTargetNet$profileName"
					msbuild $slnPath /t:Publish /p:PublishProfile=$profile /p:TargetFramework=$targetFramework /v:$msBuildVerbosity /nologo /p:Configuration=$configuration

					Remove-Item "$artifactsPath\$profileName\*.pdb"

					Compress-Archive -Path "$artifactsPath\$profileName\*" -DestinationPath "$artifactsPath\$productName-Portable-$version-$profileName.zip"
				}

                Write-Host "Publishing version $version $configuration packages to $artifactsPath"
                $packages = @(Get-ChildItem -r "$repoPath\src\**\*.$version.nupkg" | Where-Object {$_.Directory -like "*\bin\$configuration"})
                foreach ($package in $packages)
                {
                    Write-Host "Copying $package"
                    Copy-Item -Path $package -Destination $artifactsPath -Force

                    if ($nugetApiKey)
                    {
                        $artifactPackage = Join-Path $artifactsPath (Split-Path -Leaf $package)
                        dotnet nuget push $artifactPackage -k $nugetApiKey -s https://api.nuget.org/v3/index.json --skip-duplicate
                        $published = $true
                    }
                }
            }
        }
    }

    if ($published)
    {
		Write-Host "`n`n****** REMEMBER TO ADD A GITHUB RELEASE! ******"
    }
}
