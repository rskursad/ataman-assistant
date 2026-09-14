# ataman-assistant

A fully **offline** voice assistant for Turkish: wake-word trigger + on-device LLM
(llama.cpp/GGUF) + Vosk speech-to-text + Piper TTS. Long-term memory and
translation live behind `ITranslator` (Phase 4) and are not implemented yet.

Everything runs on the device — no cloud, no API keys. The default wake word is
**"asistan"**.

## Features

- **Wake-word detection** — "asistan" via a slim Vosk Turkish recognizer.
- **Speech-to-text** — Vosk (Turkish small model).
- **On-device LLM** — Qwen2.5 Instruct (GGUF) served locally:
  - Desktop/Windows: LLamaSharp 0.27.0.
  - Android: thin P/Invoke over `libjllama.so` (`LlamaDotNetModel`), no LLamaSharp,
    Ollama, or Termux — a single self-contained APK.
- **Text-to-speech**
  - Desktop/Windows: Piper (`tr_TR-dfki-medium`).
  - Android: Android's speech engine (Google TTS, tr-TR offline voice) via
    `AndroidTextToSpeech`.
- **UI** — Avalonia (`ShellView` with Assistant / Settings tabs).

## Repository layout

| Path | Description |
| --- | --- |
| `Ataman.Core/` | Platform-independent engines and abstractions (Vosk STT/spotter, LLamaSharp LLM, Piper TTS interfaces, `ModelManager`, settings/catalogs, `AppPaths`) |
| `ataman-assistant/` | Shared Avalonia UI (`ShellView`: Assistant/Settings tabs), `AppServices`, view models |
| `ataman-assistant.Desktop/` | Windows host: NAudio (mic/audio), `PiperTts` (PiperSharp → piper1), LLamaSharp registration. **Primary development/demo platform.** |
| `ataman-assistant.Android/` | Mobile host. **Prebuilt-AAR strategy** (no NDK): `libs/arm64-v8a/libjllama.so` (java-llama.cpp 5.1.0 = llama.cpp b10682) + `libvosk.so`. TTS: Android Google TTS. Mic: AudioRecord. |
| `ataman-assistant.iOS/`, `ataman-assistant.Browser/` | Mobile/web hosts (Browser deferred: `wasm-tools` workload missing) |
| `tools/SmokeTest/` | End-to-end verification (Vosk + LLM + Piper, exit 0) |

## Requirements

- .NET 10 SDK (10.0.401)
- Android: an arm64 device (e.g., Samsung Galaxy A24); models are downloaded on
  first run (or pre-installed directly).
- Visual Studio is optional but its MSBuild server nodes can lock `obj/bin`
  outputs (see AGENTS.md).

## Build & run

```bash
# Desktop (Windows)
dotnet build ataman-assistant.Desktop\AtamanAssistant.Desktop.csproj -c Debug

# Android (compile only)
dotnet build ataman-assistant.Android\AtamanAssistant.Android.csproj -c Debug -f net10.0-android

# Android (build + deploy + run on a USB-connected device)
dotnet build ataman-assistant.Android\AtamanAssistant.Android.csproj -c Debug -t:Run -nodeReuse:false

# End-to-end smoke test (downloads models into %TEMP%\ataman-smoke)
dotnet run --project tools\SmokeTest\SmokeTest.csproj -c Debug
```

Always use `-nodeReuse:false` when Visual Studio is running, otherwise MSBuild
server nodes may hold file locks on `obj/bin`.

## Models

Models are downloaded into `models\vosk`, `models\llm`, and `models\tts` under
the app data directory on first run, keeping the APK light:

- **LLM (default):** Qwen2.5-1.5B-Instruct `qwen2.5-1.5b-instruct-q4_k_m`
  (`ModelCatalog`); tiny fallback `qwen2.5-0.5b…`. **On Android the default is
  `qwen2.5-0.5b-instruct-q4_k_m`** (low memory/CPU footprint).
- **STT:** Vosk Turkish small model.
- **TTS (Desktop):** Piper voices only from rhaspy/piper-voices `main`:
  `tr_TR-dfki-medium`, `en_US-lessac-medium`, `en_US-lessac-low`, `en_US-ryan-high`.

## Roadmap

- Phase 1: Voice chat loop (wake word → STT → LLM → TTS) — in progress.
- Phase 4: `ITranslator` translation layer (awaiting).
- Phase 7: WebAssembly (Browser) build (blocked on the `wasm-tools` workload).

## License

See [LICENSE.txt](LICENSE.txt).