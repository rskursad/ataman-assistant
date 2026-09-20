# AGENTS.md — Ataman Assistant

Fully offline voice assistant: wake-word trigger + on-device LLM (llama.cpp/GGUF)
+ Vosk STT + Piper TTS. Avalonia UI. C# (.NET 10, SDK 10.0.401).

## Layout

- `Ataman.Core/` — platform-independent engines and abstractions (Vosk STT/spotter, LLamaSharp LLM,
  Piper TTS interfaces, ModelManager, settings/catalogs, AppPaths).
- `ataman-assistant/` — shared Avalonia UI (ShellView: Assistant/Settings tabs), AppServices, view models.
- `ataman-assistant.Desktop/` — Windows host: NAudio (mic/audio), PiperTts (PiperSharp → piper1),
  LLamaSharp registration. **Primary development/demo platform.**
- `ataman-assistant.Android/` — mobile host. **Prebuilt-AAR strategy** (no NDK): `libs/arm64-v8a/libjllama.so`
  (java-llama.cpp 5.1.0 = llama.cpp b10682) + `libvosk.so` (com.alphacephei:vosk-android:0.3.75). LLM:
  `Ataman.Core/Model/LlamaDotNetModel.cs` (thin P/Invoke). TTS: Android Google TTS (tr-TR). Mic: AudioRecord.
  `.iOS/`, `.Browser/` — mobile/web hosts (Browser: `wasm-tools` missing → deferred).
- `tools/SmokeTest/` — end-to-end verification (Vosk + LLM + Piper, exit 0).

## Commands

- `dotnet build ataman-assistant.Desktop\AtamanAssistant.Desktop.csproj -c Debug`
- `dotnet build ataman-assistant.Android\AtamanAssistant.Android.csproj -c Debug -f net10.0-android`
- `dotnet run --project tools\SmokeTest\SmokeTest.csproj -c Debug` — end-to-end smoke (downloads required models to `%TEMP%\ataman-smoke`)
- `dotnet build -t:Run -f net10.0-android` (USB-attached device) or `adb install` with APK

## Core decisions (reconsider before changing)

- Default LLM: Qwen2.5-1.5B-Instruct `qwen2.5-1.5b-instruct-q4_k_m` (`ModelCatalog`), small fallback `qwen2.5-0.5b…`.
  **On the phone the default is `qwen2.5-0.5b-instruct-q4_k_m`** (low memory/CPU); the GGUF is already installed on the A24 device.
- **Android LLM:** `LlamaDotNetModel` calls the llama.cpp C API (b10682) directly through **thin P/Invoke on
  `libjllama.so`** — no LLamaSharp, no Ollama/Termux (user decision: single self-contained APK). Structs are pinned to
  the b10682 `include/llama.h`; runtime self-checks (`llama_vocab_n_tokens`, `llama_n_ctx`) catch ABI mismatches by
  crashing loudly.
- **Android TTS:** the Android speech engine (Google TTS, device `split_config.tr.apk` → tr-TR offline voice).
  `AndroidTextToSpeech` plays audio itself; `AudioFrameProduced` is never raised on Android. Voice mapping:
  `tr` → tr-TR, `en` → en-US (SetLanguage fallback: device default).
- Default wake word: `asistan` (the slim Vosk TR dictionary has no `ataman`).
- Translation layer: `LlmTranslator` implements `ITranslator` (Phase 4 completed) for zero-shot local translation to English.
- Desktop TTS: **piper1** (rhaspy/piper standalone exe) + `tr_TR-dfki-medium`. Voices are selectable **only** from
  the rhaspy/piper-voices `main` branch `voices.json` — `fahrettin`/`amy` were removed. Available:
  `tr_TR-dfki-medium`, `en_US-lessac-medium`, `en_US-lessac-low`, `en_US-ryan-high`.
  PiperTts uses `--output-raw`; the sample rate is read from each voice's `.onnx.json` → `audio.sample_rate`
  (there is no top-level `audio` block).
- Versions (locked): LLamaSharp 0.27.0 (+ Backend.Cpu), Vosk **0.3.38** (latest on nuget; 0.3.75 missing — NU1102),
  NAudio 2.2.1, PiperSharp 1.0.7, Avalonia 12.1.2.

## Pitfalls (already encountered)

