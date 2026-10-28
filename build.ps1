# Build script: compile TokFrame.exe (single file, WebView2 deps embedded) with in-box csc.exe
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe not found (need .NET Framework 4.x)'
}

$app = Join-Path $root 'app'
New-Item -Path $app -ItemType Directory -Force | Out-Null

& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    /win32icon:"$root\build\icon.ico" `
    /win32manifest:"$root\src\app.manifest" `
    /out:"$app\TokFrame.exe" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:"$root\lib\Microsoft.Web.WebView2.Core.dll" `
    /r:"$root\lib\Microsoft.Web.WebView2.WinForms.dll" `
    /resource:"$root\lib\Microsoft.Web.WebView2.Core.dll","DouyinBrowser.WebView2.Core.dll" `
    /resource:"$root\lib\Microsoft.Web.WebView2.WinForms.dll","DouyinBrowser.WebView2.WinForms.dll" `
    /resource:"$root\lib\WebView2Loader.dll","DouyinBrowser.WebView2Loader.dll" `
    "$root\src\browser.cs"

if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit $LASTEXITCODE)" }

$exe = Get-Item "$app\TokFrame.exe"
Write-Host "Build OK: $($exe.FullName)" -ForegroundColor Green
Write-Host "Size: $([math]::Round($exe.Length/1KB)) KB"
Write-Host "SHA256: $((Get-FileHash $exe.FullName -Algorithm SHA256).Hash)"
