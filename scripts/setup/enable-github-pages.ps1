param(
  [string]$Token = $env:GITHUB_PERSONAL_ACCESS_TOKEN
)
$ErrorActionPreference = 'Stop'
if (-not $Token) {
    Write-Error "No token provided. Pass -Token or set GITHUB_PERSONAL_ACCESS_TOKEN."
    exit 1
}
$headers = @{ Authorization = "token $Token"; Accept = 'application/vnd.github+json' }

# Enable Pages from main branch, /docs folder
$body = @{ source = @{ branch = 'main'; path = '/docs' } } | ConvertTo-Json -Compress -Depth 3

try {
    $resp = Invoke-RestMethod -Uri 'https://api.github.com/repos/Kadenvh/adze-cad/pages' -Method Post -Headers $headers -Body $body -ContentType 'application/json'
    Write-Host "Pages enabled: $($resp.html_url)"
} catch {
    $msg = $_.Exception.Message
    if ($msg -match '409') {
        # Already exists — update instead
        $resp = Invoke-RestMethod -Uri 'https://api.github.com/repos/Kadenvh/adze-cad/pages' -Method Put -Headers $headers -Body $body -ContentType 'application/json'
        Write-Host "Pages updated."
    } else {
        throw
    }
}

# Fetch final state
$state = Invoke-RestMethod -Uri 'https://api.github.com/repos/Kadenvh/adze-cad/pages' -Method Get -Headers $headers
Write-Host "url:    $($state.html_url)"
Write-Host "status: $($state.status)"
Write-Host "source: $($state.source.branch)/$($state.source.path)"
