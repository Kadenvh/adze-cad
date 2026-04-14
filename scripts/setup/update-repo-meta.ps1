param(
  [string]$Token = $env:GITHUB_PERSONAL_ACCESS_TOKEN
)
$ErrorActionPreference = 'Stop'
if (-not $Token) {
    Write-Error "No token provided. Pass -Token or set GITHUB_PERSONAL_ACCESS_TOKEN."
    exit 1
}
$headers = @{ Authorization = "token $Token"; Accept = 'application/vnd.github+json' }

$body = @{
  description = 'Native AI assistant for SOLIDWORKS - 18 typed tools, agentic loop, governed writes, 5 AI providers. Free and open source.'
  homepage    = 'https://kadenvh.github.io/adze-cad/'
} | ConvertTo-Json -Compress

$resp = Invoke-RestMethod -Uri 'https://api.github.com/repos/Kadenvh/adze-cad' -Method Patch -Headers $headers -Body $body -ContentType 'application/json'
Write-Host "description: $($resp.description)"
Write-Host "homepage:    $($resp.homepage)"

$topics = @{ names = @('solidworks','cad','ai-assistant','csharp','dotnet','solidworks-addin','mcp','agentic-ai') } | ConvertTo-Json -Compress
$tresp  = Invoke-RestMethod -Uri 'https://api.github.com/repos/Kadenvh/adze-cad/topics' -Method Put -Headers $headers -Body $topics -ContentType 'application/json'
Write-Host "topics:      $($tresp.names -join ', ')"
