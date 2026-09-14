using System.Globalization;
using System.Threading;
using Android.App;
using Android.Speech.Tts;
using Ataman.Core.Settings;
using Ataman.Core.Voice;
using Java.Util;

namespace AtamanAssistant.Android.Voice;

/// <summary>
/// TTS through the Android built-in engine (Google TTS). The engine renders
/// audio itself, so <see cref="AudioFrameProduced"/> is never raised on this
/// platform. Voice selection maps the shared <see cref="VoiceCatalog"/> entries
/// to Android locales: 'tr' → tr-TR, 'en' → en-US.
/// </summary>
public sealed class AndroidTextToSpeech : ITextToSpeech
{
    private readonly TaskCompletionSource<bool> _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _speakGate = new();
    private readonly TextToSpeech _tts;

    private TtsVoice _currentVoice = VoiceCatalog.TurkishDfki;
    private TaskCompletionSource<bool>? _pendingUtterance;
    private int _utteranceCounter;
    private bool _disposed;

    public event EventHandler<AudioFrameEventArgs>? AudioFrameProduced;
    public event EventHandler? SpeechStarted;
    public event EventHandler? SpeechCompleted;

    public IReadOnlyList<TtsVoice> AvailableVoices => VoiceCatalog.All;
    public TtsVoice? CurrentVoice => _currentVoice;
    public bool IsSpeaking => _pendingUtterance is not null;

    public AndroidTextToSpeech(global::Android.Content.Context context)
    {
        _tts = new TextToSpeech(context, new InitListener(status =>
        {
            if (status == OperationResult.Success)
            {
                _tts.SetOnUtteranceProgressListener(new Listener(this));
                _initTcs.TrySetResult(true);
            }
            else
            {
                _initTcs.TrySetException(new InvalidOperationException("The TTS engine could not be started."));
            }
        }));
    }

    public async Task SetVoiceAsync(TtsVoice voice, CancellationToken ct = default)
    {
        await _initTcs.Task.WaitAsync(ct).ConfigureAwait(false);

        var locale = CreateLocale(voice.Language);
        var result = _tts.SetLanguage(locale);
        if (result is not LanguageAvailableResult.Available
            && result is not LanguageAvailableResult.CountryAvailable
            && result is not LanguageAvailableResult.CountryVarAvailable)
        {
            // Fall back to the device default voice; speech must not break.
            _tts.SetLanguage(Locale.Default);
        }

        _currentVoice = voice;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AndroidTextToSpeech));
        }

        await _initTcs.Task.WaitAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var id = "_ataman_" + Interlocked.Increment(ref _utteranceCounter);

        TaskCompletionSource<bool> tcs;
        lock (_speakGate)
        {
            _pendingUtterance?.TrySetResult(true);
            tcs = _pendingUtterance = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        SpeechStarted?.Invoke(this, EventArgs.Empty);
        var queued = _tts.Speak(text, QueueMode.Flush, null, id);
        if (queued != OperationResult.Success)
        {
            lock (_speakGate)
            {
                if (_pendingUtterance == tcs)
                {
                    _pendingUtterance = null;
                }
            }

            throw new InvalidOperationException("The TTS utterance could not be queued.");
        }

        try
        {
            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _tts.Stop();
            throw;
        }
        finally
        {
            lock (_speakGate)
            {
                if (_pendingUtterance == tcs)
                {
                    _pendingUtterance = null;
                }
            }

            SpeechCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task StopSpeakingAsync()
    {
        lock (_speakGate)
        {
            _pendingUtterance?.TrySetResult(true);
            _pendingUtterance = null;
        }

        _tts.Stop();
        return Task.CompletedTask;
    }

    internal void NotifyUtteranceDone(bool failed)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_speakGate)
        {
            tcs = _pendingUtterance;
            _pendingUtterance = null;
        }

        if (tcs is not null)
        {
            tcs.TrySetResult(!failed);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopSpeakingAsync();
        _tts.Shutdown();
        _initTcs.TrySetCanceled();
    }

    private static Locale CreateLocale(string language) => language.Equals("en", StringComparison.OrdinalIgnoreCase)
        ? new Locale("en", "US")
        : new Locale("tr", "TR");

    private sealed class InitListener : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        private readonly Action<OperationResult> _onInit;

        public InitListener(Action<OperationResult> onInit)
        {
            _onInit = onInit;
        }

        public void OnInit(OperationResult status) => _onInit(status);
    }

    private sealed class Listener : UtteranceProgressListener
    {
        private readonly AndroidTextToSpeech _owner;

        public Listener(AndroidTextToSpeech owner)
        {
            _owner = owner;
        }

        public override void OnStart(string? utteranceId)
        {
        }

        public override void OnDone(string? utteranceId) => _owner.NotifyUtteranceDone(failed: false);

        [System.Obsolete]
        public override void OnError(string? utteranceId) => _owner.NotifyUtteranceDone(failed: true);
    }
}