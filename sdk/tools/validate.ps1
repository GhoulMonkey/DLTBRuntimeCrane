[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Path,
    [string]$Catalog = (Join-Path (Split-Path -Parent $PSScriptRoot) 'schemas\bridge-catalog.json'),
    [switch]$RequireSyntaxCheck
)

$ErrorActionPreference = 'Stop'
$errors = [Collections.Generic.List[string]]::new()
$warnings = [Collections.Generic.List[string]]::new()

function Add-Error([string]$Message) { $script:errors.Add($Message) }
function Add-Warning([string]$Message) { $script:warnings.Add($Message) }

try { $resolved = (Resolve-Path -LiteralPath $Path).Path }
catch { Write-Error "Script not found: $Path"; exit 1 }

if ([IO.Path]::GetExtension($resolved) -cne '.lua') { Add-Error 'script filename must end in lowercase .lua' }
if ([IO.Path]::GetFileName($resolved) -ne (Split-Path -Leaf $resolved)) { Add-Error 'script must use a plain filename' }

try {
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $text = $utf8.GetString([IO.File]::ReadAllBytes($resolved))
} catch { Add-Error 'script is not valid UTF-8'; $text = '' }

if ($text.IndexOf([char]0) -ge 0) { Add-Error 'script contains a NUL byte' }
$lines = @($text -split "`r?`n")
$header = @($lines | Select-Object -First 60)
$name = $null
$description = $null
$params = @{}

function Split-Tokens([string]$Value) {
    $matches = [regex]::Matches($Value, '"[^"\r\n]*"|\S+')
    @($matches | ForEach-Object { $_.Value })
}

foreach ($line in $header) {
    if ($line -match '^\s*--\s*@name(?:\s+|\s*:\s*)(.+?)\s*$' -and -not $name) { $name = $Matches[1] }
    elseif ($line -match '^\s*--\s*@description(?:\s+|\s*:\s*)(.+?)\s*$' -and -not $description) { $description = $Matches[1] }
    elseif ($line -match '^\s*--\s*@param(?:\s+|\s*:\s*)(.+?)\s*$') {
        $tokens = @(Split-Tokens $Matches[1])
        if ($tokens.Count -lt 2) { Add-Error "invalid @param declaration: $line"; continue }
        $key = $tokens[0]
        $type = $tokens[1].ToLowerInvariant()
        if ($key.Length -gt 40) { Add-Error "parameter '$key' exceeds 40 characters" }
        if ($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Add-Warning "parameter '$key' should use ASCII letters, digits and underscores" }
        $folded = $key.ToLowerInvariant()
        if ($params.ContainsKey($folded)) { Add-Error "duplicate parameter '$key' (keys are case-insensitive)"; continue }
        if ($type -notin @('number','bool','boolean','string','enum')) { Add-Error "parameter '$key' has unsupported type '$type'"; continue }
        $options = @{}
        foreach ($token in $tokens[2..($tokens.Count - 1)]) {
            if ($token -notmatch '^([^=]+)=(.*)$') { continue }
            $option = $Matches[1].ToLowerInvariant()
            $value = $Matches[2]
            if ($value.StartsWith('"') -and $value.EndsWith('"')) { $value = $value.Substring(1, $value.Length - 2) }
            if ($option -notin @('default','min','max','values','label','group','desc','description')) { Add-Warning "parameter '$key' has unknown option '$option'" }
            $options[$option] = $value
        }
        if ($type -eq 'enum') {
            if (-not $options.ContainsKey('values') -or [string]::IsNullOrWhiteSpace($options.values)) { Add-Error "enum '$key' requires values=a|b" }
            elseif ($options.ContainsKey('default') -and $options.default -notin @($options.values -split '\|')) { Add-Error "enum '$key' default is not in values" }
        }
        if ($type -eq 'number') {
            $minimum = $null; $maximum = $null; $default = $null
            if ($options.ContainsKey('min')) { try { $minimum = [double]::Parse($options.min, [Globalization.CultureInfo]::InvariantCulture) } catch { Add-Error "number '$key' has invalid min" } }
            if ($options.ContainsKey('max')) { try { $maximum = [double]::Parse($options.max, [Globalization.CultureInfo]::InvariantCulture) } catch { Add-Error "number '$key' has invalid max" } }
            if ($options.ContainsKey('default')) { try { $default = [double]::Parse($options.default, [Globalization.CultureInfo]::InvariantCulture) } catch { Add-Error "number '$key' has invalid default" } }
            if ($null -ne $minimum -and $null -ne $maximum -and $minimum -gt $maximum) { Add-Error "number '$key' has min greater than max" }
            if ($null -ne $default -and $null -ne $minimum -and $default -lt $minimum) { Add-Error "number '$key' default is below min" }
            if ($null -ne $default -and $null -ne $maximum -and $default -gt $maximum) { Add-Error "number '$key' default is above max" }
        }
        if ($type -in @('bool','boolean') -and $options.ContainsKey('default') -and $options.default -notin @('true','false','0','1')) { Add-Error "boolean '$key' has invalid default" }
        $params[$folded] = $true
    }
}

