<#
.SYNOPSIS
  Builds a release of PC Spec Overlay: runs the tests, publishes the single-file setup, signs it when a
  certificate is available, and writes release notes containing the SHA-256 the in-app updater requires.

.EXAMPLE
  .\build-release.ps1                                  # checks the repository set in PCSpecOverlay.csproj
  .\build-release.ps1 -CertificateThumbprint ABC123...
  .\build-release.ps1 -PfxPath cert.pfx               # asks for the password
  .\build-release.ps1 -UpdateRepository "owner/name"  # a fork checking its own releases

  Then create a GitHub release tagged v<version>, attach artifacts\release\PCSpecOverlay-Setup-<version>.exe,
  and paste artifacts\release\release-notes.md as the description.
#>
param(
    # GitHub "owner/name" the app should check for updates; empty uses the one in PCSpecOverlay.csproj
    [string]$UpdateRepository = "",
    # Code-signing certificate in your certificate store (Cert:\CurrentUser\My)
    [string]$CertificateThumbprint = "",
    # Or a .pfx file (you'll be asked for its password; it is never stored)
    [string]$PfxPath = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$project = [xml](Get-Content PCSpecOverlay\PCSpecOverlay.csproj)
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
# An empty -p:UpdateRepository= would override the project's default, so pass it only when given
$repoArgs = if ($UpdateRepository) { @("-p:UpdateRepository=$UpdateRepository") } else { @() }
if (-not $UpdateRepository) {
    $UpdateRepository = ($project.Project.PropertyGroup.UpdateRepository | ForEach-Object { if ($_ -is [string]) { $_ } else { $_.'#text' } } | Where-Object { $_ } | Select-Object -First 1)
}
Write-Host "Building PC Spec Overlay $version" -ForegroundColor Green

if (-not $SkipTests) {
    dotnet test PCSpec.slnx -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed; no release was built." }
}

$out = "artifacts\release"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish PCSpecOverlay\PCSpecOverlay.csproj -c Release -p:PublishSingleFile=true @repoArgs -o "$out\publish" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
$setup = "$out\PCSpecOverlay-Setup-$version.exe"
Move-Item "$out\publish\PCSpecOverlay.exe" $setup
Remove-Item "$out\publish" -Recurse -Force

# Sign when a certificate is available
$signed = $false
if ($CertificateThumbprint -or $PfxPath) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -match 'x64' | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe wasn't found (install the Windows SDK)." }
    $args = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")
    if ($CertificateThumbprint) { $args += @("/sha1", $CertificateThumbprint) }
    else {
        $password = Read-Host "Password for $PfxPath" -AsSecureString
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($password))
        $args += @("/f", $PfxPath, "/p", $plain)
    }
    & $signtool.FullName @args $setup
    if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
    & $signtool.FullName verify /pa $setup | Out-Null
    $signed = $LASTEXITCODE -eq 0
}

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$size = (Get-Item $setup).Length
@"
## PC Spec Overlay $version

Run ``PCSpecOverlay-Setup-$version.exe`` to install or update (per-user, no administrator rights).
Requires Windows 10/11 64-bit and the .NET 8 Desktop Runtime.

SHA256: $hash
"@ | Set-Content "$out\release-notes.md" -Encoding UTF8

Write-Host ""
Write-Host "Setup:      $setup ($([math]::Round($size / 1MB, 1)) MB)"
Write-Host "SHA-256:    $hash"
Write-Host "Signed:     $(if ($signed) { 'yes' } else { 'no (no certificate given) - Windows will show an unknown-publisher warning' })"
Write-Host "Updates:    $(if ($UpdateRepository) { "checks github.com/$UpdateRepository" } else { 'not set up (no -UpdateRepository)' })"
Write-Host "Notes:      $out\release-notes.md (keep the SHA256 line: the in-app updater requires it)"
Write-Host "Next:       create a GitHub release tagged v$version, attach the setup, paste the notes."
