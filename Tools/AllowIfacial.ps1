param(
    [System.Net.IPAddress]$PhoneIP,
    [Parameter(Mandatory=$true)][string]$ProgramPath,
    [switch]$LocalNetwork,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$appFile = (Resolve-Path -LiteralPath $ProgramPath).Path
if ([IO.Path]::GetExtension($appFile) -ne '.exe') { throw 'Informe o executável do AvatarLab.' }
if (-not $LocalNetwork -and ($null -eq $PhoneIP -or $PhoneIP.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork)) { throw 'Informe o IPv4 local do iPhone ou use -LocalNetwork.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'A alteração desta regra exige PowerShell como administrador.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $suffix = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($appFile.ToLowerInvariant())))).Replace('-','').Substring(0,12) } finally { $sha.Dispose() }
$origin = if ($LocalNetwork) { 'LocalSubnet' } else { $PhoneIP.IPAddressToString }
$ruleName = 'AvatarLab iFacial UDP '+$suffix+' '+$(if ($LocalNetwork) { 'LAN' } else { $PhoneIP.IPAddressToString })
if ($Remove) {
    & netsh advfirewall firewall delete rule "name=$ruleName"
} else {
    & netsh advfirewall firewall delete rule "name=$ruleName" | Out-Null
    & netsh advfirewall firewall add rule "name=$ruleName" dir=in action=allow "program=$appFile" protocol=UDP localport=49983 "remoteip=$origin" profile=any enable=yes
}
if ($LASTEXITCODE -ne 0) { throw 'O Windows não aplicou a regra. Verifique a mensagem acima.' }
Write-Host ('Regra: '+$ruleName)
Write-Host ('Programa: '+$appFile)
Write-Host ('Origem permitida: '+$origin+' / destino UDP 49983 neste PC.')
