Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$frontendPort = 5270
$desktopUrl = "http://127.0.0.1:$frontendPort/app/overblik"

$lanAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -notlike '127.*' -and
        $_.IPAddress -notlike '169.254.*' -and
        $_.PrefixOrigin -ne 'WellKnown' -and
        $_.InterfaceAlias -notmatch 'Loopback|vEthernet|WSL|Docker'
    } |
    Sort-Object -Property InterfaceMetric |
    Select-Object -First 1 -ExpandProperty IPAddress

Write-Host ''
Write-Host 'Lokale Workslip-links' -ForegroundColor White
Write-Host '---------------------' -ForegroundColor DarkGray
Write-Host "Computer: $desktopUrl" -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace([string]$lanAddress)) {
    Write-Warning 'Kunne ikke finde en LAN IPv4-adresse automatisk. Telefon-link vises derfor ikke.'
    Write-Host 'Telefonen skal være på samme Wi-Fi som computeren.'
    exit 0
}

$phoneUrl = "http://${lanAddress}:$frontendPort/app/overblik"
Write-Host "Telefon:  $phoneUrl" -ForegroundColor Cyan
Write-Host 'Telefon og computer skal være på samme lokale netværk/Wi-Fi.' -ForegroundColor DarkGray
Write-Host 'Hvis Windows Firewall spørger om Node.js/Vite, tillad adgang på private netværk.' -ForegroundColor DarkGray