1. **`ModelManager.DownloadAndExtractZipAsync` extraction-skip bug (fixed):**
   the zip is stored INSIDE the extraction folder; do not shortcut by checking "does the folder exist". Skip
   extraction only when a real model folder exists at the destination (`final.mdl` at root OR `am/final.mdl` OR
   `conf/model.conf`). A missing model folder makes the Vosk recognizer crash natively
   (`ExecutionEngineException`/0xc0000005).
2. **Vosk grammar recognizer:** the grammar must be a **plain JSON string array** (`["asistan"]`); the `{"words":…}`
   format crashes natively in `new_VoskRecognizerGrm`. Also, Vosk small models use a **flat directory** layout
   (`final.mdl` at root), not `am/conf`.
3. **VS build-server lock:** Visual Studio MSBuild nodes can lock `obj/bin` outputs
   ("The file is locked by: Microsoft Visual Studio Insiders (PID)…", XALNS7024). Fix: `dotnet build … -nodeReuse:false`;
   if that fails, kill stale MSBuild nodes without touching devenv (`Get-Process MSBuild`) → rebuild.
4. **Android native — JNI_OnLoad crash (fixed):** `libjllama.so` (java-llama.cpp) `JNI_OnLoad` looks up the
   `net.ladenthin.llama.LlamaModel` Java class; that class is absent from our APK and the class loader FATALs on
   `FindClass` while loading ALL packaged `.so`s at startup. Fix: strip the `JNI_OnLoad` symbol name in the `.so`
   (make the first byte of `JNI_OnLoad\0` `j`) in BOTH `.dynstr` and `.strtab` — P/Invoke only uses `llama_*`
   symbols, so the Java layer is unnecessary. `libvosk.so` has no such issue.
   Procedure: locate the two occurrences (hex search), patch, rebuild.
5. **Android platform files (works on A24):** `MainApplication.OnCreate` → AppServices factories
   (`LlamaDotNetModel`, `VoskSpeechToText`, `VoskKeywordSpotter`, `AndroidAudioSource`, `AndroidTextToSpeech`);
   `MainActivity` requests the RECORD_AUDIO runtime permission on startup. The manifest needs `RECORD_AUDIO` +
   `uses-feature microphone`. Note: inside the `AtamanAssistant.Android` namespace `Android.X` does not resolve →
   use `global::Android.X`. A24 test status: app opens, 0.5B GGUF + Vosk TR small model installed.
6. **PiperSharp directories:** the piper zip extracts with a top-level `piper/` folder → exe at `<base>/piper/piper.exe`
   (a level is lost when extracting). The voice model downloads to `<tts>/<key>/` as `model.json` + `.onnx` + `.onnx.json`.
7. **Browser build:** the `wasm-tools` workload is missing → `NETSDK1147`; deferred to Phase 7.
8. **Bad factor/observation:** `libggml*.dll` does not appear in Desktop/SmokeTest bins but the LLM still works
   (LLamaSharp uses its own native loading path). Always base verification on `tools/SmokeTest` exit 0.
9. **Android Fast-Deployment — stale `__override__`:** Debug APKs contain NO managed assemblies
   (`EmbedAssembliesIntoApk=false`); code runs from `files/.__override__/arm64-v8a/*.dll` on the device.
   A bare `adb install -r` only updates native `.so`s and leaves the DLLs stale → "Audio input is not available on
   this platform" + "LLM engine not wired up on this platform" + no response. Always use
   `dotnet build … -t:Run -nodeReuse:false`. If stale: `adb shell am force-stop` + `run-as <pkg> rm -rf files/.__override__ files/.__tools__`.
10. **b10682 `llama_model_default_params()` defaults:** `n_gpu_layers=-1` (offload all), `split_mode=LAYER(1)`,
    `load_mode=AUTO(-1)`, `lazy_mode=AUTO(1)`, `main_gpu=0`, `vocab_only=false`, `use_extra_bufts=true`. Do not add a
    self-check expecting "0" without being sure — `AssertModelParams` checks the actual defaults.

## Settings/storage

- `AppPaths.BaseDirectory` defaults to `AppContext.BaseDirectory\ataman` (i.e. `bin\…\ataman/` in dev). Models
  download into `models\vosk`, `models\llm`, `models\tts` on first run; APK/release stays small.
- Settings are stored as JSON under `data/` (`JsonSettingsStore`).