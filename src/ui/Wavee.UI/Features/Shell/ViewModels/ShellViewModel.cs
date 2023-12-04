﻿using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using Wavee.UI.Features.Library.ViewModels;
using Wavee.UI.Features.Library.ViewModels.Album;
using Wavee.UI.Features.Library.ViewModels.Artist;
using Wavee.UI.Features.Listen;
using Wavee.UI.Features.Navigation;
using Wavee.UI.Features.Navigation.ViewModels;
using Wavee.UI.Features.NowPlaying.ViewModels;
using Wavee.UI.Features.Playback.ViewModels;
using Wavee.UI.Features.Search.ViewModels;

namespace Wavee.UI.Features.Shell.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private NavigationItemViewModel? _selectedItem;

    public ShellViewModel(
        ListenViewModel listen,
        LibrariesViewModel library,
        NowPlayingViewModel nowPlaying,
        INavigationService navigation, PlaybackViewModel playback, SearchViewModel search)
    {
        TopNavItems = new object[]
        {
            listen,
            library,
            nowPlaying
        };
        SelectedItem = listen;
        Navigation = navigation;
        Playback = playback;
        Search = search;

        navigation.NavigatedTo += (sender, o) =>
        {
            var type = o.GetType();
            if (type == typeof(ListenViewModel))
            {
                SelectedItem = listen;
            }
            else if (type == typeof(LibrariesViewModel))
            {
                SelectedItem = library;
            }
            else if (type == typeof(NowPlayingViewModel))
            {
                SelectedItem = nowPlaying;
            }
            else if (type == typeof(LibrarySongsViewModel))
            {
                SelectedItem = library;
                library.SelectedItem = library.Songs;
            }
            else if (type == typeof(LibraryAlbumsViewModel))
            {
                SelectedItem = library;
                library.SelectedItem = library.Albums;
            }
            else if (type == typeof(LibraryArtistsViewModel))
            {
                SelectedItem = library;
                library.SelectedItem = library.Artists;
            }
            else if (type == typeof(LibraryPodcastsViewModel))
            {
                SelectedItem = library;
                library.SelectedItem = library.Podcasts;
            }
            else
            {
                SelectedItem = new NothingSelectedViewModel();
            }
        };
    }

    public INavigationService Navigation { get; }
    public IReadOnlyCollection<object> TopNavItems { get; }

    public NavigationItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public PlaybackViewModel Playback { get; }
    public SearchViewModel Search { get; }
}

public sealed class NothingSelectedViewModel : NavigationItemViewModel
{
}