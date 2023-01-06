﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Eum.Artwork;
using Eum.Connections.Spotify;
using Eum.Spotify.connectstate;
using Eum.UI.Items;
using ReactiveUI;

namespace Eum.UI.ViewModels.Playback
{
    [INotifyPropertyChanged]
    public abstract partial class PlaybackViewModel
    {
        [ObservableProperty]
        private CurrentlyPlayingHolder _item;

        [ObservableProperty]
        private bool _playingOnExternalDevice;

        [ObservableProperty]
        private RemoteDevice _externalDevice;

        [ObservableProperty] private double _timestamp;

        private IDisposable _disposable;
        private ItemId _activeDeviceId;

        protected PlaybackViewModel()
        {
        }

        public ICommand NavigateToAlbum => Commands.To(EumEntityType.Album);
        public ObservableCollection<RemoteDevice> RemoteDevices { get; } = new ObservableCollection<RemoteDevice>();
        public abstract ServiceType Service { get; }

        public virtual void Deconstruct()
        {
            StopTimer();
        }

        protected void StopTimer()
        {
            _disposable?.Dispose();
        }

        protected void StartTimer(long atPosition)
        {
            StopTimer();
            Timestamp = atPosition;

            _disposable =  Observable.Interval(TimeSpan.FromMilliseconds(200), RxApp.TaskpoolScheduler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(l =>
                {
                    Timestamp += 200;
                });
        }

        public event EventHandler<ItemId> PlayingItemChanged; 
        public abstract Task SwitchRemoteDevice(ItemId? deviceId);

        public ItemId ActiveDeviceId
        {
            get => _activeDeviceId;
            protected set
            {
                if (_activeDeviceId != value)
                {
                    OnPropertyChanged(nameof(ActiveDeviceId));
                    _activeDeviceId = value;
                    ActiveDeviceChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<ItemId> ActiveDeviceChanged; 
        protected virtual void OnPlayingItemChanged(ItemId e)
        {
            PlayingItemChanged?.Invoke(this, e);
        }
    }

    public record RemoteDevice(ItemId DeviceId, string DeviceName, DeviceType Devicetype);


    public record CurrentlyPlayingHolder : IDisposable
    {
        public Stream BigImage { get; init; }
        public Stream SmallImage { get; init; }

        public Uri BigImageUrl { get; init; }
        public ItemId Context { get; init; }
        public IdWithTitle Title { get; init; }
        public IdWithTitle[] Artists { get; init; }
        public double Duration { get; init; }
        public ItemId Id { get; init; }
        public void Dispose()
        {
            BigImage.Dispose();
            SmallImage.Dispose();
        }
    }

    public class IdWithTitle
    {
        public ItemId Id { get; init; }
        public string Title { get; init; }
    }
}
