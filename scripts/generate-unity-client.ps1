# Generate C# client code from Anchor IDL for Unity
# Requires: dotnet tool install -g Solana.Unity.Anchor.Tool

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$IdlPath = Join-Path $ProjectRoot "solana-program\target\idl\chaindepth.json"
$OutputPath = Join-Path $ProjectRoot "Assets\Scripts\Solana\Generated\ChainDepthClient.cs"

Write-Host "=== Generate Unity C# Client ===" -ForegroundColor Cyan
Write-Host "IDL: $IdlPath"
Write-Host "Output: $OutputPath"

# Check IDL exists
if (-not (Test-Path $IdlPath)) {
    Write-Host "Error: IDL not found at $IdlPath" -ForegroundColor Red
    Write-Host "Run 'anchor build' first to generate the IDL." -ForegroundColor Yellow
    exit 1
}

# Ensure output directory exists
$OutputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Generate C# client
Write-Host "Generating..." -ForegroundColor Yellow
dotnet anchorgen -i $IdlPath -o $OutputPath

if ($LASTEXITCODE -eq 0) {
    Write-Host "Success! Generated: $OutputPath" -ForegroundColor Green
} else {
    Write-Host "Error: Code generation failed" -ForegroundColor Red
    exit 1
}
