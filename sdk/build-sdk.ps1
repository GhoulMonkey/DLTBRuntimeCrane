[CmdletBinding()]
param([string]$Version = '2.0.0')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = Join-Path $root 'package'
[IO.Directory]::CreateDirectory($output) | Out-Null
$zipPath = Join-Path $output "CRANE-Script-SDK-$Version.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

$required = @('README.md','CHANGELOG.md','.luarc.json','LICENSES\Lua-5.4.txt','docs\QUICKSTART.md','docs\BRIDGE-CONCEPTS.md','docs\CRANE-LUA-API.md','library\crane.lua','templates','examples','tools\validate.ps1','tools\package.ps1','tools\crane-lua-check.exe','schemas\bridge-catalog.json','schemas\script-metadata.json')
foreach ($item in $required) { if (-not (Test-Path -LiteralPath (Join-Path $root $item))) { throw "Missing SDK input: $item" } }

$craneSource = Get-Content -LiteralPath (Join-Path $root '..\asi\Crane.c') -Raw
$registered = @([regex]::Matches($craneSource, 'lua_pushcfunction\(g_lua,\s*lua_bridge_[a-z_]+\);\s*lua_setfield\(g_lua,\s*-2,\s*"([a-z_]+)"\)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$annotated = @([regex]::Matches((Get-Content -LiteralPath (Join-Path $root 'library\crane.lua') -Raw), 'function CraneBridge\.([a-z_]+)\(') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$missing = @($registered | Where-Object { $_ -notin $annotated })
$extra = @($annotated | Where-Object { $_ -notin $registered })
if ($registered.Count -ne 16 -or $missing.Count -or $extra.Count) { throw "Lua surface drift: registered=$($registered.Count), missing=$($missing -join ','), extra=$($extra -join ',')" }

foreach ($example in Get-ChildItem -LiteralPath (Join-Path $root 'examples') -Filter '*.lua') {
    & (Join-Path $root 'tools\validate.ps1') $example.FullName -RequireSyntaxCheck
    if ($LASTEXITCODE -ne 0) { throw "Example failed validation: $($example.Name)" }
}
foreach ($template in Get-ChildItem -LiteralPath (Join-Path $root 'templates') -Recurse -Filter '*.lua') {
    & (Join-Path $root 'tools\validate.ps1') $template.FullName -RequireSyntaxCheck
    if ($LASTEXITCODE -ne 0) { throw "Template failed validation: $($template.Name)" }
}

foreach ($fixture in @('invalid-metadata.lua','invalid-syntax.lua')) {
    & (Join-Path $root 'tools\validate.ps1') (Join-Path $root "tests\$fixture") -RequireSyntaxCheck
    if ($LASTEXITCODE -eq 0) { throw "Negative validator fixture was accepted: $fixture" }
}

$catalog = Get-Content -LiteralPath (Join-Path $root 'schemas\bridge-catalog.json') -Raw | ConvertFrom-Json
if ($catalog.bridgeAbi -ne 3 -or $catalog.paths.Count -lt 20 -or $catalog.events.Count -lt 9) { throw 'Bridge catalog sanity gate failed.' }

# The catalog is hand-maintained data ABOUT ANOTHER COMPONENT, and it is the part
# of this SDK most likely to be wrong. Two rounds of the external-author gate found
# two defects in it, and the second was introduced while fixing the first, by
# inventing a subject that looked right. Both were mechanically detectable, so a
# fabricated field should now fail the build rather than reach an author.
#
# Skipped when the Bridge source is absent, which is the normal case for an author
# who extracted the archive: this checks the catalog against something only the
# workspace has. A skip is announced rather than silent, because a gate that
# quietly does nothing is worse than no gate.
$catalogGate = Join-Path (Split-Path -Parent $root) 'tools\verify-sdk-catalog.py'
$bridgeSource = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) 'DLTBRuntimeBridge\src\Abi'
$api2 = Join-Path $bridgeSource 'Api2.c'
$api3 = Join-Path $bridgeSource 'Api3.c'
if ((Test-Path -LiteralPath $catalogGate) -and (Test-Path -LiteralPath $api2) -and (Test-Path -LiteralPath $api3)) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & $python.Source $catalogGate (Join-Path $root 'schemas\bridge-catalog.json') $api2 $api3
        if ($LASTEXITCODE -ne 0) { throw 'SDK catalog does not match the Bridge; see above.' }
    } else {
        Write-Host 'Catalog gate SKIPPED: python not found.'
    }
} else {
    Write-Host 'Catalog gate SKIPPED: Bridge source not present (normal outside the workspace).'
}

Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $files = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
            $_.FullName -notlike "$output*" -and $_.Name -notlike '*.zip' -and $_.Name -notlike '*.sha256.txt'
        } | Sort-Object FullName
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            $entry = $archive.CreateEntry("CRANE-Script-SDK-$Version/$relative", [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($file.FullName); $entryStream = $entry.Open()
            try { $input.CopyTo($entryStream) } finally { $entryStream.Dispose(); $input.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zipPath.sha256.txt", "$hash  $([IO.Path]::GetFileName($zipPath))`r`n", [Text.UTF8Encoding]::new($false))
Write-Host "Built $zipPath"
Write-Host "Lua calls: $($registered.Count); catalog: $($catalog.paths.Count) paths, $($catalog.events.Count) events"
Write-Host "SHA-256: $hash"
# The negative fixtures intentionally leave a native syntax-check process with
# exit code 1. They passed because that failure was expected; do not leak it as
# this build script's own process result after a successful archive build.
$global:LASTEXITCODE = 0
