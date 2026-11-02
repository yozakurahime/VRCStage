param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$labProject = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$labLogs = Join-Path $labProject 'Logs'
New-Item -ItemType Directory -Force -Path $labLogs | Out-Null
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity não encontrada em $UnityPath" }
$labArgs = @('-batchmode', '-nographics', '-projectPath', ('"' + $labProject + '"'), '-executeMethod', 'AvatarLab.Editor.LabBuild.Build', '-logFile', ('"' + (Join-Path $labLogs 'build.log') + '"'))
$labBuildProcess = Start-Process -FilePath $UnityPath -ArgumentList $labArgs -WindowStyle Hidden -PassThru
Write-Host "Compilando. Log: $(Join-Path $labLogs 'build.log')"
$labBuildProcess.WaitForExit()
if ($labBuildProcess.ExitCode -ne 0) { throw "Build falhou. Consulte o log." }
Write-Host "Build concluído: $(Join-Path $labProject 'Build')"
