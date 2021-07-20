param (
  # Actions 
  [switch]$build = $false,
  [switch]$test = $false,
  [switch]$testExtra = $false,
  [switch]$updateVsixVersion = $false,
  [switch]$uploadVsix = $false,
  [switch]$help = $false,

  # Settings
  [switch]$ci = $false,
  [string]$config = "Debug",

  [parameter(ValueFromRemainingArguments=$true)][string[]]$properties)

Set-StrictMode -version 2.0
$ErrorActionPreference="Stop"

[string]$rootDir = Split-Path -parent $MyInvocation.MyCommand.Definition 
[string]$rootDir = Resolve-Path (Join-Path $rootDir "..")
[string]$binariesDir = Join-Path $rootDir "Binaries"
[string]$configDir = Join-Path $binariesDir $config
[string]$deployDir = Join-Path $binariesDir "Deploy"
[string]$logsDir = Join-Path $binariesDir "Logs"
[string]$toolsDir = Join-Path $rootDir "Tools"
[string[]]$vsVersions = @("2017", "2019", "2022")

function Print-Usage() {
  Write-Host "Actions:"
  Write-Host "  -build                    Build VsVim"
  Write-Host "  -test                     Run unit tests"
  Write-Host "  -testExtra                Run extra verification"
  Write-Host "  -updateVsixVersion        Update the VSIX manifest version"
  Write-Host "  -uploadVsix               Upload the VSIX to the Open Gallery"

  Write-Host ""
  Write-Host "Settings:"
  Write-Host "  -ci                       True when running in CI"
  Write-Host "  -config <value>           Build configuration: 'Debug' or 'Release'"
}

# Toggle between human readable messages and Azure Pipelines messages based on 
# our current environment.
# https://docs.microsoft.com/en-us/azure/devops/pipelines/scripts/logging-commands?view=azure-devops&tabs=powershell
function Write-TaskError([string]$message) {
  if ($ci) {
    Write-Host "##vso[task.logissue type=error]$message"
  }
  else {
    Write-Host $message
  }
}

# Meant to mimic the OpenVsix Gallery script for changing the VSIX version based
# on the Azure DevOps build environment
function Update-VsixVersion() {
  if ($env:BUILD_BUILDID -eq $null) {
    throw "The environment variable %BUILD_BUILDID% is not set"
  }

  Write-Host "Updating VSIX version to include $($env:BUILD_BUILDID)"

  foreach ($vsVersion in $vsVersions) {
    Write-Host "Updating $vsVersion"
    $vsixManifest = Join-Path $rootDir "Src/VsVim$($vsVersion)/source.extension.vsixmanifest"
    [xml]$vsixXml = Get-Content $vsixManifest
    $ns = New-Object System.Xml.XmlNamespaceManager $vsixXml.NameTable
    $ns.AddNamespace("ns", $vsixXml.DocumentElement.NamespaceURI) | Out-Null

    $attrVersion = $vsixXml.SelectSingleNode("//ns:Identity", $ns).Attributes["Version"]
    [Version]$version = $attrVersion.Value
    $version = New-Object Version ([int]$version.Major),([int]$version.Minor),$env:BUILD_BUILDID
    $attrVersion.InnerText = $version
    $vsixXml.Save($vsixManifest) | Out-Null
  }
}

# Meant to mimic the OpenVsix Gallery script for uploading the VSIX 
function Upload-Vsix() {
  if ($env:BUILD_BUILDID -eq $null) {
    throw "This is only meant to run in Azure DevOps"
  }

  Write-Host "Uploading VSIX to the Open Gallery"
  foreach ($vsVersion in $vsVersions) {
    Write-Host "Uploading $vsVersion"
    $vsixFile = Join-Path $deployDir $vsVersion
    $vsixFile = Join-Path $vsixFile "VsVim.vsix"
    $vsixUploadEndpoint = "http://vsixgallery.com/api/upload"
    $repoUrl = "https://github.com/VsVim/VsVim/"
    [Reflection.Assembly]::LoadWithPartialName("System.Web") | Out-Null
    $repo = [System.Web.HttpUtility]::UrlEncode($repoUrl)
    $issueTracker = [System.Web.HttpUtility]::UrlEncode(($repoUrl + "issues/"))

    [string]$url = ($vsixUploadEndpoint + "?repo=" + $repo + "&issuetracker=" + $issueTracker)
    [byte[]]$bytes = [System.IO.File]::ReadAllBytes($vsixFile)

    try {
        $response = Invoke-WebRequest $url -Method Post -Body $bytes -UseBasicParsing
        'OK' | Write-Host -ForegroundColor Green
    }
    catch{
        'FAIL' | Write-TaskError
        $_.Exception.Response.Headers["x-error"] | Write-TaskError
    }
  }
}

