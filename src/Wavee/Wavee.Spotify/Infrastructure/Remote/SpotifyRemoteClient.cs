﻿using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Eum.Spotify.connectstate;
using Eum.Spotify.playlist4;
using Google.Protobuf;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Wavee.Core.Ids;
using Wavee.Infrastructure.IO;
using Wavee.Player;
using Wavee.Spotify.Infrastructure.ApResolve;
using Wavee.Spotify.Infrastructure.Playback.Contracts;
using Wavee.Spotify.Infrastructure.Remote.Contracts;
using static LanguageExt.Prelude;

namespace Wavee.Spotify.Infrastructure.Remote;

internal sealed class SpotifyRemoteClient : ISpotifyRemoteClient, IDisposable
{

    private readonly Subject<SpotifyLibraryUpdateNotification> _libraryNotifSubj;
    private readonly Subject<SpotifyRootlistUpdateNotification> _rootlistNotifSubj;


    private Func<CancellationToken, Task<string>> _tokenFactory;
    private Func<RemoteSpotifyPlaybackEvent, Task> _playbackEvent;
    private readonly SpotifyRemoteConfig _config;
    private readonly string _deviceId;
    private readonly string _userId;

    private readonly Channel<SpotifyWebsocketMessage> _messageChannel =
        Channel.CreateUnbounded<SpotifyWebsocketMessage>();

    public TaskCompletionSource<Unit> Ready { get; } = new TaskCompletionSource<Unit>();

    public SpotifyRemoteClient(Func<CancellationToken, Task<string>> tokenFactory,
        Func<RemoteSpotifyPlaybackEvent, Task> playbackEvent, SpotifyRemoteConfig config, string deviceId,
        string userId)
    {
        _libraryNotifSubj = new Subject<SpotifyLibraryUpdateNotification>();
        _rootlistNotifSubj = new Subject<SpotifyRootlistUpdateNotification>();
        _tokenFactory = tokenFactory;
        _playbackEvent = playbackEvent;
        _config = config;
        _deviceId = deviceId;
        _userId = userId;
        ConnectionId = Ref(Option<string>.None);
        Task.Run(async () => { await Connect(_deviceId); });
    }

    public Ref<Option<string>> ConnectionId { get; }
    public Ref<Option<SpotifyRemoteState>> State { get; } = Ref(Option<SpotifyRemoteState>.None);

    public IObservable<Option<SpotifyRemoteState>> StateUpdates =>
        State.OnChange().StartWith(State.Value);

    public IObservable<SpotifyRootlistUpdateNotification> RootlistChanged =>
        _rootlistNotifSubj.StartWith(
            new SpotifyRootlistUpdateNotification(_userId));

    public IObservable<SpotifyLibraryUpdateNotification> LibraryChanged => _libraryNotifSubj;
    public async Task<Option<Unit>> Takeover(CancellationToken ct = default)
    {
        await Ready.Task;
        if (State.Value.IsNone)
            return Option<Unit>.None;
        if (State.Value.ValueUnsafe().TrackUri.IsNone)
            return Option<Unit>.None;

        var currentState = State.Value.ValueUnsafe();
        await _playbackEvent(new RemoteSpotifyPlaybackEvent
        {
            TrackId = currentState.TrackUri.ValueUnsafe(),
            TrackUid = currentState.TrackUid,
            PlayingFromQueue = currentState.PlayingFromQueue,
            TrackForQueueHint = currentState.TrackForQueueHint,
            PlaybackPosition = currentState.Position,
            ContextUri = currentState.ContextUri,
            IsPaused = currentState.IsPaused,
            IsShuffling = currentState.IsShuffling,
            RepeatState = currentState.RepeatState,
            EventType = RemoteSpotifyPlaybackEventType.Play,
            TrackIndex = currentState.TrackIndex,
            Queue = Some(currentState.NextTracks.Filter(f => f.Provider is "queue"))
        });

        return Some(default(Unit));
    }

    public Task<Unit> SetShuffle(bool isShuffling, CancellationToken ct = default)
    {
        const string commandName = "set_shuffling_context";
        return InvokeCommand(commandName, new HashMap<string, object>().Add("value", isShuffling), ct);
    }

