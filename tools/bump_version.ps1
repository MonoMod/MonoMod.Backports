#Requires -Version 7

param (
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string] $BumpVersion = 'Patch'
)

$ErrorActionPreference = 'Stop';
Set-StrictMode -Version 3;

$VersionIndex = 0;
if ($BumpVersion -eq 'Major')
{
    $VersionIndex = 0;
}
elseif ($BumpVersion -eq 'Minor')
{
    $VersionIndex = 1;
}
elseif ($BumpVersion -eq 'Patch')
{
    $VersionIndex = 2;
}

Push-Location $PSScriptRoot/..;
try
{
    $props = Join-Path $PSScriptRoot .. "Version.props";

    # load the project up as a normal XML file
    $xml = [xml]::new();
    $xml.PreserveWhitespace = $true;
    $xml.Load($props);

    $version = $xml.Project.PropertyGroup.VersionPrefix;
    if ($VersionIndex -eq 0)
    {
        $xml.Project.PropertyGroup.PackageValidationBaselineVersion = "";
    }
    else
    {
        $xml.Project.PropertyGroup.PackageValidationBaselineVersion = $version;
    }

    $verParts = $version -csplit '\.',3;
    $verParts[$VersionIndex] = [string](([int]$verParts[$VersionIndex]) + 1);
    for ($i = $VersionIndex + 1; $i -lt $verParts.Length; $i++)
    {
        $verParts[$i] = '0';
    }
    $version = $verParts -join '.';
    $xml.Project.PropertyGroup.VersionPrefix = $version

    Write-Host "New version: $version";

    # and write the project file back out
    $xml.Save($props);
}
finally
{
    Pop-Location;
}