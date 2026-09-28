param(
    [Parameter(Mandatory=$true)]
    [string]$PublishDirectory,

    [string]$ServiceName = "CWLicenseService"
)

$exe = Join-Path $PublishDirectory "LicenseService.exe"

if (-not (Test-Path $exe)) {
    throw "LicenseService.exe not found: $exe"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service already exists: $ServiceName"
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName "CW License Service" `
    -StartupType Automatic

Start-Service $ServiceName

Get-Service $ServiceName
