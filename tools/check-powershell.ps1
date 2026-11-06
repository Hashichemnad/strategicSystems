# Parses every PowerShell script in a folder and fails on syntax errors (CI: deploy scripts, ADR 0157).
param([Parameter(Mandatory = $true)][string]$Folder)
$failed = $false
foreach ($file in Get-ChildItem (Join-Path $Folder '*.ps1')) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors) {
        $failed = $true
        foreach ($e in $errors) { Write-Host "$($file.Name):$($e.Extent.StartLineNumber): $($e.Message)" }
    } else {
        Write-Host "$($file.Name): ok"
    }
}
if ($failed) { exit 1 }
