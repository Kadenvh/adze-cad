# Load .env file from repo root into current process environment.
# Skips blank lines and comments (#). Does not override existing vars.
param(
    [switch]$Force  # Override existing env vars
)

$envFile = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) ".env"

if (-not (Test-Path $envFile)) {
    return
}

foreach ($line in (Get-Content $envFile)) {
    $trimmed = $line.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
        continue
    }

    $eqIndex = $trimmed.IndexOf("=")
    if ($eqIndex -lt 1) { continue }

    $key = $trimmed.Substring(0, $eqIndex).Trim()
    $val = $trimmed.Substring($eqIndex + 1).Trim()

    # Strip surrounding quotes if present
    if (($val.StartsWith('"') -and $val.EndsWith('"')) -or
        ($val.StartsWith("'") -and $val.EndsWith("'"))) {
        $val = $val.Substring(1, $val.Length - 2)
    }

    if (-not $Force) {
        $existing = [System.Environment]::GetEnvironmentVariable($key)
        if ($existing) { continue }
    }

    [System.Environment]::SetEnvironmentVariable($key, $val, "Process")
}