if ([string]::IsNullOrWhiteSpace($name)) { Add-Error 'missing @name in the first 60 lines' }
if ([string]::IsNullOrWhiteSpace($description)) { Add-Error 'missing @description in the first 60 lines' }
foreach ($library in @('os','io','debug','package')) {
    if ($text -match "(?m)(^|[^A-Za-z0-9_])$library\s*[\[\.]" -or $text -match "\brequire\s*\(") { Add-Error "uses unavailable Lua library/function '$library'" }
}
if ($text -match '\bbridge\.set\s*\(') { Add-Warning 'uses unmanaged bridge.set; prefer a lease when restoration is wanted' }
$mutates = $text -match '\bbridge\.(set|claim|lease_write|modifier_acquire|modifier_write)\s*\('
if ($mutates -and $text -notmatch '(?i)(AllowWrites|write access|writes enabled)') { Add-Warning 'script mutates state but does not document that CRANE write access is required' }

if (Test-Path -LiteralPath $Catalog) {
    $catalogData = Get-Content -LiteralPath $Catalog -Raw | ConvertFrom-Json
    $known = @{}; foreach ($entry in $catalogData.paths) { $known[$entry.name] = $true }
    foreach ($match in [regex]::Matches($text, 'bridge\.(?:get|set|claim|modifier_acquire|describe)\s*\(\s*"([^"]+)"')) {
        $pathName = $match.Groups[1].Value
        $dynamic = $pathName -match '^(var|engine|diag|interaction)\.' -or $pathName -eq 'hunger.drain_multiplier'
        if (-not $known.ContainsKey($pathName) -and -not $dynamic) { Add-Warning "literal Bridge path is absent from the bundled core catalog: $pathName" }
    }
}

$checker = Join-Path $PSScriptRoot 'crane-lua-check.exe'
$checkerArgs = @($resolved)
if (-not (Test-Path -LiteralPath $checker)) {
    $command = Get-Command luac -ErrorAction SilentlyContinue
    if ($command) { $checker = $command.Source; $checkerArgs = @('-p', $resolved) }
}
if (Test-Path -LiteralPath $checker) {
    & $checker @checkerArgs
    if ($LASTEXITCODE -ne 0) { Add-Error 'Lua 5.4 syntax check failed' }
} elseif ($RequireSyntaxCheck) { Add-Error 'Lua 5.4 syntax checker was required but crane-lua-check.exe was not found' }
else { Add-Warning 'Lua syntax was not checked because crane-lua-check.exe was not found' }

foreach ($warning in $warnings) { Write-Warning $warning }
foreach ($problem in $errors) { Write-Host "ERROR: $problem" -ForegroundColor Red }
if ($errors.Count -gt 0) { Write-Host "Validation failed: $($errors.Count) error(s), $($warnings.Count) warning(s)."; exit 1 }
Write-Host "Validation passed: $([IO.Path]::GetFileName($resolved)) ($($params.Count) parameter(s), $($warnings.Count) warning(s))."
exit 0
