# Cross-compiles/acquires the native libraries the Android build needs:
#   - libllama.so + ggml libs  (llama.cpp, built with the Android NDK)
#   - libvosk.so               (extracted from the official vosk-android AAR)
#   - libonnxruntime.so        (extracted from the official onnxruntime AAR, for Piper / TTS / NMT)
#
# Produced libraries are copied into: ataman-assistant.Android/libs/<abi>/
# (the Android csproj picks them up automatically once present).
#
# Usage:
#   build\native\install-android-toolchain.ps1     # once
#   powershell -ExecutionPolicy Bypass -File build\native\build-android-libs.ps1 -Abi arm64-v8a
param(
    [string]$Abi = "arm64-v8a",
    [string]$LlamaCommit = "master",
    [switch]$BuildVulkan,
    [switch]$SkipLlama,
    [switch]$SkipVosk,
    [switch]$SkipOnnx,
    [string]$VoskVersion = "0.3.47",
    [string]$OnnxRuntimeVersion = "1.18.0",
    [string]$NinjaBuildJobs = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$DepsDir = Join-Path $PSScriptRoot "deps"
$AndroidProjectDir = Join-Path $RepoRoot "ataman-assistant.Android"
$LibOutDir = Join-Path $AndroidProjectDir "libs" $Abi

function Resolve-AndroidSdk {
    $candidates = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        "$env:LOCALAPPDATA\Android\Sdk",
        "$env:USERPROFILE\AppData\Local\Android\Sdk"
    ) | Where-Object { $_ -and (Test-Path $_) }
    $sdk = $candidates | Select-Object -First 1
    if (-not $sdk) { throw "Android SDK bulunamadı (ANDROID_HOME)." }
    return $sdk
}

function Resolve-NewestNdk([string]$sdk) {
    $ndkRoot = Join-Path $sdk "ndk"
    if (-not (Test-Path $ndkRoot)) { throw "NDK yüklü değil. install-android-toolchain.ps1'i çalıştırın." }
    $versioned = Get-ChildItem $ndkRoot -Directory | Sort-Object Name -Descending
    if (-not $versioned) { throw "NDK klasörü boş." }
    return $versioned[0].FullName
}

function Resolve-CMake([string]$sdk) {
    $cmakeRoot = Join-Path $sdk "cmake"
    $versioned = Get-ChildItem $cmakeRoot -Directory | Sort-Object Name -Descending
    if (-not $versioned) { throw "CMake yüklü değil. install-android-toolchain.ps1'i çalıştırın." }
    return $versioned[0].FullName
}

function Invoke-DownloadAndExtractAar([string]$url, [string]$outZip, [string]$targetRoot, [string]$expectedSo) {
    if (-not (Test-Path $outZip)) {
        Write-Host "İndiriliyor: $url"
        Invoke-WebRequest -Uri $url -OutFile $outZip
    }
    $extractDir = [System.IO.Path]::Combine($tmpRoot, [System.IO.Path]::GetFileNameWithoutExtension($outZip) + "_x")
    if (-not (Test-Path $extractDir)) {
        Expand-Archive -LiteralPath $outZip -DestinationPath $extractDir -Force
    }
    $abiJni = Join-Path $extractDir "jni" $Abi
    if (-not (Test-Path $abiJni)) { throw "AAR içinde jni/$Abi bulunamadı: $outZip" }
    $soFiles = Get-ChildItem $abiJni -Filter "*.so"
    if (-not $soFiles) { throw "AAR içinde .so bulunamadı." }
    if ($soFiles.Count -gt 1) {
        # ggml style builds ship many .so; only copy the requested one(s)
        foreach ($f in $soFiles) {
            if ($f.Name -eq $expectedSo) { Copy-Item -LiteralPath $f.FullName -Destination $targetRoot -Force }
        }
    }
    else {
        Copy-Item -LiteralPath $soFiles[0].FullName -Destination $targetRoot -Force
    }
}

New-Item -ItemType Directory -Force -Path $DepsDir, $LibOutDir | Out-Null
$tmpRoot = Join-Path $DepsDir "tmp"
New-Item -ItemType Directory -Force -Path $tmpRoot | Out-Null

