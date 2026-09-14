# AGENTS.md — Ataman Assistant

Tamamen çevrimdışı sesli asistan: wake-word tetikleme + cihaz içi LLM (llama.cpp/GGUF ile)
+ Vosk STT + Piper TTS. Avályona UI. C# (.NET 10, SDK 10.0.401).

## Yapı

- `Ataman.Core/` — platform-bağımsız motorlar + soyutlamalar (Vosk STT/spotter, LLamaSharp LLM,
  Piper TTS arayüzleri, ModelManager, Ayarlar/Kataloglar, AppPaths).
- `ataman-assistant/` — ortak Avalonia UI (ShellView: Asistan/Ayarlar sekmeleri), AppServices, ViewModel'ler.
- `ataman-assistant.Desktop/` — Windows başlığı: NAudio (mikrofon/ses), PiperTts (PiperSharp → piper1),
  LLamaSharp kayıtları. **Ana geliştirme/gösterim platformu.**
- `ataman-assistant.Android/` — mobil başlık. **Prebuilt-AAR stratejisi** (NDK yok): `libs/arm64-v8a/libjllama.so`
  (java-llama.cpp 5.1.0 = llama.cpp b10682) + `libvosk.so` (com.alphacephei:vosk-android:0.3.75). LLM:
  `Ataman.Core/Model/LlamaDotNetModel.cs` (ince P/Invoke). TTS: Android Google TTS (tr-TR). Mikrofon: AudioRecord.
  `.iOS/`, `.Browser/` — mobil/web başlıkları (Browser: `wasm-tools` eksik → ertelendi).
- `tools/SmokeTest/` — uçtan uca doğrulama (Vosk + LLM + Piper, exit 0).

## Komutlar

