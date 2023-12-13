﻿using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Mediator;
using Wavee.Spotify.Common;
using Wavee.Spotify.Domain.Common;
using Wavee.UI.Domain.Artist;
using Wavee.UI.Domain.Library;
using Wavee.UI.Domain.Playback;
using Wavee.UI.Extensions;
using Wavee.UI.Features.Album.ViewModels;
using Wavee.UI.Features.Artist.Queries;
using Wavee.UI.Features.Artist.ViewModels;
using Wavee.UI.Features.Library.Notifications;
using Wavee.UI.Features.Library.Queries;
using Wavee.UI.Features.Navigation;
using Wavee.UI.Features.Navigation.ViewModels;
using Wavee.UI.Features.Playback;
using Wavee.UI.Features.Playback.ViewModels;
using Wavee.UI.Test;

namespace Wavee.UI.Features.Library.ViewModels.Artist;

public sealed class LibraryArtistsViewModel : NavigationItemViewModel, IPlaybackChangedListener
{
    private readonly IMediator _mediator;

    private string? _query;
    private bool _isLoading;
    private string _sortField;
    private int _total;
    private LibraryArtistViewModel? _selectedArtist;
    private readonly INavigationService _navigationService;
    private IUIDispatcher _dispatcher;
    private readonly PlaybackViewModel _playback;
    public LibraryArtistsViewModel(IMediator mediator,
        INavigationService navigationService,
        IUIDispatcher dispatcher,
        PlaybackViewModel playback)
    {
        _mediator = mediator;
        _navigationService = navigationService;
        _dispatcher = dispatcher;
        _playback = playback;
        PlayCommand = new AsyncRelayCommand<object>(async x =>
        {
            try
            {
                var playbackVm = _playback;
                switch (x)
                {
                    case AlbumTrackViewModel track:
                        //TODO: Can this be more??
                        var artist = Artists.Single(z => z.Albums.Contains(track.Album));
                        var ctx = PlayContext.FromLibraryArtist(artist)
                            .FromDiscography(PlayContextDiscographyGroupType.All)
                            .StartWithAlbum(track.Album)
                            .StartWithTrack(track)
                            .Build();
                        await playbackVm.Play(ctx);
                        break;
                }
            }
            catch (InvalidOperationException)
            {

            }
        });
        _sortField = nameof(LibraryItem<SimpleArtistEntity>.AddedAt);

        SortFields = new[]
        {
            nameof(LibraryItem<SimpleArtistEntity>.AddedAt),
            nameof(LibraryItem<SimpleArtistEntity>.Item.Name),
            "Recents"
        };
        Artists = new ObservableCollection<LibraryArtistViewModel>();
    }
    public ObservableCollection<LibraryArtistViewModel> Artists { get; private set; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }


