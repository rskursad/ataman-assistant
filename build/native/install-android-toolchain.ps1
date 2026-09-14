# Installs the Android cross-compile toolchain (NDK + CMake) required to
# build llama.cpp / Piper for Android.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File build\native\install-android-toolchain.ps1
#
# Optional parameters:
#   -NdkVersion   NDK package id, default "ndk;27.2.12479018"
#   -CmakeVersion CMake package id, default "cmake;3.22.1"
param(
    [string]$NdkVersion = "ndk;27.2.12479018",
    [string]$CmakeVersion = "cmake;3.22.1"
)

$ErrorActionPreference = "Stop"

function Resolve-AndroidSdk {
    $candidates = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        "$env:LOCALAPPDATA\Android\Sdk",
        "$env:USERPROFILE\AppData\Local\Android\Sdk"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $sdk = $candidates | Select-Object -First 1
    if (-not $sdk) {
        throw "Android SDK bulunamadı. ANDROID_HOME ayarlayın."
    }
    return $sdk
}

function Get-SdkManager {
    $sdk = Resolve-AndroidSdk
    $sdkmanager = Get-ChildItem -Path $sdk -Recurse -Filter "sdkmanager.bat" -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (-not $sdkmanager) {
        throw "sdkmanager bulunamadı. Android SDK command-line-tools gerekli."
    }
    return $sdkmanager.FullName
}

$sdkmanager = Get-SdkManager
Write-Host "SDKManager: $sdkmanager"

& $sdkmanager --install $NdkVersion $CmakeVersion 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "sdkmanager başarısız (exit $LASTEXITCODE). Lisansları kabul etmeniz gerekebilir: sdkmanager --licenses"
}

Write-Host "Hazır:"
Write-Host "  NDK   -> $(Resolve-AndroidSdk)\ndk"
Write-Host "  CMake -> $(Resolve-AndroidSdk)\cmake"
Write-Host "Şimdi çalıştırın: build\native\build-android-libs.ps1"