# ---------------------------------------------------------------------------
If (-not $SkipLlama) {
    $llamaDir = Join-Path $DepsDir "llama.cpp"
    if (-not (Test-Path $llamaDir)) {
        Write-Host "llama.cpp klonlanıyor (shallow)…"
        git clone --depth 1 --branch $LlamaCommit https://github.com/ggml-org/llama.cpp $llamaDir
        if ($LASTEXITCODE -ne 0) { throw "llama.cpp klonu başarısız." }
    }
    else {
        git -C $llamaDir fetch --depth 1 origin $LlamaCommit
        if ($LASTEXITCODE -eq 0) { git -C $llamaDir checkout $LlamaCommit }
        Write-Host "llama.cpp güncellendi -> $LlamaCommit"
    }

    $sdk    = Resolve-AndroidSdk
    $ndkDir = Resolve-NewestNdk $sdk
    $cmake  = Resolve-CMake $sdk

    $cmakeExe   = Join-Path $cmake "bin\cmake.exe"
    $ninjaExe   = Join-Path $cmake "bin\ninja.exe"
    $toolchain  = Join-Path $ndkDir "build\cmake\android.toolchain.cmake"

    $buildDir   = Join-Path $DepsDir "build-llama-$Abi"
    New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

    $vulkan = if ($BuildVulkan) { "ON" } else { "OFF" }

    & $cmakeExe -S $llamaDir -B $buildDir -G Ninja `
        "-DCMAKE_MAKE_PROGRAM=$ninjaExe" `
        "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
        "-DANDROID_ABI=$Abi" `
        "-DANDROID_PLATFORM=android-28" `
        "-DCMAKE_BUILD_TYPE=Release" `
        "-DBUILD_SHARED_LIBS=ON" `
        "-DGGML_OPENMP=OFF" `
        "-DGGML_VULKAN=$vulkan" `
        "-DLLAMA_BUILD_EXAMPLES=OFF" `
        "-DLLAMA_BUILD_TESTS=OFF" `
        "-DLLAMA_BUILD_TOOLS=OFF" `
        "-DLLAMA_BUILD_SERVER=OFF"
    if ($LASTEXITCODE -ne 0) { throw "llama.cpp CMake configure başarısız." }

    $jobs = if ($NinjaBuildJobs) { $NinjaBuildJobs } else { "$([Environment]::ProcessorCount)" }
    & $cmakeExe --build $buildDir --config Release -j $jobs
    if ($LASTEXITCODE -ne 0) { throw "llama.cpp build başarısız." }

    $installDir = Join-Path $DepsDir "install-llama-$Abi"
    & $cmakeExe --install $buildDir --prefix $installDir
    if ($LASTEXITCODE -ne 0) { throw "llama.cpp install başarısız." }

    $libSrcDir = Join-Path $installDir "lib"
    Get-ChildItem $libSrcDir -Filter "*.so" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $LibOutDir -Force
        Write-Host ("  + " + $_.FullName)
    }
}

# ---------------------------------------------------------------------------
# Vosk (offline STT / wake word) – no compilation needed, .so comes from AAR.
if (-not $SkipVosk) {
    $voskAar  = Join-Path $tmpRoot "vosk-android-$VoskVersion.aar"
    $voskUrl  = "https://repo1.maven.org/maven2/com/alphacep/vosk-android/$VoskVersion/vosk-android-$VoskVersion.aar"
    if (-not (Test-Path (Join-Path $LibOutDir "libvosk.so"))) {
        Invoke-DownloadAndExtractAar $voskUrl $voskAar $LibOutDir "libvosk.so"
        Write-Host "  + libvosk.so ($VoskVersion)"
    }
}

# ---------------------------------------------------------------------------
# ONNX Runtime (Piper TTS + optional NMT translation in Faz 3/4).
if (-not $SkipOnnx) {
    $ortAar = Join-Path $tmpRoot "onnxruntime-android-$OnnxRuntimeVersion.aar"
    $ortUrl = "https://repo1.maven.org/maven2/com/microsoft/onnxruntime/onnxruntime-android/$OnnxRuntimeVersion/onnxruntime-android-$OnnxRuntimeVersion.aar"
    if (-not (Test-Path (Join-Path $LibOutDir "libonnxruntime.so"))) {
        Invoke-DownloadAndExtractAar $ortUrl $ortAar $LibOutDir "libonnxruntime.so"
        Write-Host "  + libonnxruntime.so ($OnnxRuntimeVersion)"
    }
}

Write-Host ""
Write-Host "Native kütüphaneler hazır: $LibOutDir"
Get-ChildItem $LibOutDir -Filter "*.so" | Select-Object -ExpandProperty Name
Write-Host ""
Write-Host "Piper (TTS) motoru Faz 3 kapsamındadır ve ayrı derlenir."