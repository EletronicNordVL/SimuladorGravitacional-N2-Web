# Encerra o servidor iniciado pelo Iniciar.ps1.
$ErrorActionPreference = 'Stop'
$runtimeDirectory = Join-Path $PSScriptRoot '.local-runtime'
$pidFile = Join-Path $runtimeDirectory 'server.pid'

try {
    # O servidor roda como "dotnet ...SimuladorGravitacionalN2_Web.dll" (dentro de um cmd oculto).
    $processos = Get-CimInstance Win32_Process |
        Where-Object { $_.CommandLine -like '*SimuladorGravitacionalN2_Web.dll*' -and $_.ProcessId -ne $PID }

    foreach ($processo in $processos) {
        Stop-Process -Id $processo.ProcessId -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile -Force }

    if (@($processos).Count -gt 0) { Write-Host 'Servidor local encerrado.' }
    else { Write-Host 'Nenhum servidor do simulador estava rodando.' }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
