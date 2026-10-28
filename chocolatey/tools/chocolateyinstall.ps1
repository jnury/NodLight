$ErrorActionPreference = 'Stop'

# Chocolatey runs elevated, so install for every user (ALLUSERS=1) rather than the MSI's per-user default.
$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'msi'
  url64bit       = 'https://github.com/jnury/NodLight/releases/download/v1.0.2/NodLight-1.0.2-win-x64.msi'
  checksum64     = '8B4ADEB1EF251124CE1AF1831C83ECDB7C26ACC867D8A510A4A99A33EF221209'
  checksumType64 = 'sha256'
  softwareName   = 'NodLight'
  silentArgs     = "/qn /norestart ALLUSERS=1 /l*v `"$($env:TEMP)\$($env:ChocolateyPackageName).$($env:ChocolateyPackageVersion).MsiInstall.log`""
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
