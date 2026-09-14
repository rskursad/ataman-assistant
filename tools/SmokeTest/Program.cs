using Ataman.Core.Model;
using Ataman.Core.Settings;
using Ataman.Core.Voice;
using PiperSharp;

var root = Path.Combine(Path.GetTempPath(), "ataman-smoke");
var voiceManager = new ModelManager(Path.Combine(root, "models", "vosk"));
var llmManager = new ModelManager(Path.Combine(root, "models", "llm"));

// ---- Vosk: STT + wake word ----------------------------------------------
var modelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-tr-0.3.zip";
Console.WriteLine("[1/4] Vosk model kontrol / indirme…");
var modelDir = await voiceManager.DownloadAndExtractZipAsync(modelUrl, Path.Combine(root, "vosk"));
Console.WriteLine($"      Model: {modelDir}");

Console.WriteLine("[2/4] VoskSpeechToText yükleniyor…");
var stt = new VoskSpeechToText();
await stt.LoadModelAsync(modelDir);
var silence = new byte[AudioFormat.SampleRate * 2];
stt.Feed(silence);
stt.Reset();

Console.WriteLine("[3/4] VoskKeywordSpotter (grammar)…");
var spotter = new VoskKeywordSpotter();
await spotter.LoadAsync(modelDir);
spotter.SetKeywords(new[] { "asistan" });
spotter.Feed(silence);
spotter.Reset();
Console.WriteLine("      OK — STT + wake-word hazır.");

// ---- LLM: real llama.cpp inference --------------------------------------
var info = ModelCatalog.Qwen25_0_5B;
var ggufPath = llmManager.GetFilePath(info.Id + ".gguf");
Console.WriteLine($"[4/4] GGUF {(llmManager.IsDownloaded(ggufPath) ? "mevcut" : "indiriliyor")}: {info.Id}");
await llmManager.DownloadAsync(info.DownloadUrl, ggufPath);
Console.WriteLine($"      GGUF hazır: {new FileInfo(ggufPath).Length / 1_000_000.0:0} MB");

Console.WriteLine("      LLM yükleniyor + çıkarım…");
await using var model = new LLamaSharpModel();
var report = await model.LoadAsync(new ModelSpec(info.Id, ggufPath, ContextTokens: 1024, Threads: Environment.ProcessorCount));
Console.WriteLine($"      Yüklendi: {report.Bytes / 1_000_000.0:0} MB");

var sb = new System.Text.StringBuilder();
model.TokenProduced += (_, t) => sb.Append(t);
var answer = await model.CompleteAsync(
    new[]
    {
        new ChatMessage(ChatRole.System, "Kısa ve net Türkçe yanıt ver."),
        new ChatMessage(ChatRole.User, "Bir cümleyle kendini tanıt."),
    },
    new GenerationOptions(Temperature: 0.7f, MaxTokens: 96));
Console.WriteLine($"      YANIT: {answer}");
Console.WriteLine($"      Toplam {sb.Length} karakter token olarak aktı.");

// ---- Piper TTS: piper.exe + TR ses modeli + inference --------------------
Console.WriteLine("[5/5] Piper TTS hazırlanıyor…");
var piperRuntime = Path.Combine(root, "piper");
var ttsRoot = Path.Combine(root, "models", "tts");
Directory.CreateDirectory(piperRuntime);
Directory.CreateDirectory(ttsRoot);

var piperExe = Path.Combine(piperRuntime, "piper", "piper.exe");
if (File.Exists(piperExe) is false)
{
    Console.WriteLine("      piper çalıştırıcısı indiriliyor…");
    var bundle = await PiperSharp.PiperDownloader.DownloadPiper();
    await Task.Run(() => bundle.ExtractPiper(piperRuntime));
}
Console.WriteLine($"      Çalıştırıcı: {piperExe} ({(File.Exists(piperExe) ? new FileInfo(piperExe).Length / 1_000_000.0 : 0):0.00} MB)");

var voiceKey = VoiceCatalog.TurkishDfki.Id;
var voiceDir = Path.Combine(ttsRoot, voiceKey);
if (File.Exists(Path.Combine(voiceDir, "model.json")) is false)
{
    Console.WriteLine($"      Ses ({voiceKey}) indiriliyor…");
    var voiceInfo = await PiperSharp.PiperDownloader.GetModelByKey(voiceKey);
    if (voiceInfo is null) throw new InvalidOperationException("Ses modeli bulunamadı!");
    await voiceInfo.DownloadModel(ttsRoot);
}
var voiceModel = await PiperSharp.Models.VoiceModel.LoadModel(voiceDir);

var sampleRate = 22050;
var onnxJsonName = voiceModel.Files.Keys.FirstOrDefault(f => f.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase));
if (onnxJsonName is not null)
{
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(voiceDir, Path.GetFileName(onnxJsonName))));
    if (doc.RootElement.TryGetProperty("audio", out var audio)
        && audio.TryGetProperty("sample_rate", out var rate))
    {
        sampleRate = rate.GetInt32();
    }
}

Console.WriteLine("      Seslendirme testi (Türkçe)…");
var tts = new PiperSharp.PiperProvider(new PiperSharp.Models.PiperConfiguration
{
    ExecutableLocation = piperExe,
    WorkingDirectory = Path.GetDirectoryName(piperExe)!,
    Model = voiceModel,
});
var pcm = await tts.InferAsync("Merhaba, ben Ataman. Bugün size nasıl yardımcı olabilirim?", PiperSharp.Models.AudioOutputType.Raw);
Console.WriteLine($"      PCM: {pcm.Length} bayt, {sampleRate} Hz, {(pcm.Length / 2 / (double)sampleRate):0.0} sn");

Console.WriteLine("SONUÇ: OK — Vosk (STT+wake), llama.cpp/c# ve Piper TTS uçtan uca çalışıyor.");