# Parses every PowerShell script in a folder and fails on syntax errors (CI: deploy scripts, ADR 0157), and on a non-ASCII byte in a
# script without a UTF-8 BOM: Windows PowerShell 5.1 reads such a script in the ANSI code page (ADR 0163).
param([Parameter(Mandatory = $true)][string]$Folder)
$failed = $false
foreach ($file in Get-ChildItem (Join-Path $Folder '*.ps1')) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = -1
    if (-not $hasBom) {
        for ($i = 0; $i -lt $bytes.Length; $i++) { if ($bytes[$i] -gt 0x7F) { $nonAscii = $i; break } }
    }
    if ($errors) {
        $failed = $true
        foreach ($e in $errors) { Write-Host "$($file.Name):$($e.Extent.StartLineNumber): $($e.Message)" }
    } elseif ($nonAscii -ge 0) {
        $failed = $true
        $line = 1
        for ($i = 0; $i -lt $nonAscii; $i++) { if ($bytes[$i] -eq 0x0A) { $line++ } }
        Write-Host "$($file.Name):$($line): non-ASCII character in a script without BOM (Windows PowerShell 5.1 would misread it)"
    } else {
        Write-Host "$($file.Name): ok"
    }
}
if ($failed) { exit 1 }