    public Task<Unit> Resume(CancellationToken ct = default)
    {
        const string commandName = "resume";
        return InvokeCommand(commandName, LanguageExt.HashMap<string, object>.Empty, ct);
    }

    public Task<Unit> Pause(CancellationToken ct = default)
    {
        const string commandName = "pause";
        return InvokeCommand(commandName, LanguageExt.HashMap<string, object>.Empty, ct);
    }

    public Task<Unit> SkipNext(CancellationToken ct = default)
    {
        const string commandName = "skip_next";

        return InvokeCommand(commandName, LanguageExt.HashMap<string, object>.Empty, ct);
    }

    public Task<Unit> SkipPrevious(CancellationToken ct = default)
    {
        const string commandName = "skip_prev";

        return InvokeCommand(commandName, LanguageExt.HashMap<string, object>.Empty, ct);
    }

    public Task<Unit> SetRepeat(RepeatState next, CancellationToken ct = default)
    {
        var options = new HashMap<string, object>()
            .Add("repeating_context", next >= RepeatState.Context)
            .Add("repeating_track", next is RepeatState.Track);
        const string commandName = "set_options";

        return InvokeCommand(commandName, options, ct);
    }

    public Task<Unit> SeekTo(TimeSpan to, CancellationToken ct = default)
    {
        const string commandName = "seek_to";
        return InvokeCommand(commandName, new HashMap<string, object>().Add("value", to.TotalMilliseconds), ct);
    }

    public Task<Unit> RefreshState()
    {
        //TODO:
        return Task.FromResult(default(Unit));
        //     return Put(_, PutStateReason.PlayerStateChanged);
    }

    public IObservable<Diff> ObservePlaylist(AudioId id)
    {
        //TODO
        return Observable.Never<Diff>();
    }

    public async Task OnPlaybackUpdate(SpotifyLocalPlaybackState state)
    {
        if (ConnectionId.Value.IsNone)
            return;

        await Put(state, PutStateReason.PlayerStateChanged);
    }

    /// <summary>
    /// Invokes a command on the remote device
    /// </summary>
    /// <param name="commandName"></param>
    /// <param name="value"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="NoActiveDeviceException"></exception>
    private async Task<Unit> InvokeCommand(
        string commandName,
        HashMap<string, object> value, CancellationToken ct)
    {
        //POSThttps://gae2-spclient.spotify.com/connect-state/v1/player/command/from/35ee833111913f567c4dc6ba7537cb6d089b130f/to/35ee833111913f567c4dc6ba7537cb6d089b130f
        var spClient = ApResolver.SpClient.ValueUnsafe();
        var activeDevice = State.Value.IfNone(() => throw new NoActiveDeviceException()).ActiveDeviceId.IfNone(() => throw new NoActiveDeviceException());
        var url = $"https://{spClient}/connect-state/v1/player/command/from/{_deviceId}/to/{activeDevice}";

        //{"command":{"repeating_context":true,"repeating_track":false,"endpoint":"set_options"}}
        //we need to concat the command name with the parameter (which is optional, and is just a dictionary 
        //of key value pairs)

        var command = new Dictionary<string, object>
        {
            {"endpoint", commandName}
        };
        foreach (var (key, val) in value)
            command.Add(key, val);

        var serialized = JsonSerializer.Serialize(new
        {
            command = command
        });
        using var content = new StringContent(serialized, Encoding.UTF8, "application/json");
        var jwt = await _tokenFactory(ct);
        using var response = await HttpIO.Post(url, new AuthenticationHeaderValue("Bearer", jwt), LanguageExt.HashMap<string, string>.Empty,
            content, ct);
        return default;
    }