function Get-PackagesDir() {
  $d = $null
  if ($env:NUGET_PACKAGES -ne $null) {
    $d = $env:NUGET_PACKAGES
  }
  else {
    $d = Join-Path $env:UserProfile ".nuget\packages\"
  }

  Create-Directory $d
  return $d
}

function Get-MSBuildPath() {
  $vsWhere = Join-Path $toolsDir "vswhere.exe"
  $vsInfo = Exec-Command $vsWhere "-latest -format json -requires Microsoft.Component.MSBuild" | Out-String | ConvertFrom-Json

  # use first matching instance
  $vsInfo = $vsInfo[0]
  $vsInstallDir = $vsInfo.installationPath
  $vsMajorVersion = $vsInfo.installationVersion.Split('.')[0]
  $msbuildVersionDir = if ([int]$vsMajorVersion -lt 16) { "$vsMajorVersion.0" } else { "Current" }
  return Join-Path $vsInstallDir "MSBuild\$msbuildVersionDir\Bin\msbuild.exe"
}

# Test the contents of the Vsix to make sure it has all of the appropriate
# files 
function Test-VsixContents() { 
  Write-Host "Verifying the Vsix Contents"
  foreach ($vsVersion in $vsVersions) {
    Write-Host "Verifying $vsVersion"
    $vsixPath = Join-Path $deployDir $vsVersion
    $vsixPath = Join-Path $vsixPath "VsVim.vsix"
    if (-not (Test-Path $vsixPath)) {
      throw "Vsix doesn't exist"
    }

    $expectedFiles = @(
      "Colors.pkgdef",
      "extension.vsixmanifest",
      "License.txt",
      "Vim.Core.dll",
      "VsVim.dll",
      "VsVim.pkgdef",
      "VsVim_large.png",
      "VsVim_small.png",
      "VsVim_full.pdf",
      "catalog.json",
      "manifest.json",
      "[Content_Types].xml")

    # Make a folder to hold the foundFiles
    $target = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
    Create-Directory $target 
    $zipUtil = Join-Path $rootDir "Tools\7za920\7za.exe"
    Exec-Command $zipUtil "x -o$target $vsixPath" | Out-Null

    $foundFiles = Get-ChildItem $target | %{ $_.Name }
    if ($foundFiles.Count -ne $expectedFiles.Count) { 
      Write-TaskError "Found $($foundFiles.Count) but expected $($expectedFiles.Count)"
      Write-TaskError "Wrong number of foundFiles in VSIX." 
      Write-TaskError "Extra foundFiles"
      foreach ($file in $foundFiles) {
        if (-not $expectedFiles.Contains($file)) {
          Write-TaskError "`t$file"
        }
      }

      Write-Host "Missing foundFiles"
      foreach ($file in $expectedFiles) {
        if (-not $foundFiles.Contains($file)) {
          Write-TaskError "`t$file"
        }
      }

      Write-TaskError "Location: $target"
    }
  }

  foreach ($item in $expectedFiles) {
    # Look for dummy foundFiles that made it into the VSIX instead of the 
    # actual DLL 
    $itemPath = Join-Path $target $item
    if ($item.EndsWith("dll") -and ((get-item $itemPath).Length -lt 5kb)) {
      throw "Small file detected $item in the zip file ($target)"
    }
  }
}