- `dotnet build ataman-assistant.Desktop\AtamanAssistant.Desktop.csproj -c Debug`
- `dotnet build ataman-assistant.Android\AtamanAssistant.Android.csproj -c Debug -f net10.0-android`
- `dotnet run --project tools\SmokeTest\SmokeTest.csproj -c Debug` — uçtan uca smoke (istenen model dosyalarını `%TEMP%\ataman-smoke` altına indirir)
- `dotnet build -t:Run -f net10.0-android` (USB'li cihaz) veya `adb install` ile APK

## Temel kararlar (değiştirmeden önce düşün)

- Varsayılan LLM: Qwen2.5-1.5B-Instruct `qwen2.5-1.5b-instruct-q4_k_m` (`ModelCatalog`), kamiş `qwen2.5-0.5b…`.
  **Telefonda varsayılan `qwen2.5-0.5b-instruct-q4_k_m`** (önemsiz bellek/CPU); A24 cihazında kurulu GGUF hazır.
- **Android LLM:** `LlamaDotNetModel` = doğrudan `libjllama.so` üzerinden çağrılan **inale P/Invoke llama.cpp C API
  (b10682)** — LLamaSharp yok, Ollama/Termux yok (kullanıcı kararı: tek bağımsız APK). Struct'lar b10682
  `include/llama.h`'e sabitlenmiştir; runtime self-check'ler (`llama_vocab_n_tokens`, `llama_n_ctx`) ABI
  uyumsuzluğunu yüksek sesle çökerterek yakalar.
- **Android TTS:** üst üste Android motoru (Google TTS, cihazda `split_config.tr.apk` → tr-TR offline ses).
  `AndroidTextToSpeech` ses kendisi oynatır; `AudioFrameProduced` Android'de hiç üretilmez. Ses eşlemesi:
  `tr` → tr-TR, `en` → en-US (SetLanguage fallback: device default).
- Varsayılan wake word: `asistan` (Vosk küçük TR sözlüğünde `ataman` yok).
- Amaç: ilk sürüm yalnızca sesli sohbet; çeviri katmanı `ITranslator` (Faz 4) bekliyor.
- Desktop TTS: **piper1** (rhasspy/piper standalone exe) + `tr_TR-dfki-medium`. Sesler **yalnızca**
  rhasspy/piper-voices `main` dallarındaki `voices.json` içinden seçilebilir — `fahrettin`/`amy` kaldırıldı.
  Mevcut: `tr_TR-dfki-medium`, `en_US-lessac-medium`, `en_US-lessac-low`, `en_US-ryan-high`.
  PiperTts `--output-raw` kullanır; örnekleme hızı her sesin `.onnx.json` → `audio.sample_rate`'inden okunur
  (üst düzey `audio` yoktur).
- Sürümler (kilitli): LLamaSharp 0.27.0 (+ Backend.Cpu), Vosk **0.3.38** (nuget en son; 0.3.75 yok — NU1102),
  NAudio 2.2.1, PiperSharp 1.0.7, Avalonia 12.1.2.

## Tuzaklar (önceden yaşanmış hatalar)

1. **`ModelManager.DownloadAndExtractZipAsync` çıkarma atlama hatası (böcek düzeltildi):**
   zip, çıkarma klasörünün İÇİNDE saklanır; "klasör var mı" diye kısayol yapma. Çıkarmayı yalnızca,
   hedefte gerçek bir model klasörü varsa atla (`final.mdl` kökte VEYA `am/final.mdl` VEYA `conf/model.conf`).
   Eksik model klasörü Vosk tanıyıcısında native çökme (`ExecutionEngineException`/0xc0000005) yapar.
2. **Vosk grammar recognizer:** gramer **düz JSON string dizisi** olmalı (`["asistan"]`); `{"words":[…]}` formatı
   `new_VoskRecognizerGrm`'de native olarak çökertir. Ayrıca Vosk'un küçük modelleri **düz dizin** yapısındadır
   (`final.mdl` kökte), `am/conf` değil.
3. **VS build-server kilidi:** Visual Studio'nun MSBuild düğümleri `obj/bin` çıktılarını kilitleyebilir
   ("Dosya şunun tarafından kilitlendi: Microsoft Visual Studio Insiders (PID)…", XALNS7024). Çözüm:
   `dotnet build … -nodeReuse:false`; olmazsa devenv'e dokunmadan bayat MSBuild düğümlerini öldür
   (`Get-Process MSBuild`) → yeniden derle.
4. **Android native — JNI_OnLoad çökmesi (düzeltildi):** `libjllama.so` (java-llama.cpp) `JNI_OnLoad`'unda
   `net.ladenthin.llama.LlamaModel` Java sınıfını arar; bu sınıf bizim APK'da yok ve sınıf yükleyici başlangıçta
   TÜM paketlenmiş `.so`'lari yüklerken `FindClass` → FATAL verir. Çözüm: `.so` içindeki `JNI_OnLoad` sembol adını
   hem `.dynstr` hem `.strtab`'de (`JNI_OnLoad\0` ilk baytı `j` yaparak) sil — P/Invoke sadece `llama_*` sembollerini
   kullandığı için Java katmanı gereksiz. `libvosk.so`'da aynı sorun yok.
   Prosedür: iki konumu bul (`Select-String`/hex), patchle, yeniden build.
5. **Android platform dosyaları (A24'te çalışıyor):** `MainApplication.OnCreate` → AppServices fabrikaları
   (`LlamaDotNetModel`, `VoskSpeechToText`, `VoskKeywordSpotter`, `AndroidAudioSource`, `AndroidTextToSpeech`);
   `MainActivity` başlangıçta RECORD_AUDIO runtime izni ister. Manifest'e `RECORD_AUDIO` + `uses-feature microphone`
   gerekli. Not: `AtamanAssistant.Android` ad alanı içinde `Android.X` çözümlenmez → `global::Android.X` kullan.
   A24'te test durumu: uygulama açılıyor, 0.5B GGUF + Vosk TR küçük model kurulu.
6. **PiperSharp dizinler:** piper zip'i, üstte `piper/` klasörüyle çıkar → exe `<base>/piper/piper.exe`
   (extract edilirken bir düzey eksik kalır). Ses modeli `<tts>/<key>/` altına `model.json` + `.onnx` + `.onnx.json`
   olarak iner.
7. **Browser build:** `wasm-tools` workload eksik → `NETSDK1147`; Faz 7'ye bırakıldı.
8. **Kötü faktör/gözlem:** Desktop/SmokeTest bin'lerinde `libggml*.dll` görünmüyor ama LLM yine de çalışır
   (LLamaSharp kendi native yükleme yolunu kullanır). Doğrulama için daima `tools/SmokeTest` exit 0'ı baz al.
9. **Android Fast-Deployment — bayat `__override__`:** Debug APK'de yönetilen derleme YOKTUR
   (`EmbedAssembliesIntoApk=false`); kod cihazda `files/.__override__/arm64-v8a/*.dll` içinden çalışır.
   Çıplak `adb install -r` yalnızca native `.so`'lari günceller, DLL'leri bayat bırakır → "Ses girişi bu platforma
   yok" + "LLM motoru bu platforma bağlanmadı" + yanıtsızlık. Her zaman `dotnet build … -t:Run -nodeReuse:false`.
   Bayat kalırsa: `adb shell am force-stop` + `run-as <pkg> rm -rf files/.__override__ files/.__tools__`.
10. **b10682 `llama_model_default_params()` varsayılanları:** `n_gpu_layers=-1` (hepsini offload), `split_mode=
    LAYER(1)`, `load_mode=AUTO(-1)`, `lazy_mode=AUTO(1)`, `main_gpu=0`, `vocab_only=false`, `use_extra_bufts=true`.
    Bunlardan emin olmadan "0 bekliyordu" diye self-check koyma — `AssertModelParams` gerçek varsayılanlara bakar.

## Ayarlar/depolama

- `AppPaths.BaseDirectory` varsayılanı `AppContext.BaseDirectory\ataman` (yani dev'de `bin\…\ataman/`).
  Modeller `models\vosk`, `models\llm`, `models\tts` altına ilk çalıştırmada iner, APK/uç küçük kalır.
- Ayarlar JSON olarak `data/` altında saklanır (`JsonSettingsStore`).