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
/// </remarks>
public sealed class SmtcMediaSource : IMediaSource
{
    /// <summary>Taille maximale d'une pochette lue ; au-delà, pas de pochette.</summary>
    internal const int MaxArtworkBytes = 4 * 1024 * 1024;

    private readonly IModuleLog _log;
    private readonly TimeProvider _time;

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
    {
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public MediaSnapshot? Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct).ConfigureAwait(false);

        lock (_hookGate)
        {
            if (_disposed) return;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            HookSession(SafeGetCurrentSession(manager));
        }

        // Premier relevé : Current est à jour au retour (un échec de relevé est journalisé, pas propagé).
        await RefreshAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
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

    /// <summary>Relit la session courante et remplace <see cref="Current"/>. Ne lève jamais.</summary>
    private async Task RefreshAsync()
    {
        var ticket = Interlocked.Increment(ref _requested);
        try
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _covered) >= ticket) return;
                // Tout ce qui a été demandé jusqu'ici sera vu par ce relevé.
                var covers = Interlocked.Read(ref _requested);

                var snapshot = await ReadAsync().ConfigureAwait(false);
                lock (_hookGate)
                {
                    if (_disposed) return;
                    Volatile.Write(ref _current, snapshot);
                }
                Volatile.Write(ref _covered, covers);
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

    private async Task<MediaSnapshot?> ReadAsync()
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
        var properties = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var title = properties.Title ?? string.Empty;
        var artist = properties.Artist ?? string.Empty;

        var key = (title, artist, appId ?? string.Empty);
        var mediaChanged = Interlocked.Exchange(ref _mediaChanged, 0) == 1;
        if (_artworkFor != key || (_artwork is null && mediaChanged))
        {
            _artwork = await ReadArtworkAsync(properties).ConfigureAwait(false);
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

    private async Task<byte[]?> ReadArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        try
        {
            if (properties.Thumbnail is not { } thumbnail) return null;
            using var stream = await thumbnail.OpenReadAsync().AsTask().ConfigureAwait(false);
            if (stream.Size is 0 or > MaxArtworkBytes) return null;

            using var input = stream.AsStreamForRead();
            using var buffer = new MemoryStream((int)stream.Size);
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await input.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxArtworkBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.Length == 0 ? null : buffer.ToArray();
        }
        catch (Exception ex)
        {
            _log.Warning($"Pochette illisible : {ex.Message}");
            return null;
        }
    }
}
