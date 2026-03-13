$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$schemaRoot = Join-Path $repoRoot "schemas"

if (-not (Test-Path $schemaRoot)) {
  throw "Schema directory not found: $schemaRoot"
}

$failed = $false

Get-ChildItem $schemaRoot -Recurse -Filter *.json | Sort-Object FullName | ForEach-Object {
  try {
    Get-Content $_.FullName -Raw | ConvertFrom-Json | Out-Null
    Write-Output ("OK " + $_.FullName)
  } catch {
    $failed = $true
    Write-Output ("FAIL " + $_.FullName + " :: " + $_.Exception.Message)
  }
}

if ($failed) {
  exit 1
}
