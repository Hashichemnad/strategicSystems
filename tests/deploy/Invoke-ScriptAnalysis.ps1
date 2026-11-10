<#
.SYNOPSIS
  PSScriptAnalyzer (MIT) on the deploy scripts and their tests (ADR 0163): syntax compatible with Windows PowerShell 5.1 and PowerShell 7,
  commands and parameters available in Windows PowerShell 5.1, plus the analyzer's error-level rules. Exit code 1 on any finding.
.EXAMPLE
  pwsh -NoProfile -File tests/deploy/Invoke-ScriptAnalysis.ps1
#>
param([string]$Version = '1.23.0')
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$module = Get-Module -ListAvailable PSScriptAnalyzer | Where-Object { $_.Version -eq [version]$Version } | Select-Object -First 1
if (-not $module) {
    Install-Module PSScriptAnalyzer -RequiredVersion $Version -Scope CurrentUser -Force -AllowClobber
    $module = Get-Module -ListAvailable PSScriptAnalyzer | Where-Object { $_.Version -eq [version]$Version } | Select-Object -First 1
}
Import-Module $module.Path

# The Windows PowerShell 5.1 command profile shipped with the analyzer (Windows Server 2019, .NET Framework 4.x).
$profiles = Join-Path $module.ModuleBase 'compatibility_profiles'
$target = Get-ChildItem $profiles -Filter 'win-8_x64_*_5.1.*_framework.json' | Select-Object -First 1
if (-not $target) { throw "No Windows PowerShell 5.1 profile in $profiles." }
Write-Host "PSScriptAnalyzer $($module.Version), command profile $($target.BaseName)"

$settings = @{
    IncludeDefaultRules = $true
    Severity = @('Error')
    Rules = @{
        PSUseCompatibleSyntax = @{ Enable = $true; TargetVersions = @('5.1', '7.0') }
        PSUseCompatibleCommands = @{ Enable = $true; TargetProfiles = @($target.BaseName) }
    }
}
$compatibilityRules = @('PSUseCompatibleSyntax', 'PSUseCompatibleCommands')

$files = @(Get-ChildItem (Join-Path $repo 'deploy') -Filter '*.ps1') + @(Get-ChildItem $PSScriptRoot -Filter '*.ps1') +
    @(Get-Item (Join-Path (Join-Path $repo 'tools') 'check-powershell.ps1'))
$findings = @()
foreach ($file in $files) {
    # Error-level default rules, and the compatibility rules at any severity.
    $findings += @(Invoke-ScriptAnalyzer -Path $file.FullName -Settings $settings)
    $findings += @(Invoke-ScriptAnalyzer -Path $file.FullName -IncludeRule $compatibilityRules -Settings @{ Rules = $settings.Rules })
}
$findings = @($findings | Sort-Object ScriptName, Line, RuleName, Message -Unique)
foreach ($f in $findings) { Write-Host "$($f.ScriptName):$($f.Line): [$($f.RuleName)] $($f.Message)" }
Write-Host "$($files.Count) scripts analysed, $($findings.Count) findings."
if ($findings.Count -gt 0) { exit 1 }
