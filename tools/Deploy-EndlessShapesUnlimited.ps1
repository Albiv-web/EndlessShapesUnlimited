[CmdletBinding()]
param(
    [string]$SourceRoot = '',
    [string]$ModsRoot = (Join-Path $env:USERPROFILE 'Documents\From The Depths\Mods'),
    [string]$ModName = 'EndlessShapesUnlimited'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SourceRoot))
{
    $SourceRoot = Join-Path $PSScriptRoot '..\EndlessShapesUnlimited'
}

$source = Resolve-Path -LiteralPath $SourceRoot
$modsRootItem = New-Item -ItemType Directory -Force -Path $ModsRoot
$modsRootFull = [System.IO.Path]::GetFullPath($modsRootItem.FullName)
$destinationFull = [System.IO.Path]::GetFullPath((Join-Path $modsRootFull $ModName))
$modsRootPrefix = $modsRootFull.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (!$destinationFull.StartsWith($modsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path -Leaf $destinationFull) -ne $ModName)
{
    throw "Refusing to deploy outside the configured Mods folder: $destinationFull"
}

$runtimeFiles = @(
    'EndlessShapesUnlimited.dll',
    '0Harmony.dll',
    'plugin.json',
    'header.header',
    'header.jpg',
    'LICENSE',
    'README.md',
    'releases',
    'THIRD_PARTY_NOTICES.md'
)
$runtimeDirectories = @(
    'Assets',
    'Character Items',
    'Items',
    'Meshes'
)

if (Test-Path -LiteralPath $destinationFull)
{
    Remove-Item -LiteralPath $destinationFull -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destinationFull | Out-Null

foreach ($file in $runtimeFiles)
{
    $from = Join-Path $source.Path $file
    if (!(Test-Path -LiteralPath $from))
    {
        throw "Missing runtime file: $from"
    }

    Copy-Item -LiteralPath $from -Destination $destinationFull -Force
}

foreach ($directory in $runtimeDirectories)
{
    $from = Join-Path $source.Path $directory
    if (!(Test-Path -LiteralPath $from))
    {
        throw "Missing runtime directory: $from"
    }

    Copy-Item -LiteralPath $from -Destination $destinationFull -Recurse -Force
}

$dlls = @(Get-ChildItem -LiteralPath $destinationFull -Recurse -Filter '*.dll')
$esuDlls = @($dlls | Where-Object { $_.Name -eq 'EndlessShapesUnlimited.dll' })
$unexpectedDlls = @($dlls | Where-Object {
        $_.Name -ne 'EndlessShapesUnlimited.dll' -and
        $_.Name -ne '0Harmony.dll'
    })

if ($esuDlls.Count -ne 1)
{
    throw "Expected exactly one deployed EndlessShapesUnlimited.dll, found $($esuDlls.Count)."
}

if ($unexpectedDlls.Count -ne 0)
{
    throw "Unexpected deployed DLL(s): $($unexpectedDlls.FullName -join ', ')"
}

if (Test-Path -LiteralPath (Join-Path $destinationFull 'Source'))
{
    throw "Clean runtime deploy must not contain the development Source folder."
}

Write-Host "Deployed clean EndlessShapesUnlimited runtime to $destinationFull"
Write-Host "DLL: $($esuDlls[0].Length) bytes, $($esuDlls[0].LastWriteTime)"
