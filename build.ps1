#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the JavOrganizer plugin for one or every supported Jellyfin line.

.DESCRIPTION
    Produces one publish folder per build under .\dist\, each ready
    to be copied into a server's plugins directory:

        dist\JavOrganizer-10.8\   (Jellyfin 10.8.x, net6.0)
        dist\JavOrganizer-10.9\   (Jellyfin 10.9.x and every later version,
                                   including releases not yet published,
                                   net8.0)

    Two builds cover the whole supported range. 10.8 needs its own because
    Jellyfin 10.9 moved IPluginServiceRegistrator to another namespace and
    changed its RegisterServices signature, and that interface has to be
    implemented against whichever the running server exposes. Everything
    that changed after 10.9 is resolved at runtime instead, so the 10.9
    build needs no successor as new Jellyfin versions arrive.

    Each folder also contains the .meta sidecar file Jellyfin uses to
    recognize a manually installed plugin.

.PARAMETER JellyfinVersion
    Build only this build (10.8 or 10.9). Omit to build both.

.PARAMETER Configuration
    Build configuration; defaults to Release.

.EXAMPLE
    .\build.ps1                       # build everything
    .\build.ps1 -JellyfinVersion 10.9 # build only the 10.9-and-later build
#>
param(
    [ValidateSet('10.8', '10.9')]
    [string]$JellyfinVersion,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$dist = Join-Path $root 'dist'

$pluginGuid = 'e9e8bfe2-5e0f-4d1a-9a3c-7b7ea4e7581c'
$pluginName = 'JavOrganizer'
$pluginVersion = '1.5.3.0'

# ABI version each build advertises. The 10.9 build declares 10.9 so that
# every later Jellyfin - which accepts any plugin whose abi is not newer
# than its own - is offered it.
$abiMap = @{
    '10.8' = '10.8.13.0'
    '10.9' = '10.9.11.0'
}

$targets = if ($JellyfinVersion) { @($JellyfinVersion) } else { @('10.8', '10.9') }

$failed = @()
foreach ($v in $targets) {
    Write-Host ""
    Write-Host "=== Building JavOrganizer for Jellyfin $v ===" -ForegroundColor Cyan
    $out = Join-Path $dist "$pluginName-$v"

    dotnet publish (Join-Path $root 'Jellyfin.Plugin.JavOrganizer.csproj') `
        -c $Configuration `
        -p:JellyfinVersion=$v `
        -o $out

    if ($LASTEXITCODE -ne 0) {
        $failed += $v
        continue
    }

    # Meta sidecar: Jellyfin reads this to register a manually installed plugin.
    $meta = @{
        guid        = $pluginGuid
        name        = $pluginName
        version     = $pluginVersion
        targetAbi   = $abiMap[$v]
        source      = $pluginName
        status      = 'Active'
        autoUpdate  = $false
        installDate = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json -Compress

    Set-Content -Path (Join-Path $out 'Jellyfin.Plugin.JavOrganizer.dll.meta') -Value $meta

    # Release zip: the format the online plugin installer consumes.
    # Contains the plugin dll + dependency + meta sidecar at the zip root.
    $zip = Join-Path $dist "$pluginName-$($v -replace '\.','')-v$pluginVersion.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "OK   $out" -ForegroundColor Green
    Write-Host "ZIP  $zip" -ForegroundColor Green
    Write-Host "SHA256 $hash" -ForegroundColor Green
    $hash | Set-Content "$zip.sha256"
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "FAILED targets: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "All requested targets built into $dist" -ForegroundColor Green