# Make sure that the version number is the same in all locations.  
function Test-Version() {
  Write-Host "Testing Version Numbers"
  $version = $null;
  foreach ($line in Get-Content "Src\VimCore\Constants.fs") {
    if ($line -match 'let VersionNumber = "([\d.]*)"') {
      $version = $matches[1]
      break
    }
  }

  if ($version -eq $null) {
    throw "Couldn't determine the version from Constants.fs"
  }

  $foundPackageVersion = $false
  foreach ($line in Get-Content "Src\VsVimShared\VsVimPackage.cs") {
    if ($line -match 'productId: VimConstants.VersionNumber') {
      $foundPackageVersion = $true
      break
    }
  }

  if (-not $foundPackageVersion) {
    throw "Could not verify the version of VsVimPackage.cs"
  }

  foreach ($vsVersion in $vsVersions) {
    $data = [xml](Get-Content "Src\VsVim$($vsVersion)\source.extension.vsixmanifest")
    $manifestVersion = $data.PackageManifest.Metadata.Identity.Version
    if ($manifestVersion -ne $version) { 
      throw "The version $version doesn't match up with the manifest version of $manifestVersion" 
    }
  }
}

function Test-UnitTests() { 
  Write-Host "Running unit tests"
  foreach ($vsVersion in $vsVersions) {
    Write-Host "Running $vsVersion"
    $resultsDir = Join-Path $binariesDir "xunitResults"
    Create-Directory $resultsDir

    $all = @(
      "VimCoreTest$($vsVersion)\net472\Vim.Core.$($vsVersion).UnitTest.dll",
      "VsVimTest$($vsVersion)\net472\Vim.VisualStudio.Shared.$($vsVersion).UnitTest.dll")
    $xunit = Join-Path (Get-PackagesDir) "xunit.runner.console\2.4.1\tools\net472\xunit.console.x86.exe"
    $anyFailed = $false

    foreach ($filePath in $all) { 
      $filePath = Join-Path $configDir $filePath
      $fileName = [IO.Path]::GetFileNameWithoutExtension($filePath)
      $logFilePath = Join-Path $resultsDir "$($fileName).xml"
      $arg = "$filePath -xml $logFilePath"
      try {
        Exec-Console $xunit $arg
      }
      catch {
        $anyFailed = $true
      }
    }
  }

  if ($anyFailed) {
    throw "Unit tests failed"
  }
}


function Build-Solution(){ 
  $msbuild = Get-MSBuildPath
  Write-Host "Using MSBuild from $msbuild"

  Write-Host "Building VsVim"
  $binlogFilePath = Join-Path $logsDir "msbuild.binlog"
  $args = "/nologo /restore /v:m /m /bl:$binlogFilePath /p:Configuration=$config VsVim.sln"

  if ($ci) {
    $args += " /p:DeployExtension=false"

  }

  Exec-Console $msbuild $args

  Write-Host "Cleaning Vsix"
  Create-Directory $deployDir
  Push-Location $deployDir 
  try { 
    Remove-Item -re -fo "$deployDir\*"
    foreach ($vsVersion in $vsVersions) {
      Write-Host "Cleaning $vsVersion"
      $vsVersionDir = Join-Path $deployDir $vsVersion 
      Create-Directory $vsVersionDir
      Set-Location $vsVersionDir
      $sourcePath = Join-Path $configDir "VsVim$($vsVersion)\net472\VsVim.vsix"
      Copy-Item $sourcePath "VsVim.orig.vsix"
      Copy-Item $sourcePath "VsVim.vsix"

      # Due to the way we build the VSIX there are many files included that we don't actually
      # want to deploy.  Here we will clear out those files and rebuild the VSIX without 
      # them
      $cleanUtil = Join-Path $configDir "CleanVsix\net472\CleanVsix.exe"
      Exec-Console $cleanUtil (Join-Path $vsVersionDir "VsVim.vsix")
      Copy-Item "VsVim.vsix" "VsVim.zip"
      Set-Location ..
    }
  }
  finally {
    Pop-Location
  }
}

Push-Location $rootDir
try {
  . "Scripts\Common-Utils.ps1"

  if ($help -or ($properties -ne $null)) {
    Print-Usage
    exit 0
  }

  if ($updateVsixVersion) {
    Update-VsixVersion
  }

  if ($build) {
    Build-Solution
  }

  if ($test) {
    Test-UnitTests
  }

  if ($testExtra) {
    Test-VsixContents
    Test-Version
  }

  if ($uploadVsix) {
    Upload-Vsix
  }
}
catch {
  Write-TaskError "Error: $($_.Exception.Message)"
  Write-TaskError $_.ScriptStackTrace
  exit 1
}
finally {
  Pop-Location
}


