param([switch]$NoBrowser)

# Compila e inicia o simulador N2 em segundo plano e abre o navegador.
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$localUrl = 'http://localhost:5080'
$projectFile = Join-Path $projectDirectory 'SimuladorGravitacionalN2_Web.csproj'
$runtimeDirectory = Join-Path $projectDirectory '.local-runtime'
$applicationDirectory = Join-Path $runtimeDirectory 'app'
$applicationFile = Join-Path $applicationDirectory 'SimuladorGravitacionalN2_Web.dll'
$pidFile = Join-Path $runtimeDirectory 'server.pid'

function Test-LocalApplication {
    try { return (Invoke-RestMethod -Uri "$localUrl/api/health" -TimeoutSec 2).status -eq 'ok' }
    catch { return $false }
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'O SDK .NET 10 precisa estar instalado para preparar o projeto.'
    }

    if (Test-LocalApplication) {
        Write-Host "O simulador ja esta rodando em $localUrl. Para reiniciar, use Parar.cmd e depois Iniciar.cmd."
        if (-not $NoBrowser) { Start-Process $localUrl }
        exit 0
    }

    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null

    # Compila sempre, para o servidor usar a versao mais recente do codigo.
    Write-Host 'Preparando o simulador gravitacional local...'
    & dotnet publish $projectFile -c Release --nologo -o $applicationDirectory
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao falhou. Consulte as mensagens acima.' }

    # Inicia o servidor em uma janela propria e oculta, para ele continuar rodando depois que
    # esta janela fechar. A pasta de trabalho e a do projeto (onde fica o wwwroot).
    $log = Join-Path $runtimeDirectory 'server.log'
    $errorLog = Join-Path $runtimeDirectory 'server-error.log'
    $command = '/c ""dotnet" "' + $applicationFile + '" 1>"' + $log + '" 2>"' + $errorLog + '""'
    $server = Start-Process -FilePath 'cmd.exe' -ArgumentList $command `
        -WorkingDirectory $projectDirectory -WindowStyle Hidden -PassThru
    [System.IO.File]::WriteAllText($pidFile, [string]$server.Id)

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if (Test-LocalApplication) { $ready = $true; break }
        if ($server.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) {
        throw 'O servidor nao respondeu na porta 5080. Consulte .local-runtime\server.log e server-error.log.'
    }

    Write-Host "Simulador pronto em $localUrl. O servidor continua ativo em segundo plano (use Parar.cmd para encerrar)."
    if (-not $NoBrowser) { Start-Process $localUrl }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
