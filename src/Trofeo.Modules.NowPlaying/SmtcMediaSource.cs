using Windows.Media.Control;
using Windows.Storage.Streams;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.NowPlaying;

/// <summary>
/// Source de lecture en cours d'après les contrôles média de Windows (SMTC) : la session « courante » du système,
/// c'est-à-dire l'application dont les touches multimédia sont actives.
/// </summary>
/// <remarks>
/// Les évènements SMTC arrivent sur des threads quelconques. Les relevés sont sérialisés (un seul à la fois, les
/// rafales étant regroupées) et toute exception d'un relevé est journalisée puis avalée : un lecteur capricieux ne doit
/// jamais faire tomber le service. <see cref="Current"/> est remplacé d'un bloc.
/// Aucun appel WinRT n'est attendu sans délai : un appel qui ne répond pas est abandonné, le verrou de relevé est rendu
/// et la demande suivante relit tout.
/// </remarks>
public sealed class SmtcMediaSource : IMediaSource
{
    /// <summary>Taille maximale d'une pochette lue ; au-delà, pas de pochette.</summary>
    internal const int MaxArtworkBytes = 4 * 1024 * 1024;

    /// <summary>Délai d'un appel WinRT (gestionnaire de sessions, propriétés du média).</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Délai de la lecture de la pochette, ouverture et lecture du flux comprises.</summary>
    internal static readonly TimeSpan ArtworkTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Délai d'un relevé entier : un relevé qui dépasse est abandonné, quoi qu'il attende.</summary>
    internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Attente maximale du verrou de relevé.</summary>
    internal static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Intervalle entre deux tentatives de connexion tant que les contrôles média ne répondent pas.</summary>
    internal static readonly TimeSpan RetryPeriod = TimeSpan.FromSeconds(30);

    private readonly IModuleLog _log;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task<GlobalSystemMediaTransportControlsSessionManager>> _requestManager;
    private readonly Func<CancellationToken, Task<MediaSnapshot?>> _read;
    private readonly CancellationTokenSource _lifetime = new();

    // Sérialisation des relevés : chaque demande prend un numéro ; un relevé qui démarre couvre toutes les demandes
    // numérotées avant lui, les demandes déjà couvertes en attente du verrou sont donc ignorées.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private long _requested;
    private long _covered;

    private readonly object _hookGate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    // Pochette : relue seulement quand le titre ou l'artiste change (ou, si elle manquait, à un changement de
    // propriétés : beaucoup de lecteurs la publient un instant après le titre). N'est touché que sous _refreshGate.
    private (string Title, string Artist, string AppId)? _artworkFor;
    private byte[]? _artwork;
    private int _mediaChanged;

    private MediaSnapshot? _current;

    public SmtcMediaSource(IModuleLog log, TimeProvider? time = null)
        : this(log, time, null, null)
    {
    }