    private async Task Connect(string deviceId)
    {
        ClientWebSocket ws = default;
        try
        {
            var dealerUrl = ApResolve.ApResolver.Dealer.ValueUnsafe();
            var accessToken = await _tokenFactory(CancellationToken.None);
            var websocketUrl = $"wss://{dealerUrl}?access_token={accessToken}";
            ws = await WebsocketIO.Connect(websocketUrl);

            //Read the first message, and then setup the loop
            var message = await ReadNextMessage(ws, CancellationToken.None);
            if (message.Type is not SpotifyWebsocketMessageType.ConnectionId)
            {
                await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Expected ConnectionId",
                    CancellationToken.None);
                throw new Exception("Expected ConnectionId");
            }

            var connectionId = message.Headers
                .Find("Spotify-Connection-Id")
                .ValueUnsafe();
            if (string.IsNullOrEmpty(connectionId))
            {
                await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Expected ConnectionId",
                    CancellationToken.None);
                throw new Exception("Expected ConnectionId");
            }

            atomic(() => ConnectionId.Swap(_ => connectionId));
            //Send the handshake 
            var newState = SpotifyLocalPlaybackState.Empty(_config, deviceId);
            var cluster = await Put(newState, PutStateReason.NewDevice, CancellationToken.None);

            atomic(() => State.Swap(_ => SpotifyRemoteState.From(cluster, _deviceId)));

            Ready.TrySetResult(Unit.Default);
            //Start the loop
            while (true)
            {
                var nextMessage = await ReadNextMessage(ws, CancellationToken.None);
                switch (nextMessage.Type)
                {
                    case SpotifyWebsocketMessageType.ConnectionId:
                        await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Unexpected ConnectionId",
                            CancellationToken.None);
                        throw new Exception("Unexpected ConnectionId");
                    case SpotifyWebsocketMessageType.Message:
                        HandleMessage(nextMessage);
                        break;
                    case SpotifyWebsocketMessageType.Request:
                        {
                            var jsonData = Encoding.UTF8.GetString(nextMessage.Payload.ValueUnsafe().Span);
                            using var jsonDoc = JsonDocument.Parse(jsonData);
                            var request = jsonDoc.RootElement;
                            var messageId = request.GetProperty("message_id").GetUInt32();
                            var sentBy = request.GetProperty("sent_by_device_id").GetString();
                            var command = request.GetProperty("command");
                            var endpoint = command.GetProperty("endpoint").GetString();

                            switch (endpoint)
                            {
                                case "add_to_queue":
                                    {
                                        var track = command.GetProperty("track");
                                        var uri = track.GetProperty("uri").GetString();
                                        string uid = string.Empty;
                                        if (track.TryGetProperty("uid", out var uidd))
                                        {
                                            uid = uidd.GetString();
                                        }

                                        var pv = new ProvidedTrack
                                        {
                                            Uri = uri,
                                            Uid = uid
                                        };
                                        var metadata = track.GetProperty("metadata");
                                        foreach (var key in metadata.EnumerateObject())
                                        {
                                            pv.Metadata[key.Name] = key.Value.GetString();
                                        }

                                        pv.Provider = track.GetProperty("provider").GetString();
                                        await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                        {
                                            EventType = RemoteSpotifyPlaybackEventType.AddToQueue,
                                            SentBy = sentBy,
                                            Queue = Some<IEnumerable<ProvidedTrack>>(new ProvidedTrack[]
                                            {
                                        pv
                                            }),
                                            TrackUid = default,
                                            TrackIndex = default
                                        });
                                        break;
                                    }
                                case "set_queue":
                                    var nextTracks = command.GetProperty("next_tracks")
                                        .EnumerateArray()
                                        .Select(track =>
                                        {
                                            var uri = track.GetProperty("uri").GetString();
                                            string? uid = string.Empty;
                                            if (track.TryGetProperty("uid", out var uidd))
                                            {
                                                uid = uidd.GetString();
                                            }

                                            var pv = new ProvidedTrack
                                            {
                                                Uri = uri,
                                                Uid = uid
                                            };
                                            var metadata = track.GetProperty("metadata");
                                            foreach (var key in metadata.EnumerateObject())
                                            {
                                                pv.Metadata[key.Name] = key.Value.GetString();
                                            }

                                            pv.Provider = track.GetProperty("provider").GetString();
                                            return pv;
                                        });

                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.SetQueue,
                                        SentBy = sentBy,
                                        CommandId = messageId,
                                        TrackUid = default,
                                        TrackIndex = default,
                                        Queue = Some(nextTracks)
                                    });
                                    break;
                                case "set_repeating_track":
                                    {
                                        var value = command.GetProperty("value").GetBoolean();
                                        if (value)
                                        {
                                            await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                            {
                                                EventType = RemoteSpotifyPlaybackEventType.Repeat,
                                                SentBy = sentBy,
                                                CommandId = messageId,
                                                RepeatState = value ? RepeatState.Track : Option<RepeatState>.None,
                                                TrackUid = default,
                                                TrackIndex = default,
                                            });
                                        }

