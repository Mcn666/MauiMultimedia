# PowerShell 测试样本
$AppName = 'MauiMultimedia'
$Version = '1.0.0'

Write-Host "Starting $AppName v$Version"

Get-ChildItem -File | Where-Object {
    $_.Extension -in '.txt', '.md', '.json'
} | ForEach-Object {
    Write-Host "Processing: $($_.FullName)"
}
