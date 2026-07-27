#Requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BinaryName  = 'ce'
$ProjectDir  = Join-Path $PSScriptRoot 'src\CodeEdit.Presentation'
$PublishDir  = Join-Path $PSScriptRoot 'publish\win-x64'
$InstallDir  = Join-Path $env:LOCALAPPDATA 'Programs\code-edit'

Write-Host "Building $BinaryName..."
dotnet publish $ProjectDir `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=none `
  --output $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Force "$PublishDir\CodeEdit.Presentation.exe" "$InstallDir\$BinaryName.exe"

# Add InstallDir to the user PATH if not already present
$pathKey   = 'HKCU:\Environment'
$current   = (Get-ItemProperty -Path $pathKey -Name Path -ErrorAction SilentlyContinue).Path ?? ''
$pathParts = $current -split ';' | Where-Object { $_ -ne '' }

if ($pathParts -notcontains $InstallDir) {
    $newPath = ($pathParts + $InstallDir) -join ';'
    Set-ItemProperty -Path $pathKey -Name Path -Value $newPath
    # Notify running shells of the environment change
    $signature = @'
[DllImport("user32.dll", SetLastError=true)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@
    $type   = Add-Type -MemberDefinition $signature -Name WinAPI -Namespace Env -PassThru
    $result = [UIntPtr]::Zero
    $type::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
    Write-Host "Added to user PATH: $InstallDir"
    Write-Host "Restart your terminal for PATH changes to take effect."
}

Write-Host "Installed: $InstallDir\$BinaryName.exe"
