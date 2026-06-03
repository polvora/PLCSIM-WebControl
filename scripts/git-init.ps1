# git-init.ps1 - Initializes the local git repository and makes the first commit.
# Run this AFTER installing Git for Windows (https://git-scm.com/download/win).
# It does NOT push anything; it prints the exact commands to publish to GitHub at the end.

param(
    [string]$AuthorName  = "Marcelo Tapia",
    [string]$AuthorEmail = "t.marcelo.p@gmail.com"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git is not installed. Get it from https://git-scm.com/download/win and re-run this script."
}

if (-not (Test-Path (Join-Path $root ".git"))) {
    git init | Out-Null
    git symbolic-ref HEAD refs/heads/main 2>$null
}
git config user.name  $AuthorName
git config user.email $AuthorEmail
git add -A
git commit -m "Initial public release (v1.0.0)" | Out-Null

Write-Host "Local repository ready with the initial commit." -ForegroundColor Green
Write-Host ""
Write-Host "To publish to GitHub:" -ForegroundColor Cyan
Write-Host "  1) Create a new EMPTY repository on github.com (no README/license/.gitignore)."
Write-Host "  2) Then run, from this folder:"
Write-Host ""
Write-Host "     git remote add origin https://github.com/<your-user>/PLCSIM-WebControl.git"
Write-Host "     git push -u origin main"
Write-Host ""
Write-Host "  3) Create a Release (tag v1.0.0) and attach dist\PLCSIM-WebControl-1.0.0.zip"
Write-Host "     (build it with scripts\package.ps1 if it isn't there)."