    public string? Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }

    public int Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    public LibraryArtistViewModel? SelectedArtist
    {
        get => _selectedArtist;
        set => SetProperty(ref _selectedArtist, value);
    }

    public string SortField
    {
        get => _sortField;
        set => SetProperty(ref _sortField, value);
    }

    public IReadOnlyCollection<string> SortFields { get; }

    public async Task Initialize()
    {
        try
        {
            Artists ??= new ObservableCollection<LibraryArtistViewModel>();
            if (Artists.Count is not 0) return;
            Artists.Clear();
            var library = await _mediator.Send(new GetLibraryArtistsQuery()
            {
                Offset = 0,
                Limit = 20,
                Search = _query,
                SortField = _sortField
            });
            HandleResult(library);
        }
        catch (Exception e)
        {
            HandleError(e);
        }
    }

    public async Task FetchPage(int offset, CancellationToken cancellationToken)
    {
        try
        {
            var library = await _mediator.Send(new GetLibraryArtistsQuery()
            {
                Offset = offset,
                Limit = 20,
                Search = _query,
                SortField = _sortField
            }, cancellationToken);
            HandleResult(library);
        }
        catch (Exception e)
        {
            HandleError(e);
        }
    }

    private void HandleResult(LibraryItems<SimpleArtistEntity> artistsResult)
    {
        foreach (var artist in artistsResult.Items)
        {
            var bigImage = artist.Item.Images.MaxBy(z => z.Height ?? 0);
            var smallestImage = artist.Item.Images.MinBy(z => z.Height ?? 0);
            var mediumImage = artist.Item.Images.OrderBy(z => z.Height ?? 0).Skip(1).FirstOrDefault();
            var vm = new LibraryArtistViewModel
            {
                Name = artist.Item.Name,
                Id = artist.Item.Id,
                BigImageUrl = bigImage.Url,
                MediumImageUrl = mediumImage.Url,
                SmallImageUrl = smallestImage.Url,
                AddedAt = artist.AddedAt,
                TotalAlbums = null
            };
            vm.FetchArtistAlbumsFunc = (i, i1, arg3) => FetchArtistAlbums(vm, i, i1, arg3);
            Artists.Add(vm);
        }
        Total = artistsResult.Total;
    }

    private void HandleError(Exception exception)
    {

    }

    public async Task FetchArtistAlbums(LibraryArtistViewModel artist,
        int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset is 0) artist.Albums.Clear();
        var results = await _mediator.Send(new GetAlbumsForArtistQuery
        {
            Id = artist.Id,
            Offset = (int)offset,
            Limit = (int)limit,
        }, cancellationToken);

        artist.TotalAlbums = results.Total;
        foreach (var album in results.Albums)
        {
            var bigImage = album.Images.MaxBy(z => z.Height ?? 0);
            //   var smallestImage = album.Images.MinBy(z => z.Height ?? 0);
            var mediumImage = album.Images.OrderBy(z => z.Height ?? 0).Skip(1).FirstOrDefault();
            var vm = new AlbumViewModel
            {
                Name = album.Name,
                Id = album.Id,
                BigImageUrl = bigImage.Url,
                TotalSongs = (uint)album.Tracks.Count,
                Duration = TimeSpan.FromMilliseconds(album.Tracks.Sum(x => x.Duration.TotalMilliseconds)),
                Year = album.Year ?? 1,
                Type = album.Type,
                Tracks = null,
                MediumImageUrl = mediumImage.Url,
            };
            vm.Tracks = album.Tracks.Select((x, i) => new AlbumTrackViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Duration = x.Duration,
                Number = i + 1,
                PlayCommand = PlayCommand,
                Album = vm,
                PlaybackState = _playback.IsPlaying(x.Id, null)
            })
                .ToArray();
            artist.Albums.Add(vm);
        }
    }

    public AsyncRelayCommand<object> PlayCommand { get; }

    public async Task Add(int items)
    {
        var itemsCount = Artists.Count;
        var library = await _mediator.Send(new GetLibraryArtistsQuery()
        {
            Offset = 0,
            Limit = itemsCount + items,
            Search = _query,
            SortField = _sortField
        });
        Artists.Clear();
        HandleResult(library);
    }

    public void Remove(ImmutableArray<string> ids)
    {
        var artistsToRemove = Artists.Where(x => ids.Contains(x.Id)).ToArray();
        foreach (var artist in artistsToRemove)
        {
            Artists.Remove(artist);
        }
    }

    public void NavigateToArtist(string id)
    {
        _navigationService.NavigateToArtist(id);
        // _navigationService.Navigate(null, new ArtistViewModel(_mediator, id, _dispatcher));
    }

    public void OnPlaybackChanged(PlaybackViewModel player)
    {
        foreach (var artist in Artists)
        {
            foreach (var album in artist.Albums)
            {
                foreach (var track in album.Tracks)
                {
                    track.PlaybackState = player.IsPlaying(track.Id, null);
                }
            }
        }
    }
}

public enum PlayContextDiscographyGroupType
{
    All,
    Albums,
    Singles,
    Compilations
}