param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$root =
    Split-Path -Parent $PSScriptRoot

$project =
    Join-Path $root "LicenseService\LicenseService.csproj"

$output =
    Join-Path $root "publish"

dotnet publish `
    $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $output

Write-Host "Published to: $output"
