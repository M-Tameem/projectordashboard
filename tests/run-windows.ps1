$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & .\build.bat --ci
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard build failed' }
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    $references = @('/r:Dashboard.exe', "/r:$framework\System.dll", "/r:$framework\System.Core.dll",
        "/r:$framework\System.Xaml.dll", "/r:$framework\System.Windows.Forms.dll",
        "/r:$framework\System.Drawing.dll", "/r:$framework\WPF\WindowsBase.dll",
        "/r:$framework\WPF\PresentationCore.dll", "/r:$framework\WPF\PresentationFramework.dll")
    foreach ($test in @('InputTests', 'LayoutSmoke', 'WindowInputTests')) {
        & $compiler /nologo /target:exe /platform:x86 "/out:$test.exe" @references "tests\$test.cs"
        if ($LASTEXITCODE -ne 0) { throw "$test compilation failed" }
        & ".\$test.exe"
        if ($LASTEXITCODE -ne 0) { throw "$test failed" }
    }
    $rollbackFolder = '.compile-check\rollback'
    New-Item -ItemType Directory -Force "$rollbackFolder\previous" | Out-Null
    Copy-Item Dashboard.exe "$rollbackFolder\Dashboard.exe" -Force
    & $compiler /nologo /target:exe /platform:x86 "/out:$rollbackFolder\previous\Dashboard.exe" tests\fixtures\PreviousBuild.cs
    if ($LASTEXITCODE -ne 0) { throw 'Rollback fixture compilation failed' }
    & $compiler /nologo /target:exe /platform:x86 "/out:$rollbackFolder\RollbackTests.exe" @references tests\RollbackTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Rollback test compilation failed' }
    & ".\$rollbackFolder\RollbackTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Rollback checks failed' }
} finally {
    Remove-Item InputTests.exe, LayoutSmoke.exe, WindowInputTests.exe -ErrorAction SilentlyContinue
    Pop-Location
}
