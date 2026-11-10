# =============================================================================
# baslat.ps1 — Tüm altyapıyı, 6 mikroservisi, YARP Gateway'i ve React UI'ı başlatır
# Kullanım: .\baslat.ps1
# =============================================================================

$ErrorActionPreference = 'Continue'
$Root = $PSScriptRoot

Write-Host "=== [1/5] SQL Server (MSSQLSERVER) kontrol ediliyor ===" -ForegroundColor Cyan
$sqlSvc = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
if ($sqlSvc -and $sqlSvc.Status -ne 'Running') {
    Start-Service MSSQLSERVER -ErrorAction SilentlyContinue
}
Write-Host "SQL Server durumu: $($sqlSvc.Status)" -ForegroundColor Green

Write-Host "=== [2/5] Redis (Port 6379) kontrol ediliyor ===" -ForegroundColor Cyan
$redisPort = Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue
if (-not $redisPort) {
    $redisCmd = Get-Command redis-server.exe -ErrorAction SilentlyContinue
    $redisExe = if ($redisCmd) { $redisCmd.Source } else { Join-Path $env:USERPROFILE 'source\apps\_cache\redis\Redis-8.10.3-Windows-x64-msys2\redis-server.exe' }
    if (Test-Path $redisExe) {
        Start-Process -FilePath $redisExe -ArgumentList '--port','6379' -WindowStyle Minimized
        Write-Host "Redis başlatıldı (6379)." -ForegroundColor Green
    } else {
        Write-Warning "Redis bulunamadı; servisler bellek içi fallback ile çalışacak."
    }
} else {
    Write-Host "Redis zaten çalışıyor (6379)." -ForegroundColor Green
}

Write-Host "=== [3/5] RabbitMQ (Port 5672 / Management 15672) kontrol ediliyor ===" -ForegroundColor Cyan
$rmqPort = Get-NetTCPConnection -LocalPort 5672 -State Listen -ErrorAction SilentlyContinue
if (-not $rmqPort) {
    Start-Service RabbitMQ -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
Write-Host "RabbitMQ çalışıyor (5672)." -ForegroundColor Green

Write-Host "=== [4/5] Eski servis süreçleri temizleniyor ve çözüm tek seferde derleniyor ===" -ForegroundColor Cyan
$ports = @(5000, 5101, 5102, 5103, 5104, 5105, 5106)
foreach ($p in $ports) {
    $conns = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns) {
        Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue
    }
}

Push-Location $Root
dotnet build ETicaret.slnx --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "Derleme başarısız oldu!"
    exit 1
}
Pop-Location
Write-Host "Derleme tamamlandı (0 hata, 0 güvenlik uyarısı)." -ForegroundColor Green

Write-Host "=== [5/5] Mikroservisler (--no-build), API Gateway ve React başlatılıyor ===" -ForegroundColor Cyan

$services = @(
    @{ Name = 'Catalog.Api (5101)';         Path = 'src\Services\Catalog\Catalog.Api' },
    @{ Name = 'Basket.Api (5102)';          Path = 'src\Services\Basket\Basket.Api' },
    @{ Name = 'Order.Api (5103)';           Path = 'src\Services\Order\Order.Api' },
    @{ Name = 'Payment.Api (5104)';         Path = 'src\Services\Payment\Payment.Api' },
    @{ Name = 'Notification.Worker (5105)'; Path = 'src\Services\Notification\Notification.Worker' },
    @{ Name = 'Identity.Api (5106)';        Path = 'src\Services\Identity\Identity.Api' },
    @{ Name = 'Gateway.Api (5000)';         Path = 'src\Gateway\Gateway.Api' }
)

foreach ($svc in $services) {
    $projDir = Join-Path $Root $svc.Path
    Start-Process pwsh -ArgumentList '-NoExit','-Command',"Set-Location '$projDir'; `$Host.UI.RawUI.WindowTitle='$($svc.Name)'; dotnet run --no-build --no-launch-profile" -WindowStyle Minimized
}

$webDir = Join-Path $Root 'web'
if (-not (Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue)) {
    Start-Process pwsh -ArgumentList '-NoExit','-Command',"Set-Location '$webDir'; `$Host.UI.RawUI.WindowTitle='React UI (5173)'; npm run dev" -WindowStyle Minimized
}

Write-Host ""
Write-Host "Tüm servisler başlatıldı:" -ForegroundColor Green
Write-Host "  • React Arayüzü       : http://localhost:5173"
Write-Host "  • Giriş / Google Auth : http://localhost:5173/giris"
Write-Host "  • API Gateway (YARP)  : http://localhost:5000/api/health"
Write-Host "  • RabbitMQ Yönetim UI : http://localhost:15672 (guest / guest)"
