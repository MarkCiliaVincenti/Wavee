﻿using System.Collections.Immutable;
using System.Collections.ObjectModel;
using AngleSharp.Dom;
using CommunityToolkit.Mvvm.ComponentModel;
using Mediator;
using Nito.AsyncEx;
using Spotify.Metadata;
using Wavee.Spotify.Common;
using Wavee.UI.Domain.Playlist;
using Wavee.UI.Features.Playlists.Contracts;
using Wavee.UI.Features.Playlists.Queries;
using Wavee.UI.Features.Tracks;
using Wavee.UI.Test;

namespace Wavee.UI.Features.Playlists.ViewModel;

public sealed class PlaylistViewModel : ObservableObject, IPlaylistListener
{
    private string? _title;
    private string? _bigImage;
    private bool _tracksLoaded;
    private bool _dataLoaded;
    private string? _description;
    private ulong? _popCount;
    private TimeSpan? _totalDuration;
    private readonly IMediator _mediator;
    private readonly IUIDispatcher _uiDispatcher;
    private bool _hasImage;
    private bool _hidePopCount;
    private string _generalSearchTerm = string.Empty;
    private readonly AsyncLock _lock = new AsyncLock();

    public PlaylistViewModel(PlaylistSidebarItemViewModel sidebarItem, IMediator mediator, IUIDispatcher uiDispatcher)
    {
        _mediator = mediator;
        _uiDispatcher = uiDispatcher;
        Id = sidebarItem.Id;
        Title = sidebarItem.Name;
        BigImage = sidebarItem.BigImage;

        for (int i = 0; i < sidebarItem.Items; i++)
        {
            Tracks.Add(new LazyPlaylistTrackViewModel
            {
                HasValue = false,
                Track = null,
                Index = i
            });
        }
        Description = sidebarItem.Description;

        HasImage = sidebarItem.HasImage;
        TracksLoaded = false;
        DataLoaded = true;
    }

    public async void OnPlaylistChanged_Dumb()
    {
        await RefreshTracks();
    }

    public string Id { get; }
    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public string? BigImage
    {
        get => _bigImage;
        set => SetProperty(ref _bigImage, value);
    }

    public bool DataLoaded
    {
        get => _dataLoaded;
        set => SetProperty(ref _dataLoaded, value);
    }
    public bool TracksLoaded
    {
        get => _tracksLoaded;
        set => SetProperty(ref _tracksLoaded, value);
    }

    public void Initialize(CancellationToken cancellationToken)
    {
        // PopCount
        _ = Task.Run(async () => await _mediator.Send(new GetPlaylistSavedCountQuery
        {
            PlaylistId = Id
        }), cancellationToken).ContinueWith(x =>
        {
            _uiDispatcher.Invoke(() =>
            {
                if (x.Result is null)
                {
                    HidePopCount = true;
                }
                else
                {
                    HidePopCount = false;
                }
                PopCount = x.Result;
            });
        }, cancellationToken);

        // Tracks 
        _ = Task.Run(async () => await FetchAndSetTracks(), cancellationToken);
    }
    public async Task RefreshTracks()
    {
        await FetchAndSetTracks();
    }

    private async Task FetchAndSetTracks()
    {
        using (await _lock.LockAsync())
        {
            var trackIdsAndAttributes = await _mediator.Send(new GetPlaylistTracksIdsQuery
            {
                PlaylistId = Id
            });
            var tracks = trackIdsAndAttributes.ToDictionary(x => x.UniqueItemId ?? x.Id, x => x);

            var tracksMetadata = await _mediator.Send(new GetTracksMetadataRequest
            {
                Ids = tracks.Select(f => f.Value.Id).ToImmutableArray(),
                SearchTerms = SearchTerms.Concat(new[]
                {
                    GeneralSearchTerm
                }).ToImmutableArray()
            });

            _uiDispatcher.Invoke(() =>
            {
                Tracks.Clear();
                int index = 0;
                TracksLoaded = true;
                double totalSeconds = 0;
                foreach (var info in tracks.Values)
                {
                    if (tracksMetadata.TryGetValue(info.Id, out var track))
                    {
                        if (track.HasValue)
                        {
                            PlaylistTrackViewModel? trackasVm = null;
                            if (track.Value.Track is not null)
                            {
                                trackasVm = new PlaylistTrackViewModel(track.Value.Track, info);
                                totalSeconds += trackasVm.Duration.TotalSeconds;
                            }

                            Tracks.Add(new LazyPlaylistTrackViewModel
                            {
                                HasValue = true,
                                Track = trackasVm!,
                                Index = index++
                            });
                        }
                    }
                }

                TotalDuration = TimeSpan.FromSeconds(totalSeconds);
            });
        }
    }

