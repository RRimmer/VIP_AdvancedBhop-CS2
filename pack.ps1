$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$file = Join-Path $project 'VIP_Advanced_Bhop/VIP_Advanced_Bhop.csproj'
dotnet build $file -m:1 -c Release -p:NuGetAudit=false --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed: $file" }

# A fresh directory prevents old releases leaking into the archive.
$stageRoot = [IO.Path]::GetFullPath((Join-Path $project 'VIP_Advanced_Bhop/obj'))
$stage = Join-Path $stageRoot ([Guid]::NewGuid().ToString('N'))
$css = Join-Path $stage 'addons/counterstrikesharp'
$plugin = Join-Path $css 'plugins/VIP_Advanced_Bhop'
$bin = Join-Path $project 'VIP_Advanced_Bhop/bin/Release/net10.0'
$zip = Join-Path $project 'VIP_Advanced_Bhop-CS2.zip'
try {
    New-Item -ItemType Directory -Force -Path $plugin | Out-Null
    Copy-Item "$bin/VIP_Advanced_Bhop.dll","$bin/VIP_Advanced_Bhop.deps.json" $plugin
    Copy-Item "$bin/lang" $plugin -Recurse
    $modules = Join-Path $css 'configs/plugins/VIPCore/Modules'
    New-Item -ItemType Directory -Force -Path $modules | Out-Null
    Copy-Item "$project/configs/plugins/VIPCore/Modules/VIP_Advanced_Bhop.jsonc" $modules
    Copy-Item "$project/INSTALL.md","$project/CHANGELOG.md" $stage
    # ZipFile uses '/' entries on Windows so Linux unzip can extract the tree.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $tempZip = "$stage.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$tempZip)
    if ([IO.File]::Exists($zip)) {
        try { [IO.File]::Replace($tempZip, $zip, [System.Management.Automation.Language.NullString]::Value) }
        catch [IO.IOException] {
            $zip = Join-Path $project ("VIP_Advanced_Bhop-CS2-" + [DateTime]::Now.ToString("yyyyMMdd-HHmmss") + ".zip")
            [IO.File]::Move($tempZip, $zip)
        }
    } else {
        [IO.File]::Move($tempZip, $zip)
    }
    Write-Host "OK $zip"
} finally {
    if ((Split-Path ([IO.Path]::GetFullPath($stage))) -eq $stageRoot -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
