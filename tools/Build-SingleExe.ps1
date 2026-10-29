#requires -Version 7
<#
.SYNOPSIS
    Собирает единый SetupAssistant-Full-<версия>.exe: приложение + конфиг и дистрибутивы внутри одного файла.

.DESCRIPTION
    1. Публикует SetupAssistant и вспомогательный ExtensionUpdater (Release, автономные, без установки .NET).
    2. Удаляет из публикации старые версии .crx (ExtensionUpdater --prune-only, без сети). Этот инструмент в результат не входит.
    3. Упаковывает Config, Appendices и LatoFont в zip и дописывает его в конец SetupAssistant.exe
       вместе с подвалом «SAPAYLD1» + длина архива. Приложение читает эти данные само и распаковывает
       нужный файл в %ProgramData%\SetupAssistant\Payload только в момент, когда он понадобился.
    Файл Config\Config.json или дистрибутив рядом с exe имеют приоритет над встроенными.
    Если exe нужно подписать, подписывайте готовый exe: приложение находит данные и в подписанном файле.
    Результат — единственный файл SetupAssistant-Full-<версия>.exe (версия из <Version> в SetupAssistant.csproj): и мастер
    настройки, и обновление дистрибутивов (--update-distributions). После сборки прежние SetupAssistant-Full*.exe в выходной
    папке удаляются, чтобы там лежала только последняя версия; запущенную старую версию удалить не получится — будет предупреждение.

