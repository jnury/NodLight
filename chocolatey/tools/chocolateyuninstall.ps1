$ErrorActionPreference = 'Stop'

[array]$keys = Get-UninstallRegistryKey -SoftwareName 'NodLight'

if ($keys.Count -eq 0) {
  Write-Warning "NodLight is not in Programs and Features; it may have been uninstalled already."
  return
}

foreach ($key in $keys) {
  Uninstall-ChocolateyPackage -PackageName $env:ChocolateyPackageName -FileType 'msi' `
    -SilentArgs "$($key.PSChildName) /qn /norestart" -ValidExitCodes @(0, 3010, 1605, 1614, 1641)
}
