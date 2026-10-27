param([Parameter(Mandatory=$true)][string]$InputFile, [Parameter(Mandatory=$true)][string]$OutputFile, [Parameter(Mandatory=$true)][string]$EngineRoot)
$ErrorActionPreference = 'Stop'
try {
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
    [Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::InvariantCulture
    [void][Reflection.Assembly]::LoadFrom((Join-Path $EngineRoot 'Model Plot Painter.exe'))
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $parameters = New-Object CodeDom.Compiler.CompilerParameters
    $parameters.GenerateInMemory = $true
    foreach ($reference in @((Join-Path $EngineRoot 'Model Plot Painter.exe'), 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll')) {
        [void]$parameters.ReferencedAssemblies.Add($reference)
    }
    $compiled = $provider.CompileAssemblyFromFile($parameters, (Join-Path $PSScriptRoot 'modelplot_native.cs'))
    $errors = @($compiled.Errors | Where-Object { -not $_.IsWarning })
    if ($errors.Count) { throw ($errors -join [Environment]::NewLine) }
    [void]$compiled.CompiledAssembly
    [LuzrModelPlot]::Run($InputFile, $OutputFile)
} catch {
    $failure = $_.Exception
    while ($failure.InnerException) { $failure = $failure.InnerException }
    @{ error = @{ message = $failure.Message; type = $failure.GetType().FullName } } | ConvertTo-Json -Compress | Set-Content -LiteralPath $OutputFile -Encoding UTF8
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
