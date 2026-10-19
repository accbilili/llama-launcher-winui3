$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $PSScriptRoot 'src\LlamaLauncher.csproj'
$outputDirectory = Join-Path $PSScriptRoot 'release-1.3.0'
& dotnet publish $projectFile -c Release -p:Platform=x64 -o $outputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
foreach ($requiredFile in @('LlamaLauncher.exe', 'App.xbf', 'LlamaLauncher.pri', 'Microsoft.UI.Xaml.dll', 'Assets\app.ico', 'Assets\app.svg', 'Assets\qwen-color.svg', 'Assets\openai.svg', 'Assets\hunyuan-color.svg', 'Assets\gguf.svg')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $requiredFile))) { throw "发布缺少文件：$requiredFile" }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.md') -Destination (Join-Path $outputDirectory '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '验证说明.md') -Destination (Join-Path $outputDirectory '验证说明.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '自测清单-1.1.0.md') -Destination (Join-Path $outputDirectory '自测清单-1.1.0.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '客户端接入-1.2.0.md') -Destination (Join-Path $outputDirectory '客户端接入-1.2.0.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '图标预览-1.2.1.png') -Destination (Join-Path $outputDirectory '图标预览-1.2.1.png') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '优化与自测-1.3.0.md') -Destination (Join-Path $outputDirectory '优化与自测-1.3.0.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $outputDirectory -Recurse -Force
Write-Host "完成：$outputDirectory\LlamaLauncher.exe"