    public ObservableCollection<LazyPlaylistTrackViewModel> Tracks { get; } = new();

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public ulong? PopCount
    {
        get => _popCount;
        set => SetProperty(ref _popCount, value);
    }

    public bool HidePopCount
    {
        get => _hidePopCount;
        set => SetProperty(ref _hidePopCount, value);
    }

    public TimeSpan? TotalDuration
    {
        get => _totalDuration;
        set => SetProperty(ref _totalDuration, value);
    }

    public bool HasImage
    {
        get => _hasImage;
        set => SetProperty(ref _hasImage, value);
    }

    public string GeneralSearchTerm
    {
        get => _generalSearchTerm;
        set => SetProperty(ref _generalSearchTerm, value);
    }

    public ObservableCollection<string> SearchTerms { get; } = new();

}

public sealed class LazyPlaylistTrackViewModel : ObservableObject
{
    private bool _hasValue;
    private PlaylistTrackViewModel? _track;

    public bool HasValue
    {
        get => _hasValue;
        set => SetProperty(ref _hasValue, value);
    }

    public PlaylistTrackViewModel? Track
    {
        get => _track;
        set => SetProperty(ref _track, value);
    }

    public int Index { get; set; }

    public int AddOne(int i)
    {
        return i + 1;
    }

    public string FormatDuration(TimeSpan timeSpan)
    {
        var totalHours = (int)timeSpan.TotalHours;
        if (totalHours > 0)
        {
            //=> Duration.ToString(@"mm\:ss");
            return timeSpan.ToString(@"hh\:mm\:ss");
        }
        else
        {
            return timeSpan.ToString(@"mm\:ss");
        }
    }
}

public sealed class PlaylistTrackViewModel
{
    public PlaylistTrackViewModel(Track spotifyTrack, PlaylistTrackInfo trackInfo)
    {
        Name = spotifyTrack.Name;

        const string url = "https://i.scdn.co/image/";
        var images = spotifyTrack.Album.CoverGroup.Image.Select(c =>
        {
            var id = SpotifyId.FromRaw(c.FileId.Span, SpotifyItemType.Unknown);
            var hex = id.ToBase16();

            return ($"{url}{hex}", c.Width);
        }).ToList();
        SmallestImageUrl =images.OrderBy(x => x.Width).FirstOrDefault().Item1;
        BiggestImageUrl = images.OrderByDescending(x => x.Width).FirstOrDefault().Item1;

        Artists = spotifyTrack.Artist.Select(x => (SpotifyId.FromRaw(x.Gid.Span, SpotifyItemType.Artist).ToString(), x.Name))
            .ToImmutableArray();
        Album = (SpotifyId.FromRaw(spotifyTrack.Album.Gid.Span, SpotifyItemType.Album).ToString(),
            spotifyTrack.Album.Name);
        Duration = TimeSpan.FromMilliseconds(spotifyTrack.Duration);

        AddedAt = trackInfo.AddedAt;
        AddedBy = trackInfo.AddedBy;
        UniquePlaylistItemId = trackInfo.UniqueItemId;
    }

    public string Name { get; }
    public string? SmallestImageUrl { get; }
    public IReadOnlyCollection<(string Id, string Name)> Artists { get; }
    public (string Id, string Name) Album { get; }
    public TimeSpan Duration { get; }

    public DateTimeOffset? AddedAt { get; }
    public string? AddedBy { get; }
    public string? UniquePlaylistItemId { get; }
    public string BiggestImageUrl { get; }
}