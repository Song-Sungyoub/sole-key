param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot 'SoleKey.csproj'
$version = ([xml](Get-Content -Raw -LiteralPath $projectFile)).Project.PropertyGroup.Version
$releaseDir = Join-Path $projectRoot "artifacts/releases/$version"
$publishDir = Join-Path $releaseDir 'app'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
if (-not $SkipPublish) {
    dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:BaseOutputPath=artifacts/package-build/ -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
}
$exe = Join-Path $publishDir 'SoleKey.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published application is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Published EXE version mismatch.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'RELEASE-NOTES.md') -Destination $publishDir -Force
$target = ([xml](Get-Content -Raw -LiteralPath $projectFile)).Project.PropertyGroup.TargetFramework
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $projectRoot "artifacts/package-build/Release/$target/win-x64/SoleKey.runtimeconfig.json") | ConvertFrom-Json
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $runtimeSource = Join-Path $packages "$package/$($framework.version)"
    $licenseDir = Join-Path $publishDir "Licenses/$($framework.name)"
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    Get-ChildItem -LiteralPath $runtimeSource -File | Where-Object { $_.Name -in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') } | Copy-Item -Destination $licenseDir -Force
    if (-not (Get-ChildItem -LiteralPath $licenseDir -File)) { throw "Missing runtime license: $($framework.name)" }
}
$files = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File | Sort-Object FullName)
foreach ($file in $files) {
    if ($file.Extension -in @('.solekey', '.pdb', '.log', '.bak') -or $file.Name -eq 'settings.json' -or $file.Name -like '*.solekey.*' -or $file.Name -like '*Checks*') { throw "Unexpected release payload: $($file.Name)" }
}
$toolDir = Join-Path $projectRoot 'obj/installer-tools'
$wix = Join-Path $toolDir 'wix.exe'
if (-not (Test-Path -LiteralPath $wix)) {
    dotnet tool install wix --version 5.0.2 --tool-path $toolDir
    if ($LASTEXITCODE -ne 0) { throw 'WiX installation failed.' }
}
& $wix extension add -g WixToolset.UI.wixext/5.0.2
if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension setup failed.' }
$fragment = Join-Path $projectRoot 'obj/SoleKey-Payload.wxs'
$xmlSettings = [Xml.XmlWriterSettings]::new(); $xmlSettings.Indent = $true
$xml = [Xml.XmlWriter]::Create($fragment, $xmlSettings)
try {
    $xml.WriteStartElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
    $xml.WriteStartElement('Fragment')
    $xml.WriteStartElement('ComponentGroup'); $xml.WriteAttributeString('Id', 'Payload'); $xml.WriteAttributeString('Directory', 'INSTALLFOLDER')
    $index = 0
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($publishDir, $file.FullName)
        $subdir = [IO.Path]::GetDirectoryName($relative)
        $xml.WriteStartElement('Component'); $xml.WriteAttributeString('Id', "Payload$index"); $xml.WriteAttributeString('Guid', '*')
        if ($subdir) { $xml.WriteAttributeString('Subdirectory', $subdir) }
        $xml.WriteStartElement('File'); $xml.WriteAttributeString('Id', $(if ($relative -eq 'SoleKey.exe') { 'AppExe' } else { "File$index" }))
        $xml.WriteAttributeString('Source', $file.FullName); $xml.WriteAttributeString('KeyPath', 'yes'); $xml.WriteEndElement()
        $xml.WriteEndElement(); $index++
    }
    $xml.WriteEndElement(); $xml.WriteEndElement(); $xml.WriteEndElement()
} finally { $xml.Dispose() }
$msi = Join-Path $releaseDir "SoleKey-$version-x64.msi"
& $wix build (Join-Path $PSScriptRoot 'Product.wxs') $fragment -arch x64 -ext WixToolset.UI.wixext -culture ko-KR -d "ProductVersion=$version" -d "ProjectRoot=$projectRoot" -o $msi
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
$zip = Join-Path $releaseDir "SoleKey-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$hashes = @($msi, $zip) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output $msi
Write-Output $zip
