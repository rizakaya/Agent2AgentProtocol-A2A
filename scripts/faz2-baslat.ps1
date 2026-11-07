# =====================================================================
# Faz 2'deki tüm tedarikçileri ayrı pencerelerde başlatır.
#
# Kullanım (proje kök klasöründen):
#   powershell -ExecutionPolicy Bypass -File scripts\faz2-baslat.ps1
#
# Sonra Satınalma Ajanını çalıştır:
#   dotnet run --project src\Purchasing.Agent
# =====================================================================

$root = Split-Path $PSScriptRoot -Parent

# Önce bir kez derle. Aynı projeyi iki pencerede aynı anda derlemeye çalışmak
# Windows'ta dosya kilidi hatası verir; bu yüzden pencereler --no-build ile açılır.
Write-Host "Derleniyor..."
dotnet build (Join-Path $root 'A2AFactory.slnx')
if ($LASTEXITCODE -ne 0) { Write-Host "Derleme basarisiz." -ForegroundColor Red; exit 1 }

function Start-Agent([string]$title, [string]$project, [string]$runArgs) {
    $dir = Join-Path $root "src\$project"
    $cmd = "`$host.UI.RawUI.WindowTitle = '$title'; dotnet run $runArgs"
    Start-Process powershell -WorkingDirectory $dir -ArgumentList '-NoExit', '-Command', $cmd
    Write-Host "Baslatildi: $title"
}

Start-Agent 'Celik AS - 5101'             'Supplier.Agent' '--no-build --launch-profile CelikAS'
Start-Agent 'Baglanti Ltd - 5102'         'Supplier.Agent' '--no-build --launch-profile BaglantiLtd'
Start-Agent 'RawSupplier (Faz 1) - 5100' 'RawSupplier'    '--no-build'

Write-Host ""
Write-Host "Tedarikciler birkac saniye icinde hazir olur."
Write-Host "Satinalma Ajani icin:  dotnet run --project src\Purchasing.Agent"