    /// <summary>Constructeur de test : remplace l'ouverture du gestionnaire de sessions et/ou le relevé par des faux.</summary>
    internal SmtcMediaSource(
        IModuleLog log,
        TimeProvider? time,
        Func<CancellationToken, Task<GlobalSystemMediaTransportControlsSessionManager>>? requestManager,
        Func<CancellationToken, Task<MediaSnapshot?>>? read)
    {
        _log = log;
        _time = time ?? TimeProvider.System;
        _requestManager = requestManager ?? (ct => GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct));
        _read = read ?? ReadAsync;
    }

    public MediaSnapshot? Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken ct)
    {
        if (await TryConnectAsync(ct).ConfigureAwait(false)) return;

        // Contrôles média muets (au démarrage de la session Windows, par exemple) : on réessaie sans bloquer le module.
        _ = RetryConnectAsync(_lifetime.Token);
    }

    /// <summary>Ouvre le gestionnaire de sessions et fait le premier relevé. Faux (journalisé) si l'ouverture échoue ou dépasse son délai.</summary>
    private async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await WithTimeoutAsync(_requestManager, CallTimeout, "Contrôles média de Windows", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Contrôles média de Windows indisponibles, nouvel essai dans {RetryPeriod.TotalSeconds:0} s : {ex.Message}");
            return false;
        }

        lock (_hookGate)
        {
            if (_disposed) return true;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            HookSession(SafeGetCurrentSession(manager));
        }

        // Premier relevé : Current est à jour au retour (un échec de relevé est journalisé, pas propagé).
        await RefreshAsync().ConfigureAwait(false);
        return true;
    }

    private async Task RetryConnectAsync(CancellationToken ct)
    {
        try
        {
            do
            {
                await Task.Delay(RetryPeriod, _time, ct).ConfigureAwait(false);
            }
            while (!await TryConnectAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Source libérée.
        }
        catch (Exception ex)
        {
            _log.Error($"Reconnexion aux contrôles média : {ex.Message}");
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        lock (_hookGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
            HookSession(null);
        }

        Volatile.Write(ref _current, null);
    }

    // --- Abonnements ---

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        try
        {
            lock (_hookGate)
            {
                if (_disposed) return;
                HookSession(SafeGetCurrentSession(sender));
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Changement de session média : {ex.Message}");
        }

        _ = RefreshAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Interlocked.Exchange(ref _mediaChanged, 1);
        _ = RefreshAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ = RefreshAsync();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ = RefreshAsync();

    /// <summary>Quitte les évènements de l'ancienne session avant de s'abonner à la nouvelle. À appeler sous <see cref="_hookGate"/>.</summary>
    private void HookSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session)) return;

        if (_session is { } old)
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            old.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;
        if (session is null) return;
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    private GlobalSystemMediaTransportControlsSession? SafeGetCurrentSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            return manager.GetCurrentSession();
        }
        catch (Exception ex)
        {
            _log.Error($"Session média courante illisible : {ex.Message}");
            return null;
        }
    }

    // --- Relevé ---

    /// <summary>
    /// Relit la session courante et remplace <see cref="Current"/>. Ne lève jamais. Si le relevé échoue ou dépasse son
    /// délai, <see cref="Current"/> devient <c>null</c> (une valeur périmée ne doit pas passer pour fraîche) et la
    /// demande suivante relit tout, pochette comprise.
    /// </summary>
    internal async Task RefreshAsync()
    {
        var ticket = Interlocked.Increment(ref _requested);
        try
        {
            if (!await _refreshGate.WaitAsync(GateTimeout).ConfigureAwait(false))
            {
                _log.Error("Lecture des contrôles média : un relevé précédent ne se termine pas.");
                Interlocked.Exchange(ref _mediaChanged, 1);
                return;
            }

            try
            {
                if (Volatile.Read(ref _covered) >= ticket) return;
                // Tout ce qui a été demandé jusqu'ici sera vu par ce relevé.
                var covers = Interlocked.Read(ref _requested);

                MediaSnapshot? snapshot;
                var succeeded = true;
                try
                {
                    snapshot = await WithTimeoutAsync(_read, RefreshTimeout, "Relevé des contrôles média", _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Error($"Lecture des contrôles média : {ex.Message}");
                    snapshot = null;
                    succeeded = false;
                    _artworkFor = null;
                    _artwork = null;
                    Interlocked.Exchange(ref _mediaChanged, 1);
                }

                lock (_hookGate)
                {
                    if (_disposed) return;
                    Volatile.Write(ref _current, snapshot);
                }
                // Un relevé raté ne couvre rien : la demande suivante relira.
                if (succeeded) Volatile.Write(ref _covered, covers);
            }
            finally
            {
                _refreshGate.Release();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Error($"Lecture des contrôles média : {ex.Message}");
        }
    }

    /// <summary>
    /// Attend <paramref name="operation"/> au plus <paramref name="timeout"/>. Passé ce délai l'opération est annulée et
    /// abandonnée même si elle ignore l'annulation (un appel WinRT peut ne jamais rendre la main).
    /// </summary>
    private async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout, string what, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = operation(linked.Token);
        try
        {
            return await task.WaitAsync(timeout, _time, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            // L'opération abandonnée peut encore échouer plus tard : on observe son erreur pour qu'elle ne remonte pas ailleurs.
            _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw new TimeoutException($"{what} : pas de réponse en {timeout.TotalSeconds:0} s.");
        }
    }

    private async Task<MediaSnapshot?> ReadAsync(CancellationToken ct)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_hookGate)
        {
            session = _session;
        }

        if (session is null)
        {
            _artworkFor = null;
            _artwork = null;
            return null;
        }

        var appId = session.SourceAppUserModelId;
        var properties = await WithTimeoutAsync(
            token => session.TryGetMediaPropertiesAsync().AsTask(token), CallTimeout, "Propriétés du média", ct).ConfigureAwait(false);
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var title = properties.Title ?? string.Empty;
        var artist = properties.Artist ?? string.Empty;

        var key = (title, artist, appId ?? string.Empty);
        var mediaChanged = Interlocked.Exchange(ref _mediaChanged, 0) == 1;
        if (_artworkFor != key || (_artwork is null && mediaChanged))
        {
            var artwork = await ReadArtworkAsync(properties, ct).ConfigureAwait(false);
            // Un relevé abandonné qui finit tard ne doit pas écraser l'état du relevé suivant.
            ct.ThrowIfCancellationRequested();
            _artwork = artwork;
            _artworkFor = key;
        }

        // Sans date de relevé fiable, on extrapole depuis maintenant.
        var updated = timeline.LastUpdatedTime;
        var positionAt = updated.Year < 2000 ? _time.GetUtcNow() : updated;

        return new MediaSnapshot(
            title,
            artist,
            AppNames.Friendly(appId),
            timeline.Position,
            timeline.EndTime - timeline.StartTime,
            positionAt,
            playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            _artwork);
    }

    private async Task<byte[]?> ReadArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties, CancellationToken ct)
    {
        try
        {
            if (properties.Thumbnail is not { } thumbnail) return null;
            return await WithTimeoutAsync(token => ReadThumbnailAsync(thumbnail, token), ArtworkTimeout, "Pochette", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning($"Pochette illisible : {ex.Message}");
            return null;
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail, CancellationToken ct)
    {
        using var stream = await thumbnail.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
        if (stream.Size is 0 or > MaxArtworkBytes) return null;

        using var input = stream.AsStreamForRead();
        using var buffer = new MemoryStream((int)stream.Size);
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxArtworkBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.Length == 0 ? null : buffer.ToArray();
    }
}