                                        break;
                                    }
                                case "set_repeating_context":
                                    {
                                        var value = command.GetProperty("value").GetBoolean();
                                        await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                        {
                                            EventType = RemoteSpotifyPlaybackEventType.Repeat,
                                            SentBy = sentBy,
                                            CommandId = messageId,
                                            RepeatState = value ? RepeatState.Context : RepeatState.None,
                                            TrackUid = default,
                                            TrackIndex = default
                                        });
                                        break;
                                    }
                                case "set_shuffling_context":
                                    {
                                        var value = command.GetProperty("value").GetBoolean();
                                        await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                        {
                                            EventType = RemoteSpotifyPlaybackEventType.Shuffle,
                                            SentBy = sentBy,
                                            CommandId = messageId,
                                            IsShuffling = value,
                                            TrackUid = default,
                                            TrackIndex = default
                                        });
                                        break;
                                    }
                                case "play":
                                    var skipTo = command.GetProperty("options").GetProperty("skip_to");
                                    var ctx = command.GetProperty("context");
                                    Option<string> trackUid = None;
                                    if (skipTo.TryGetProperty("track_uid", out var trackUidProp))
                                        trackUid = trackUidProp.GetString();
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.Play,
                                        SentBy = sentBy,
                                        CommandId = messageId,
                                        TrackUid = trackUid,
                                        TrackId = skipTo.TryGetProperty("track_uri", out var ur)
                                            ? AudioId.FromUri(ur.GetString())
                                            : None,
                                        TrackIndex = skipTo.TryGetProperty("track_index", out var idx) ? idx.GetInt32() : Option<int>.None,
                                        ContextUri = ctx.GetProperty("uri").GetString(),
                                        IsPaused = false,
                                        IsShuffling = None,
                                        RepeatState = None,
                                        Queue = None,
                                        PlaybackPosition = TimeSpan.Zero,
                                        SeekTo = None
                                    });
                                    break;
                                case "transfer":
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.UpdateDevice,
                                        SentBy = sentBy,
                                        CommandId = messageId,
                                        TrackUid = default,
                                        TrackIndex = default,
                                    });
                                    await Takeover();
                                    break;
                                case "skip_next":
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.SkipNext,
                                        SentBy = sentBy,
                                        CommandId = messageId,

                                        TrackUid = default,
                                        TrackIndex = default
                                    });
                                    break;
                                case "seek_to":
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.SeekTo,
                                        SeekTo = TimeSpan.FromMilliseconds(command.GetProperty("value")
                                            .GetDouble()),
                                        SentBy = sentBy,
                                        CommandId = messageId,

                                        TrackUid = default,
                                        TrackIndex = default
                                    });
                                    break;
                                case "pause":
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.Pause,
                                        SentBy = sentBy,
                                        CommandId = messageId,

                                        TrackUid = default,
                                        TrackIndex = default
                                    });
                                    break;
                                case "resume":
                                    await _playbackEvent(new RemoteSpotifyPlaybackEvent
                                    {
                                        EventType = RemoteSpotifyPlaybackEventType.Resume,
                                        SentBy = sentBy,
                                        CommandId = messageId,

                                        TrackUid = default,
                                        TrackIndex = default
                                    });
                                    break;
                            }

                            //respond
                            var datareply = new
                            {
                                type = "reply",
                                key = nextMessage.Uri,
                                payload = new
                                {
                                    success = true.ToString().ToLower()
                                }
                            };
                            ReadOnlyMemory<byte> payload = JsonSerializer.SerializeToUtf8Bytes(datareply);
                            await ws.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        catch (Exception e)
        {
            ws?.Dispose();
            ws = null;
            Console.WriteLine(e);
            Debug.WriteLine(e);
            await Task.Delay(4000);
            await Connect(deviceId);
            return;
        }
    }

    private Unit HandleMessage(SpotifyWebsocketMessage message)
    {
        if (message.Uri.StartsWith("hm://connect-state/v1/cluster"))
        {
            var clusterUpdate = ClusterUpdate.Parser.ParseFrom(message.Payload.ValueUnsafe().Span);
            atomic(() => State.Swap(_ => SpotifyRemoteState.From(clusterUpdate.Cluster, _deviceId)));
            GC.Collect();
        }
        else if (message.Uri.Equals($"hm://playlist/v2/user/{_userId}/rootlist"))
        {
            //   atomic(() => _rootlistNotifSubj.OnNext(new SpotifyRootlistUpdateNotification(_userId)));
        }
        else if (message.Uri.StartsWith("hm://collection/") && message.Uri.EndsWith("/json"))
        {
            var payload = message.Payload.ValueUnsafe();
            using var jsonDoc = JsonDocument.Parse(payload);
            using var rootArr = jsonDoc.RootElement.EnumerateArray();
            foreach (var rootItemStr in rootArr.Select(c => c.ToString()))
            {
                using var rootItem = JsonDocument.Parse(rootItemStr);
                using var items = rootItem.RootElement.GetProperty("items").EnumerateArray();
                foreach (var item in items)
                {
                    var type = item.GetProperty("type").GetString();
                    var removed = item.GetProperty("removed").GetBoolean();
                    var addedAt = item.GetProperty("addedAt").GetUInt64();
                    var result = new SpotifyLibraryUpdateNotification(
                        Item: AudioId.FromBase62(
                            base62: item.GetProperty("identifier").GetString(),
                            itemType: type switch
                            {
                                "track" => AudioItemType.Track,
                                "artist" => AudioItemType.Artist,
                                "album" => AudioItemType.Album
                            }, ServiceType.Spotify),
                        Removed: removed,
                        AddedAt: removed ? Option<DateTimeOffset>.None : DateTimeOffset.FromUnixTimeSeconds((long)addedAt)
                    );
                    atomic(() => _libraryNotifSubj.OnNext(result));
                }
            }
        }

        return default;
    }

    private async Task<Cluster> Put(SpotifyLocalPlaybackState state,
        PutStateReason reason,
        CancellationToken ct = default)
    {
        var connectionId = ConnectionId.Value.ValueUnsafe();
        var jwt = await _tokenFactory(ct);
        return await Put(state.BuildPutStateRequest(reason, WaveePlayer.Instance.Position),
            connectionId, jwt,
            _deviceId, ct);
    }

    private static async Task<Cluster> Put(PutStateRequest request,
        string connectionId,
        string jwt,
        string deviceId,
        CancellationToken ct = default)
    {
        var spClient = ApResolve.ApResolver.SpClient.ValueUnsafe();
        var url = $"https://{spClient}/connect-state/v1/devices/{deviceId}";
        var bearerHeader = new AuthenticationHeaderValue("Bearer", jwt);

        var headers = new HashMap<string, string>()
            .Add("X-Spotify-Connection-Id", connectionId)
            .Add("accept", "gzip");

        using var body = GzipHelpers.GzipCompress(request.ToByteArray().AsMemory());
        using var response = await HttpIO.Put(url, bearerHeader, headers, body, ct);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var gzip = GzipHelpers.GzipDecompress(responseStream);
        gzip.Position = 0;
        var cluster = Cluster.Parser.ParseFrom(gzip);
        return cluster;
    }


    private static async Task<SpotifyWebsocketMessage> ReadNextMessage(ClientWebSocket ws, CancellationToken ct)
    {
        var message = await WebsocketIO.Receive(ws, ct);
        return SpotifyWebsocketMessage.ParseFrom(message);
    }

    public void Dispose()
    {
#pragma warning disable CS8625
        _tokenFactory = null;
        _playbackEvent = null;
#pragma warning restore CS8625
    }
}

public sealed class NoActiveDeviceException : Exception
{
    const string message = "No active device found.";

    public NoActiveDeviceException() : base(message)
    {
    }
}