using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ataman.Core.Model;

/// <summary>
/// llama.cpp executor through the thin C API exported by the prebuilt
/// <c>jllama</c> shared library (java-llama.cpp 5.1.0, llama.cpp b10682).
/// Struct layouts below are pinned to b10682 <c>include/llama.h</c>; the
/// runtime sanity checks in <see cref="LoadAsync"/> fail loudly if a future
/// library ships an incompatible ABI.
/// </summary>
public sealed class LlamaDotNetModel : ILanguageModel
{
    private static readonly object BackendGate = new();
    private static bool _backendInit;

    private readonly object _gate = new();
    private IntPtr _model;
    private IntPtr _ctx;
    private IntPtr _vocab;
    private ModelSpec? _spec;
    private bool _disposed;

    public bool IsLoaded => _ctx != IntPtr.Zero;

    public event EventHandler<string>? TokenProduced;
    public event EventHandler<ToolCall>? ToolCallProduced;

    public Task<ModelLoadReport> LoadAsync(ModelSpec spec, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_ctx != IntPtr.Zero && _spec?.FilePath == spec.FilePath)
            {
                return Task.FromResult(new ModelLoadReport(spec.Id, new FileInfo(spec.FilePath).Length, 0));
            }

            UnloadLocked();
        }

        return Task.Run(() =>
        {
            EnsureBackend();

            if (!File.Exists(spec.FilePath))
            {
                throw new FileNotFoundException("GGUF model dosyası bulunamadı.", spec.FilePath);
            }

            // Native C strings are UTF-8.
            var pathBytes = Encoding.UTF8.GetBytes(spec.FilePath + '\0');

            var mparams = LlamaModelDefaultParams();
            AssertModelParams(mparams);
            var modelPtr = LlamaModelLoadFromFile(pathBytes, mparams);
            if (modelPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("llama_model_load_from_file başarısız oldu (dosya bozuk olabilir).");
            }

            Console.Error.WriteLine($"[jllama] model yüklendi: {spec.Id} ({new FileInfo(spec.FilePath).Length} bayt)");

            var vocab = LlamaModelGetVocab(modelPtr);
            var nVocab = LlamaVocabNTokens(vocab);
            if (nVocab < 500 || nVocab > 2_000_000)
            {
                LlamaModelFree(modelPtr);
                throw new InvalidOperationException($"Vocab sayısı anormal ({nVocab}) — lib ABI'si uyumsuz olabilir.");
            }

            Console.Error.WriteLine($"[jllama] vocab n_tokens={nVocab}");

            var cparams = LlamaContextDefaultParams();
            cparams.NCtx = spec.ContextTokens > 0 ? (uint)spec.ContextTokens : 1024u;
            cparams.NThreads = spec.Threads > 0 ? spec.Threads : Environment.ProcessorCount;
            cparams.NThreadsBatch = cparams.NThreads;

            var ctxPtr = LlamaNewContextWithModel(modelPtr, cparams);
            if (ctxPtr == IntPtr.Zero)
            {
                LlamaModelFree(modelPtr);
                throw new InvalidOperationException("llama_new_context_with_model başarısız oldu (yetersiz bellek olabilir).");
            }

            var nCtx = LlamaNCtx(ctxPtr);
            if (nCtx == 0 || nCtx != cparams.NCtx)
            {
                LlamaFree(ctxPtr);
                LlamaModelFree(modelPtr);
                throw new InvalidOperationException($"n_ctx tutarsız ({nCtx} beklenen {cparams.NCtx}) — lib ABI'si uyumsuz.");
            }

            Console.Error.WriteLine($"[jllama] context hazır: n_ctx={nCtx}, threads={cparams.NThreads}");

            lock (_gate)
            {
                _model = modelPtr;
                _ctx = ctxPtr;
                _vocab = vocab;
                _spec = spec;
            }

            return new ModelLoadReport(spec.Id, new FileInfo(spec.FilePath).Length, 0);
        }, ct);
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options,
        CancellationToken ct = default)
    {
        var opts = options ?? GenerationOptions.Default;
        var modelId = _spec?.Id;
        if (modelId is null)
        {
            throw new InvalidOperationException("Model yüklenmemiş.");
        }

        var prompt = ChatTemplate.Apply(modelId, messages);
        if (prompt.Length == 0)
        {
            throw new InvalidOperationException("Prompt boş üretildi.");
        }

        Console.Error.WriteLine($"[jllama] tamamlama: promptUzunluk={prompt.Length}, mesaj={messages.Count}");
        var promptTokens = Tokenize(prompt, addSpecial: false);
        if (promptTokens.Length == 0)
        {
            throw new InvalidOperationException("Prompt boş token üretti.");
        }

        Console.Error.WriteLine($"[jllama] tamamlama başladı: prompt={promptTokens.Length} token, eos={LlamaVocabEos(_vocab)}, maxTokens={opts.MaxTokens}");
        var eosToken = LlamaVocabEos(_vocab);

        var sampler = CreateSampler(opts.Temperature);
        try
        {
            var sb = new StringBuilder();
            int decoded = DecodeTokens(promptTokens);
            if (decoded < 0)
            {
                throw new InvalidOperationException($"llama_decode prompt hatası ({decoded}).");
            }

            if (LlamaGetLogitsIth(_ctx, -1) == IntPtr.Zero)
            {
                throw new InvalidOperationException("llama_decode çıktı üretmedi (logits NULL).");
            }

            var single = new int[1];
            for (int i = 0; i < opts.MaxTokens; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (LlamaGetLogitsIth(_ctx, -1) == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"decode[{i}] çıktı üretmedi (logits NULL).");
                }

                var token = LlamaSamplerSample(sampler, _ctx, -1);
                if (token == -1 || (eosToken >= 0 && token == eosToken))
                {
                    Console.Error.WriteLine($"[jllama] durduruldu (token={token}, i={i})");
                    break;
                }

                var piece = TokenToPiece(token);
                sb.Append(piece);
                TokenProduced?.Invoke(this, piece);

                single[0] = token;
                decoded = DecodeTokens(single);
                if (decoded < 0)
                {
                    break;
                }
            }

            Console.Error.WriteLine($"[jllama] tamamlama bitti: {sb.Length} karakter, {sb.ToString().Length} uzunluk");
            return sb.ToString().Trim();
        }
        finally
        {
            LlamaSamplerFree(sampler);
        }
    }

    public Task<ToolCall?> CompleteToolCallAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default)
    {
        // Faz 2: same heuristic JSON tool-call parsing as the LLamaSharp path.
        var toolsJson = JsonSerializer.Serialize(tools.Select(t => new
        {
            t.Name,
            t.Description,
            parameters = t.ParametersSchemaJson,
        }));
        var instructions = new List<ChatMessage>(messages)
        {
            new(ChatRole.User,
                "Available tools (JSON array): " + toolsJson + "\n" +
                "If you must call a tool, answer ONLY with a JSON object: " +
                "{\"name\": \"tool_name\", \"arguments\": {...}}. Otherwise answer normally."),
        };

        return CompleteAsync(instructions, new GenerationOptions(Temperature: 0.1f, MaxTokens: 128), ct)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    throw t.Exception!;
                }

                var text = t.Result;
                TrimToolWrapper(ref text);
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (!doc.RootElement.TryGetProperty("name", out var nameEl))
                    {
                        return null;
                    }

                    var name = nameEl.GetString();
                    var args = doc.RootElement.TryGetProperty("arguments", out var argsEl)
                        ? argsEl.GetRawText()
                        : "{}";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var call = new ToolCall(name, args, text);
                        ToolCallProduced?.Invoke(this, call);
                        return call;
                    }
                }
                catch (JsonException)
                {
                }

                return null;
            }, ct);
    }

    public Task UnloadAsync()
    {
        lock (_gate)
        {
            UnloadLocked();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            UnloadLocked();
        }

        return ValueTask.CompletedTask;
    }

    private void UnloadLocked()
    {
        if (_ctx != IntPtr.Zero)
        {
            LlamaFree(_ctx);
        }

        if (_model != IntPtr.Zero)
        {
            LlamaModelFree(_model);
        }

        _ctx = IntPtr.Zero;
        _model = IntPtr.Zero;
        _vocab = IntPtr.Zero;
        _spec = null;
    }

    private void EnsureBackend()
    {
        _ = BackendGate;
        lock (BackendGate)
        {
            if (!_backendInit)
            {
                LlamaBackendInit();
                _backendInit = true;
            }
        }
    }

    private IntPtr CreateSampler(float temperature)
    {
        // b10682 zinciri: seçimi sonunda llama_sampler_init_dist yapar — onsuz
        // cur_p.selected -1 kalır ve llama_sampler_sample GGML_ASSERT ile çöker.
        var chain = LlamaSamplerChainInit(LlamaSamplerChainDefaultParams());
        LlamaSamplerChainAdd(chain, LlamaSamplerInitTopK(40));
        LlamaSamplerChainAdd(chain, LlamaSamplerInitTopP(0.9f, 1));
        LlamaSamplerChainAdd(chain, LlamaSamplerInitMinP(0.05f, 1));
        LlamaSamplerChainAdd(chain, LlamaSamplerInitTemp(temperature));
        // Rastgele tohum: LLAMA_DEFAULT_SEED (0xFFFFFFFF) ile aynı.
        LlamaSamplerChainAdd(chain, LlamaSamplerInitDist(uint.MaxValue));
        return chain;
    }

    private int[] Tokenize(string text, bool addSpecial)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = LlamaTokenize(_vocab, bytes, bytes.Length, null, 0, addSpecial, parseSpecial: true);
        if (n == 0)
        {
            Console.Error.WriteLine($"[jllama] tokenize: boş metin ({bytes.Length} bayt)");
            return Array.Empty<int>();
        }

        if (n < 0)
        {
            // b10682 llama_vocab::tokenize: yetersiz tampon halinde -(ihtiyaç duyulan
            // token sayısı) döndürür (llama-vocab.cpp:4136); NULL/0 ile "yoklama" artık
            // pozitif sayı değil, negatif gerekli-sayı üretir.
            n = -n;
        }

        var tokens = new int[n];
        var written = LlamaTokenize(_vocab, bytes, bytes.Length, tokens, n, addSpecial, parseSpecial: true);
        if (written < 0)
        {
            Console.Error.WriteLine($"[jllama] tokenize: ikinci çağrı da yetersiz (ihtiyaç={-written}, ayrılan={n})");
            return Array.Empty<int>();
        }

        if (written != n)
        {
            Array.Resize(ref tokens, written);
        }

        return tokens;
    }

    /// <summary>
    /// Decodes a token into its UTF-8 piece. The model emits a leading-space
    /// prefix per token; the caller trims the assembled string at the end.
    /// </summary>
    private string TokenToPiece(int token)
    {
        var n = LlamaTokenToPiece(_vocab, token, null, 0, 0, special: false);
        if (n == 0)
        {
            return string.Empty;
        }

        if (n < 0)
        {
            // b10682 token_to_piece: yetersiz tampon → -(gerekli bayt) döndürür.
            n = -n;
        }

        var buf = new byte[n];
        var written = LlamaTokenToPiece(_vocab, token, buf, buf.Length, 0, special: false);
        if (written <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(buf, 0, written);
    }

    private int DecodeTokens(int[] tokens)
    {
        if (tokens.Length == 0)
        {
            return 0;
        }

        int n = tokens.Length;
        var tokenBuf = Marshal.AllocHGlobal(n * sizeof(int));
        var logitsBuf = Marshal.AllocHGlobal(n);
        try
        {
            for (int i = 0; i < n; i++)
            {
                Marshal.WriteInt32(tokenBuf, i * sizeof(int), tokens[i]);
                Marshal.WriteByte(logitsBuf, i, 0);
            }

            // b10682 llama_batch_get_one, logits'i NULL döndürür → llama_decode hiç
            // çıktı üretmez ve llama_sampler_sample GGML_ASSERT ile çöker. Kendi
            // batch'imizi kurup son tokenı çıktıya işaretliyoruz.
            Marshal.WriteByte(logitsBuf, n - 1, 1);

            var batch = new LlamaBatch
            {
                NTokens = n,
                Token = tokenBuf,
                Embd = IntPtr.Zero,
                Pos = IntPtr.Zero,
                NSeqId = IntPtr.Zero,
                SeqId = IntPtr.Zero,
                Logits = logitsBuf,
            };

            int ret = LlamaDecode(_ctx, batch);
            var last = LlamaGetLogitsIth(_ctx, -1);
            Console.Error.WriteLine($"[jllama] decode: n={n}, ret={ret}, lastLogits={(last == IntPtr.Zero ? "NULL" : last.ToString("x"))}");
            return ret;
        }
        finally
        {
            Marshal.FreeHGlobal(tokenBuf);
            Marshal.FreeHGlobal(logitsBuf);
        }
    }

    private static void AssertModelParams(LlamaModelParams p)
    {
        // Actual defaults returned by llama_model_default_params() at b10682
        // (src/llama-model.cpp): n_gpu_layers=-1 (offload all layers), split
        // LAYER(1), load AUTO(-1), lazy AUTO(1), main_gpu=0, vocab_only=false.
        // Any other combination strongly suggests an ABI mismatch.
        bool ok = p.NGpuLayers == -1
                  && p.SplitMode == LLAMA_SPLIT_MODE_LAYER
                  && p.MainGpu == 0
                  && !p.VocabOnly;
        if (!ok)
        {
            throw new InvalidOperationException(
                $"llama_model_default_params anormal döndü (n_gpu_layers={p.NGpuLayers}, split_mode={p.SplitMode}, main_gpu={p.MainGpu}, vocab_only={p.VocabOnly}) — lib ABI'si uyumsuz olabilir.");
        }
    }

    private const int LLAMA_SPLIT_MODE_LAYER = 1;

    private static void TrimToolWrapper(ref string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..];
        }
        else if (text.StartsWith("```"))
        {
            text = text[3..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        text = text.Trim();
    }

    private static class ChatTemplate
    {
        public static string Apply(string modelId, IReadOnlyList<ChatMessage> messages)
        {
            if (IsQwen(modelId))
            {
                var sb = new StringBuilder();
                foreach (var m in messages)
                {
                    var tag = m.Role switch
                    {
                        ChatRole.System => "<|im_start|>system",
                        ChatRole.User => "<|im_start|>user",
                        ChatRole.Tool => "<|im_start|>tool",
                        _ => "<|im_start|>assistant",
                    };
                    sb.Append(tag).Append('\n').Append(m.Content).Append("<|im_end|>\n");
                }

                sb.Append("<|im_start|>assistant\n");
                return sb.ToString();
            }

            var sb3 = new StringBuilder("<|begin_of_text|>");
            foreach (var m in messages)
            {
                var tag = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    ChatRole.Tool => "ipython",
                    _ => "assistant",
                };
                sb3.Append("<|start_header_id|>").Append(tag).Append("<|end_header_id|>\n\n")
                   .Append(m.Content).Append("<|eot_id|>");
            }

            sb3.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
            return sb3.ToString();
        }

        private static bool IsQwen(string modelId) =>
            modelId.Contains("qwen", StringComparison.OrdinalIgnoreCase);
    }

    // ---- llama.h (b10682) P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct LlamaModelParams
    {
        public IntPtr Devices;
        public IntPtr TensorBuftOverrides;
        public int NGpuLayers;
        public int SplitMode;
        public int LoadMode;
        public int LazyMode;
        public int MainGpu;
        public IntPtr TensorSplit;
        public IntPtr ProgressCallback;
        public IntPtr ProgressCallbackUserData;
        public IntPtr KVOverrides;
        [MarshalAs(UnmanagedType.I1)] public bool VocabOnly;
        [MarshalAs(UnmanagedType.I1)] public bool CheckTensors;
        [MarshalAs(UnmanagedType.I1)] public bool UseExtraBufts;
        [MarshalAs(UnmanagedType.I1)] public bool NoHost;
        [MarshalAs(UnmanagedType.I1)] public bool NoAlloc;
        [MarshalAs(UnmanagedType.I1)] public bool LoadMtp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LlamaContextParams
    {
        public uint NCtx;
        public uint NBatch;
        public uint NUbatch;
        public uint NSeqMax;
        public uint NRsSeq;
        public uint NOutputsMax;
        public uint NOutputsMaxPerSeq;
        public int NThreads;
        public int NThreadsBatch;
        public int CtxType;
        public int RopeScalingType;
        public int PoolingType;
        public int AttentionType;
        public int FlashAttnType;
        public float RopeFreqBase;
        public float RopeFreqScale;
        public float YarnExtFactor;
        public float YarnAttnFactor;
        public float YarnBetaFast;
        public float YarnBetaSlow;
        public uint YarnOrigCtx;
        public float DefragThold;
        public IntPtr CbEval;
        public IntPtr CbEvalUserData;
        public int TypeK;
        public int TypeV;
        public IntPtr AbortCallback;
        public IntPtr AbortCallbackData;
        [MarshalAs(UnmanagedType.I1)] public bool Embeddings;
        [MarshalAs(UnmanagedType.I1)] public bool OffloadKqv;
        [MarshalAs(UnmanagedType.I1)] public bool NoPerf;
        [MarshalAs(UnmanagedType.I1)] public bool OpOffload;
        [MarshalAs(UnmanagedType.I1)] public bool SwaFull;
        [MarshalAs(UnmanagedType.I1)] public bool KvUnified;
        public IntPtr Samplers;
        public nint NSamplers;
        public IntPtr CtxOther;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LlamaBatch
    {
        public int NTokens;
        public IntPtr Token;
        public IntPtr Embd;
        public IntPtr Pos;
        public IntPtr NSeqId;
        public IntPtr SeqId;
        public IntPtr Logits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LlamaSamplerChainParams
    {
        [MarshalAs(UnmanagedType.I1)] public bool NoPerf;
    }

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_backend_init")]
    private static extern void LlamaBackendInit();

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_default_params")]
    private static extern LlamaModelParams LlamaModelDefaultParams();

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_load_from_file")]
    private static extern IntPtr LlamaModelLoadFromFile([In] byte[] path, LlamaModelParams parameters);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_get_vocab")]
    private static extern IntPtr LlamaModelGetVocab(IntPtr model);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_n_tokens")]
    private static extern int LlamaVocabNTokens(IntPtr vocab);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_vocab_eos")]
    private static extern int LlamaVocabEos(IntPtr vocab);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_context_default_params")]
    private static extern LlamaContextParams LlamaContextDefaultParams();

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_new_context_with_model")]
    private static extern IntPtr LlamaNewContextWithModel(IntPtr model, LlamaContextParams parameters);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_n_ctx")]
    private static extern uint LlamaNCtx(IntPtr ctx);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_decode")]
    private static extern int LlamaDecode(IntPtr ctx, LlamaBatch batch);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_default_params")]
    private static extern LlamaSamplerChainParams LlamaSamplerChainDefaultParams();

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_init")]
    private static extern IntPtr LlamaSamplerChainInit(LlamaSamplerChainParams parameters);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_dist")]
    private static extern IntPtr LlamaSamplerInitDist(uint seed);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_chain_add")]
    private static extern void LlamaSamplerChainAdd(IntPtr chain, IntPtr sampler);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_temp")]
    private static extern IntPtr LlamaSamplerInitTemp(float temperature);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_k")]
    private static extern IntPtr LlamaSamplerInitTopK(int k);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_top_p")]
    private static extern IntPtr LlamaSamplerInitTopP(float p, nint minKeep);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_init_min_p")]
    private static extern IntPtr LlamaSamplerInitMinP(float p, nint minKeep);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_sample")]
    private static extern int LlamaSamplerSample(IntPtr sampler, IntPtr ctx, int idx);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_get_logits_ith")]
    private static extern IntPtr LlamaGetLogitsIth(IntPtr ctx, int idx);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_sampler_free")]
    private static extern void LlamaSamplerFree(IntPtr sampler);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_tokenize")]
    private static extern int LlamaTokenize(
        IntPtr vocab,
        [In] byte[] text,
        int textLen,
        [In, Out] int[]? tokens,
        int nTokensMax,
        [MarshalAs(UnmanagedType.I1)] bool addSpecial,
        [MarshalAs(UnmanagedType.I1)] bool parseSpecial);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_token_to_piece")]
    private static extern int LlamaTokenToPiece(
        IntPtr vocab,
        int token,
        [Out] byte[]? buf,
        int length,
        int lstrip,
        [MarshalAs(UnmanagedType.I1)] bool special);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_free")]
    private static extern void LlamaFree(IntPtr ctx);

    [DllImport("jllama", CallingConvention = CallingConvention.Cdecl, EntryPoint = "llama_model_free")]
    private static extern void LlamaModelFree(IntPtr model);
}