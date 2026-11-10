# =============================================================================
# durdur.ps1 — Çalışan tüm mikroservisleri, API Gateway'i ve React dev sunucusunu durdurur
# Kullanım: .\durdur.ps1
# =============================================================================

$ports = @(5000, 5101, 5102, 5103, 5104, 5105, 5106, 5173)
foreach ($p in $ports) {
    $conns = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns) {
        Write-Host "Port $p üzerindeki süreç durduruluyor (PID: $($c.OwningProcess))..." -ForegroundColor Yellow
        Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue
    }
}
Write-Host "Tüm uygulama servisleri durduruldu." -ForegroundColor Green