.PARAMETER OutputDir
    Куда положить результат (по умолчанию папка dist рядом со скриптом, в git она не попадает).
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('SetupAssistant-build-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$app = Join-Path $work 'app'
$updater = Join-Path $work 'updater'
$stage = Join-Path $work 'payload'
New-Item -ItemType Directory -Force $app, $updater, $stage | Out-Null

$version = (Select-String -Path "$root\src\SetupAssistant\SetupAssistant.csproj" -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value

# --- Индикатор сборки -------------------------------------------------------------------------------------------------------
# Шкала шагов, крутящийся индикатор с временем на долгих операциях, полоса Write-Progress и итоговая таблица. Если вывод
# перенаправлен в файл, вместо перерисовки строки печатаются обычные строки.

$totalSteps = 4
$interactive = (-not [Console]::IsOutputRedirected) -or ($env:SETUPASSISTANT_BUILD_PROGRESS -eq '1')
$buildTimer = [Diagnostics.Stopwatch]::StartNew()
$doneSteps = [Collections.Generic.List[object]]::new()
$currentStep = $null

function Format-Bar([int]$done, [int]$total, [int]$width = 28) {
    $filled = [int][math]::Floor($width * $done / $total)
    ('█' * $filled) + ('░' * ($width - $filled))
}

function Format-Elapsed([TimeSpan]$time) { '{0:00}:{1:00}' -f [int][math]::Floor($time.TotalMinutes), $time.Seconds }

function Start-Step([int]$number, [string]$title) {
    Complete-Step
    $script:currentStep = [pscustomobject]@{ Number = $number; Title = $title; Timer = [Diagnostics.Stopwatch]::StartNew() }
    Write-Host ''
    Write-Host ('[{0}]  шаг {1} из {2}: {3}' -f (Format-Bar ($number - 1) $totalSteps), $number, $totalSteps, $title) -ForegroundColor Cyan
    Write-Progress -Activity "Сборка SetupAssistant-Full $version" -Status "Шаг $number из ${totalSteps}: $title" -PercentComplete ([int](100 * ($number - 1) / $totalSteps))
}

function Complete-Step {
    if ($null -eq $script:currentStep) { return }
    $step = $script:currentStep
    $step.Timer.Stop()
    Write-Host ('      шаг {0} готов за {1}' -f $step.Number, (Format-Elapsed $step.Timer.Elapsed)) -ForegroundColor Green
    $script:doneSteps.Add([pscustomobject]@{ Number = $step.Number; Title = $step.Title; Elapsed = $step.Timer.Elapsed })
    $script:currentStep = $null
}

# Крутящийся индикатор: перерисовывает одну строку, пока выполняется условие. Символы только из ASCII, чтобы не ломаться в консоли cmd.
function Show-Spinner([string]$text, [scriptblock]$isRunning) {
    $frames = '|', '/', '-', '\'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $i = 0
    while (& $isRunning) {
        $line = '      [{0}] {1}  {2}' -f $frames[$i++ % $frames.Count], $text, (Format-Elapsed $timer.Elapsed)
        Write-Host ("`r" + $line.PadRight(78)) -NoNewline -ForegroundColor Yellow
        Start-Sleep -Milliseconds 120
    }
    Write-Host ("`r" + (' ' * 78) + "`r") -NoNewline
    return $timer.Elapsed
}

# Выполняет блок в фоновом потоке, показывая индикатор; ошибка блока выбрасывается как обычная.
function Invoke-WithSpinner([string]$text, [scriptblock]$block, [object[]]$arguments = @()) {
    if (-not $interactive) {
        Write-Host "      $text..."
        return (& $block @arguments)
    }

    $job = Start-ThreadJob -ScriptBlock $block -ArgumentList $arguments
    try {
        $elapsed = Show-Spinner $text { $job.State -eq 'Running' }
        Receive-Job $job -Wait -ErrorAction Stop
        Write-Host ('      {0}: {1}' -f $text, (Format-Elapsed $elapsed))
    }
    finally { Remove-Job $job -Force -ErrorAction SilentlyContinue }
}

function Invoke-Publish([string]$project, [string]$target) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    $log = Join-Path $work "publish-$name.log"
    $arguments = @('publish', "`"$project`"", '-c', 'Release', '-o', "`"$target`"", '--nologo', '-v', 'quiet')
    $process = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"

    $elapsed = if ($interactive) {
        Show-Spinner "публикация $name" { -not $process.HasExited }
    }
    else {
        Write-Host "      публикация $name..."
        $process.WaitForExit()
        [TimeSpan]::Zero
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        Get-Content $log, "$log.err" -Tail 30 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "      | $_" -ForegroundColor Red }
        throw "dotnet publish завершился с ошибкой: $project"
    }

    Write-Host ('      {0} опубликован{1}' -f $name, $(if ($interactive) { " за $(Format-Elapsed $elapsed)" } else { '' }))
}

Write-Host ''
Write-Host "=== Сборка SetupAssistant-Full $version ===" -ForegroundColor White

try {
    Start-Step 1 'Публикация SetupAssistant и ExtensionUpdater'
    Invoke-Publish "$root\src\SetupAssistant\SetupAssistant.csproj" $app
    Invoke-Publish "$root\src\ExtensionUpdater\ExtensionUpdater.csproj" $updater

    Start-Step 2 'Удаление старых версий расширений'
    & "$updater\ExtensionUpdater.exe" $app --prune-only
    if ($LASTEXITCODE -ne 0) { throw 'ExtensionUpdater --prune-only завершился с ошибкой' }

    Start-Step 3 'Упаковка конфига и дистрибутивов'
    foreach ($name in 'Config', 'Appendices', 'LatoFont') {
        if (Test-Path "$app\$name") { Copy-Item "$app\$name" -Destination $stage -Recurse }
    }
    Set-Content -Path "$stage\.payload-id" -Value ([guid]::NewGuid().ToString('N')) -Encoding ascii -NoNewline
    $zip = Join-Path $work 'payload.zip'
    Invoke-WithSpinner 'упаковка в zip' {
        param($source, $target)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($source, $target, [IO.Compression.CompressionLevel]::Fastest, $false)
    } @($stage, $zip)

    Start-Step 4 'Сборка единого exe'
    New-Item -ItemType Directory -Force $OutputDir | Out-Null
    $outDir = (Resolve-Path $OutputDir).Path
    $out = Join-Path $outDir "SetupAssistant-Full-$version.exe"
    $zipLength = (Get-Item $zip).Length

    try {
        Invoke-WithSpinner ('запись {0:N0} МБ в {1}' -f (($zipLength + (Get-Item "$app\SetupAssistant.exe").Length) / 1MB), (Split-Path $out -Leaf)) {
            param($exe, $target, $archive, $archiveLength)
            Copy-Item $exe $target -Force

            $footer = [byte[]]::new(16)
            [Text.Encoding]::ASCII.GetBytes('SAPAYLD1').CopyTo($footer, 0)
            [BitConverter]::GetBytes([int64]$archiveLength).CopyTo($footer, 8)

            $stream = [IO.File]::Open($target, [IO.FileMode]::Append, [IO.FileAccess]::Write)
            try {
                $zipStream = [IO.File]::OpenRead($archive)
                try { $zipStream.CopyTo($stream) } finally { $zipStream.Dispose() }
                $stream.Write($footer, 0, $footer.Length)
            }
            finally { $stream.Dispose() }
        } @("$app\SetupAssistant.exe", $out, $zip, $zipLength)
    }
    catch { throw "Не удалось записать $out — эта версия сейчас запущена? Закройте её и повторите сборку. ($($_.Exception.Message))" }

    # Результат — один файл: обновление дистрибутивов встроено в него (SetupAssistant-Full-<версия>.exe --update-distributions <папка>),
    # а ExtensionUpdater.exe нужен только этому скрипту для очистки и в выходную папку не попадает.
    Get-ChildItem $outDir -Filter 'ExtensionUpdater.exe' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    # Новая версия заменяет прежние: в папке остаётся только последний exe.
    foreach ($old in Get-ChildItem $outDir -Filter 'SetupAssistant-Full*.exe' | Where-Object FullName -ne $out) {
        try {
            Remove-Item $old.FullName -Force
            Write-Host "      Удалена прежняя версия: $($old.Name)"
        }
        catch { Write-Warning "Не удалось удалить прежнюю версию $($old.Name): файл занят (запущен?). Закройте её и удалите вручную." }
    }

    $size = (Get-Item $out).Length / 1MB

    # Ярлык в корне проекта всегда указывает на свежий exe: сам файл остаётся в dist, копия не создаётся и не мешает запущенной версии.
    $lnkPath = Join-Path $root 'SetupAssistant-Full.lnk'
    try {
        $shell = New-Object -ComObject WScript.Shell
        $lnk = $shell.CreateShortcut($lnkPath)
        $lnk.TargetPath = $out
        $lnk.WorkingDirectory = $outDir
        $lnk.Description = "Помощник настройки рабочего места для ЭП, версия $version"
        $lnk.Save()
    }
    catch {
        $lnkPath = $null
        Write-Warning "Не удалось создать ярлык в корне проекта: $($_.Exception.Message)"
    }

    Complete-Step
    Write-Progress -Activity "Сборка SetupAssistant-Full $version" -Completed

    # Итог: шкала на 100 %, время каждого шага, где лежит результат.
    $buildTimer.Stop()
    Write-Host ''
    Write-Host ('[{0}]  ГОТОВО за {1}' -f (Format-Bar $totalSteps $totalSteps), (Format-Elapsed $buildTimer.Elapsed)) -ForegroundColor Green
    foreach ($step in $doneSteps) {
        Write-Host ('   {0}. {1,-46} {2}' -f $step.Number, $step.Title, (Format-Elapsed $step.Elapsed))
    }
    Write-Host ''
    Write-Host ('   Файл:   {0}  ({1:N0} МБ, внутри {2:N0} МБ данных)' -f $out, $size, ($zipLength / 1MB)) -ForegroundColor Green
    if ($lnkPath) { Write-Host "   Ярлык:  $lnkPath" -ForegroundColor Green }
}
catch {
    Write-Progress -Activity "Сборка SetupAssistant-Full $version" -Completed
    $where = if ($currentStep) { "на шаге $($currentStep.Number) из ${totalSteps} ($($currentStep.Title))" } else { 'до начала шагов' }
    Write-Host ''
    Write-Host ('[{0}]  СБОРКА ПРЕРВАНА {1}' -f (Format-Bar ([math]::Max(0, ($currentStep?.Number ?? 1) - 1)) $totalSteps), $where) -ForegroundColor Red
    throw
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
