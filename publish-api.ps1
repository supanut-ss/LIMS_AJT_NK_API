Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "Publishing LIMS AJT NK API (Release Mode)..." -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan

$PublishDir = "./publish"

# 1. Clean old folder if exists
if (Test-Path $PublishDir) {
    Write-Host "Cleaning previous publish directory..." -ForegroundColor Yellow
    Remove-Item -Path $PublishDir -Recurse -Force
}

# 2. Run dotnet publish
Write-Host "Running dotnet publish..." -ForegroundColor Yellow
dotnet publish LIMS_AJT_NK_API.csproj -c Release -o $PublishDir

# 3. Check exit code
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "===================================================" -ForegroundColor Green
    Write-Host "[SUCCESS] API published successfully!" -ForegroundColor Green
    Write-Host "Path: $((Get-Item $PublishDir).FullName)" -ForegroundColor Green
    Write-Host "===================================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[ERROR] Publish failed! Please check the errors above." -ForegroundColor Red
    exit $LASTEXITCODE
}
