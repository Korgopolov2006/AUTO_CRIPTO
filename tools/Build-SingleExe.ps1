#requires -Version 7
<#
.SYNOPSIS
    Собирает единый SetupAssistant-Full.exe: приложение + конфиг и дистрибутивы внутри одного файла.

.DESCRIPTION
    1. Публикует SetupAssistant и вспомогательный ExtensionUpdater (Release, автономные, без установки .NET).
    2. Удаляет из публикации старые версии .crx (ExtensionUpdater --prune-only, без сети). Этот инструмент в результат не входит.
    3. Упаковывает Config, Appendices и LatoFont в zip и дописывает его в конец SetupAssistant.exe
       вместе с подвалом «SAPAYLD1» + длина архива. Приложение читает эти данные само и распаковывает
       нужный файл в %ProgramData%\SetupAssistant\Payload только в момент, когда он понадобился.
    Файл Config\Config.json или дистрибутив рядом с exe имеют приоритет над встроенными.
    Если exe нужно подписать, подписывайте готовый SetupAssistant-Full.exe: приложение находит данные и в подписанном файле.
    Результат — единственный файл SetupAssistant-Full.exe: и мастер настройки, и обновление дистрибутивов (--update-distributions).

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

function Invoke-Publish([string]$project, [string]$target) {
    dotnet publish $project -c Release -o $target --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой: $project" }
}

try {
    Write-Host '1/4 Публикация SetupAssistant и ExtensionUpdater...'
    Invoke-Publish "$root\src\SetupAssistant\SetupAssistant.csproj" $app
    Invoke-Publish "$root\src\ExtensionUpdater\ExtensionUpdater.csproj" $updater

    Write-Host '2/4 Удаление старых версий расширений...'
    & "$updater\ExtensionUpdater.exe" $app --prune-only
    if ($LASTEXITCODE -ne 0) { throw 'ExtensionUpdater --prune-only завершился с ошибкой' }

    Write-Host '3/4 Упаковка конфига и дистрибутивов...'
    foreach ($name in 'Config', 'Appendices', 'LatoFont') {
        if (Test-Path "$app\$name") { Copy-Item "$app\$name" -Destination $stage -Recurse }
    }
    Set-Content -Path "$stage\.payload-id" -Value ([guid]::NewGuid().ToString('N')) -Encoding ascii -NoNewline
    $zip = Join-Path $work 'payload.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Fastest, $false)

    Write-Host '4/4 Сборка единого exe...'
    New-Item -ItemType Directory -Force $OutputDir | Out-Null
    $out = Join-Path (Resolve-Path $OutputDir).Path 'SetupAssistant-Full.exe'
    Copy-Item "$app\SetupAssistant.exe" $out -Force

    $zipLength = (Get-Item $zip).Length
    $footer = [byte[]]::new(16)
    [Text.Encoding]::ASCII.GetBytes('SAPAYLD1').CopyTo($footer, 0)
    [BitConverter]::GetBytes([int64]$zipLength).CopyTo($footer, 8)

    $stream = [IO.File]::Open($out, [IO.FileMode]::Append, [IO.FileAccess]::Write)
    try {
        $zipStream = [IO.File]::OpenRead($zip)
        try { $zipStream.CopyTo($stream) } finally { $zipStream.Dispose() }
        $stream.Write($footer, 0, $footer.Length)
    }
    finally { $stream.Dispose() }

    # Результат — один файл: обновление дистрибутивов встроено в него (SetupAssistant-Full.exe --update-distributions <папка>),
    # а ExtensionUpdater.exe нужен только этому скрипту для очистки и в выходную папку не попадает.
    Get-ChildItem (Split-Path $out) -Filter 'ExtensionUpdater.exe' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    $size = (Get-Item $out).Length / 1MB
    Write-Host ('Готово: {0}  ({1:N0} МБ, внутри {2:N0} МБ данных)' -f $out, $size, ($zipLength / 1MB)) -ForegroundColor Green
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
