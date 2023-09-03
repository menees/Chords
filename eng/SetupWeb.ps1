# Use "get-help -detailed .\SetupWeb.ps1" to see parameter help. (https://stackoverflow.com/a/54652886/1882616)

<#
.SYNOPSIS
Sets up IIS on Windows and checks whether the .NET Hosting Bundle needs to be (re)installed.
.DESCRIPTION
The SetupWeb.ps1 script should be run to make sure the required Windows features are enabled for IIS,
and to make sure the minimum required version of .NET is installed. It's safe to re-run the script.
#>
param
(
    [bool]$checkWindowsFeatures = $true # Whether to check that all required Windows features (e.g., IIS) are enabled.
    ,[bool]$checkDotNetVersion = $true # Whether to check that the required version of .NET is installed.
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

enum WindowsFeatureStatus
{
    AlreadyEnabled
    ChangedToEnabled
    RestartRequired
}

function Main()
{
    # From https://www.hanselman.com/blog/how-to-determine-if-a-user-is-a-local-administrator-with-powershell
    $isAdmin = (new-object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole("Administrators")
    if (!$isAdmin)
    {
        throw "You must be running as an administrator to setup IIS."
    }

    if ($PSVersionTable.PSEdition -eq 'Core')
    {
        throw "Use Windows PowerShell due to Get-WindowsOptionalFeature bug https://github.com/PowerShell/PowerShell/issues/13866 in Pwsh 7.x."
    }

    $windowsFeatureStatus = InstallRequiredWindowsFeatures

    # If we just installed IIS or any of its features, then we must force .NET to be (re)installed to ensure all ASP.NET core handlers are registered with IIS.
    $forceDotNetInstall = $windowsFeatureStatus -eq [WindowsFeatureStatus]::ChangedToEnabled
    if (($windowsFeatureStatus -ne [WindowsFeatureStatus]::RestartRequired) -and (CheckDotNetRuntime $forceDotNetInstall))
    {
        # IISAdministration is the newer module that replaces the older WebAdministration module. Unfortunately, IISAdministration
        # is PITA to install. So, here are some basic instructions instead:
        #
        # As of .NET 7.0, a folder-published Blazor app includes a web.config and a wwwroot folder. IIS requires the "URL Rewrite Module"
        # to be manually installed first to process the <rewrite> section of the generated web.config and redirect all requests to files
        # in the wwwroot folder. 
        # https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly#install-the-url-rewrite-module
        # https://stackoverflow.com/a/65111913/1882616 and https://stackoverflow.com/a/75577442/1882616.
        #
        # Also, we have to use a web site (not a web app) because Blazor uses our index.html's <base> href as its base path.
        # Create the web site with an unused port (e.g., 1233) and point the physical path to the artifacts\Web folder.
        # Since there's no server-side logic, it's fine to use the default app pool.
        #
        # Note: On real web hosts (e.g., SmarterASP.NET), the rewrite module is already present. At SmarterASP, I just had to create
        # a Chords folder, upload the Web artifact's .zip, unzip it, and then create a new web site that pointed to that folder.
        # The *.*tempurl.com site was immediately available, so I created a chords.menees.com subdomain that also pointed to the "temporary" site.
        Write-Host "Manual next steps:"        
        Write-Host "  Install URL Rewrite Module: https://iis-umbraco.azurewebsites.net/downloads/microsoft/url-rewrite"
        Write-Host "  Create Chords web site (not app due to index.html's <base> href) at port 1233 pointed to artifacts\Web folder."
    }
}

function InstallRequiredWindowsFeatures()
{
    $result = [WindowsFeatureStatus]::AlreadyEnabled

    if ($checkWindowsFeatures)
    {
        Write-Host 'Checking for required Windows features'
        # To see all available IIS features use a command like: Get-WindowsOptionalFeature -Online -FeatureName '*IIS*' >IISFeatures.txt
        # https://www.how2shout.com/how-to/how-to-install-iis-on-windows-10-using-powershell.html
        $requiredFeatureNames = @('IIS-WebServerRole', 'IIS-WebServer', 'IIS-ManagementConsole', 'IIS-ManagementScriptingTools', 'IIS-CommonHttpFeatures',
            'IIS-DefaultDocument', 'IIS-HttpErrors', 'IIS-StaticContent', 'IIS-HttpLogging', 'IIS-HttpCompressionStatic', 'IIS-RequestFiltering')
        $enableFeatureNames = @()
        foreach ($featureName in $requiredFeatureNames)
        {
            $featureInfo = Get-WindowsOptionalFeature -Online -FeatureName $featureName
            if ($featureInfo.State -ne 'Enabled')
            {
                $enableFeatureNames += $featureName
            }
        }

        if ($enableFeatureNames)
        {
            Write-Host "Enabling Windows features: $enableFeatureNames"
            $result = [WindowsFeatureStatus]::ChangedToEnabled
            $install = Enable-WindowsOptionalFeature -Online -FeatureName $enableFeatureNames
            if ($install.RestartNeeded)
            {
                $scriptName = Split-Path $scriptFullName -Leaf
                Write-Host "A system restart is required. Please reboot, then re-run the $scriptName script."
                $result = [WindowsFeatureStatus]::RestartRequired
            }
        }
    }

    return $result
}

function CheckDotNetRuntime($forceDotNetInstall)
{
    $result = $true

    if ($checkDotNetVersion -or $forceDotNetInstall)
    {
        $scriptPath = Split-Path -Parent $scriptFullName
        $repoPath = Resolve-Path (Join-Path $scriptPath '..')
        $buildPropsFile = "$repoPath\src\Directory.Build.props"
        $requiredDotNetVersion = (GetXmlPropertyValue $buildPropsFile 'MeneesTargetNetWeb').Replace('net', '')
        $minimumDotNetBuild = $requiredDotNetVersion
    
        $latestRuntime = $null
        try
        {
            $requiredVersionRegex = [Text.RegularExpressions.Regex]::Escape("Microsoft.AspNetCore.App $requiredDotNetVersion")
            $latestRuntime = dotnet --list-runtimes |Select-String $requiredVersionRegex| Select-Object -Last 1
        }
        catch [System.Management.Automation.CommandNotFoundException]
        {
            Write-Host 'Error: Unable to determine current dotnet runtime version.'
        }

        $generalDownloadPage = 'https://dotnet.microsoft.com/download/dotnet/'
        $indent = "  "
        $requireManualInstall = $true
        if ($forceDotNetInstall)
        {
            Write-Host "A .NET (re)install is required due to Windows feature changes."
        }
        elseif ($latestRuntime)
        {
            Write-Host "Found $latestRuntime"
            $parts = $latestRuntime.Line.Split(@(' ', '-'), [StringSplitOptions]::RemoveEmptyEntries)
            if ($parts.Length -ge 2)
            {
                [Version]$foundVersion = $null
                # Note: PowerShell requires parentheses around [ref] parameters.
                if ([Version]::TryParse($parts[1], ([ref]$foundVersion)) -and $foundVersion -ge [Version]::Parse($minimumDotNetBuild.Split('-')[0]))
                {
                    $requireManualInstall = $false
                }
            }
        }

        if ($requireManualInstall)
        {
            Write-Host "Version $minimumDotNetBuild of the ASP.NET Core Hosting Bundle is required."
            Write-Host "$($indent)It can be downloaded from $generalDownloadPage."
            Write-Host "$($indent)After runtime installation, restart IIS using the following commands: net stop was /y; net start w3svc"
            $result = $false
        }
    }

    return $result
}

function GetXmlPropertyValue($fileName, $propertyName)
{
	$result = Get-Content $fileName |`
		Where-Object {$_ -like "*<$propertyName>*</$propertyName>*"} |`
		ForEach-Object {$_.Replace("<$propertyName>", '').Replace("</$propertyName>", '').Trim()}
	return $result
}

# Put all logic in a Main function that we define at the top but invoke after all other functions are defined.
$scriptFullName = $MyInvocation.MyCommand.Definition
Main