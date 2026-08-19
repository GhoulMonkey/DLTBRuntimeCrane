[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)] [string]$Path,
    [Parameter(Mandatory=$true)] [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')] [string]$Version,
    [string]$OutputDirectory = (Get-Location).Path
)
$ErrorActionPreference = 'Stop'
$scriptPath = (Resolve-Path -LiteralPath $Path).Path
& (Join-Path $PSScriptRoot 'validate.ps1') $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'Validation failed; no package was created.' }

$file = [IO.Path]::GetFileName($scriptPath)
$base = [IO.Path]::GetFileNameWithoutExtension($file) -replace '[^A-Za-z0-9._-]', '-'
if ([string]::IsNullOrWhiteSpace($base)) { throw 'Script filename does not produce a usable package name.' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$zipPath = Join-Path $OutputDirectory "$base-$Version.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $entry = $archive.CreateEntry("ph_ft/work/bin/x64/scripts/$file", [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($scriptPath)
        $output = $entry.Open()
        try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zipPath.sha256.txt", "$hash  $([IO.Path]::GetFileName($zipPath))`r`n", [Text.UTF8Encoding]::new($false))
Write-Host "Created $zipPath"
Write-Host "SHA-256: $hash"

