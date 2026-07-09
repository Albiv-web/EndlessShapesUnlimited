[CmdletBinding()]
param(
    [string]$SourceRoot = '',
    [string]$ModsRoot = (Join-Path $env:USERPROFILE 'Documents\From The Depths\Mods'),
    [string]$ModName = 'EndlessShapesUnlimited',
    [string[]]$AdditionalModsRoots = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SourceRoot))
{
    $SourceRoot = Join-Path $PSScriptRoot '..\EndlessShapesUnlimited'
}

$source = Resolve-Path -LiteralPath $SourceRoot

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

function Resolve-FullPathText {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        return $null
    }

    return [System.IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($Path))
}

function Get-KnownModsRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $roots.Add($ModsRoot)
    $documents = [Environment]::GetFolderPath('MyDocuments')
    if (![string]::IsNullOrWhiteSpace($documents))
    {
        $roots.Add((Join-Path $documents 'From The Depths\Mods'))
    }

    if (![string]::IsNullOrWhiteSpace($env:USERPROFILE))
    {
        $roots.Add((Join-Path $env:USERPROFILE 'Documents\From The Depths\Mods'))
        $roots.Add((Join-Path $env:USERPROFILE 'OneDrive\Dokumenter\From The Depths\Mods'))
    }

    foreach ($root in $AdditionalModsRoots)
    {
        $roots.Add($root)
    }

    $seen = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $roots)
    {
        $full = Resolve-FullPathText $root
        if ($null -ne $full -and $seen.Add($full))
        {
            $full
        }
    }
}

function Deploy-ToModsRoot {
    param(
        [string]$ModsRootFull,
        [bool]$CreateRoot
    )

    if ($CreateRoot)
    {
        $modsRootItem = New-Item -ItemType Directory -Force -Path $ModsRootFull
        $modsRootFull = [System.IO.Path]::GetFullPath($modsRootItem.FullName)
    }
    elseif (!(Test-Path -LiteralPath $ModsRootFull -PathType Container))
    {
        Write-Host "Skipping missing Mods root $ModsRootFull"
        return $null
    }
    else
    {
        $modsRootFull = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ModsRootFull).Path)
    }

    $destinationFull = [System.IO.Path]::GetFullPath((Join-Path $modsRootFull $ModName))
    if (!$CreateRoot -and !(Test-Path -LiteralPath $destinationFull -PathType Container))
    {
        Write-Host "Skipping Mods root without existing $ModName runtime $ModsRootFull"
        return $null
    }

    $modsRootPrefix = $modsRootFull.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (!$destinationFull.StartsWith($modsRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $destinationFull) -ne $ModName)
    {
        throw "Refusing to deploy outside the configured Mods folder: $destinationFull"
    }

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

    $pluginPath = Join-Path $destinationFull 'plugin.json'
    $plugin = Get-Content -LiteralPath $pluginPath -Raw | ConvertFrom-Json
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $esuDlls[0].FullName).Hash
    Write-Host "Deployed clean EndlessShapesUnlimited runtime to $destinationFull"
    Write-Host "DLL: $($esuDlls[0].Length) bytes, $($esuDlls[0].LastWriteTime), SHA256 $hash, plugin.json version $($plugin.version)"

    [pscustomobject]@{
        ModsRoot = $modsRootFull
        Destination = $destinationFull
        Dll = $esuDlls[0].FullName
        Hash = $hash
        Version = [string]$plugin.version
    }
}

$knownRoots = @(Get-KnownModsRoots)
$deployments = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $knownRoots.Count; $index++)
{
    $deployment = Deploy-ToModsRoot -ModsRootFull $knownRoots[$index] -CreateRoot:($index -eq 0)
    if ($null -ne $deployment)
    {
        $deployments.Add($deployment)
    }
}

if ($deployments.Count -lt 1)
{
    throw 'No EndlessShapesUnlimited runtime folders were deployed.'
}

$hashes = @($deployments | Select-Object -ExpandProperty Hash -Unique)
$versions = @($deployments | Select-Object -ExpandProperty Version -Unique)
if ($hashes.Count -ne 1)
{
    throw "Deployed DLL hashes differ: $($hashes -join ', ')"
}

if ($versions.Count -ne 1)
{
    throw "Deployed plugin.json versions differ: $($versions -join ', ')"
}

Write-Host "All deployed EndlessShapesUnlimited runtime folders match."
Write-Host "version=$($versions[0])"
Write-Host "dll_sha256=$($hashes[0])"
