﻿using System.Globalization;
using System.Reactive.Linq;
using Eum.Spotify.connectstate;
using ReactiveUI;
using LanguageExt;
using Wavee.Spotify;
using Wavee.UI.Infrastructure.Sys;
using Wavee.UI.Infrastructure.Traits;

namespace Wavee.UI.ViewModels;

public sealed class SettingsViewModel<R> : ReactiveObject, INavigableViewModel where R : struct, HasFile<R>, HasDirectory<R>, HasLocalPath<R>, HasSpotify<R>
{
    private bool _setupCompleted;
    private int _setupProgress;
    private int _width;
    private int _height;
    private AppTheme _currentTheme;
    private AppLocale _currentLocale;
    private DeviceType _deviceType;
    private string _deviceName;

    public SettingsViewModel(R runtime)
    {
        Instance = this;
        CurrentLocale = AppLocale.EnglishUS;
        AvailableLocales = Seq1(
            AppLocale.EnglishUS
        // ,
        // AppLocale.Korean,
        // AppLocale.Japanese,
        // AppLocale.Dutch
        );

        // this.WhenAnyValue(
        //         x => x.SetupCompleted,
        //         x => x.SetupProgress)
        //     .Skip(1)
        //     .ObserveOn(RxApp.TaskpoolScheduler)
        //     .Select(async (___) =>
        //     {
        //         var aff =
        //             from _ in UiConfig<R>.SetSetupCompleted(SetupCompleted)
        //             from __ in UiConfig<R>.SetSetupProgress(SetupProgress)
        //             select Unit.Default;
        //
        //         var run = await aff.Run(runtime);
        //     })
        //     .Subscribe();

        this.WhenAnyValue(
                x => x.Height,
                x => x.Width)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Select(async (___) =>
            {
                var aff =
                    from _ in UiConfig<R>.SetWindowWidth((uint)Width)
                    from __ in UiConfig<R>.SetWindowHeight((uint)Height)
                    select Unit.Default;

                var run = await aff.Run(runtime);
            })
            .Subscribe();

        this.WhenAnyValue(
                x => x.CurrentTheme,
                x => x.CurrentLocale
                )
            .Skip(1)
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Select(async (___) =>
            {
                var aff =
                    from _ in UiConfig<R>.SetTheme(CurrentTheme)
                    from __ in UiConfig<R>.SetLocale(CurrentLocale.Culture.Name)
                    select Unit.Default;

                var run = await aff.Run(runtime);
            }).Subscribe();

        this.WhenAnyValue(
                           x => x.DeviceName,
                           x => x.DeviceType
            )
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Select(async (d, t) =>
            {
                Config.Remote.DeviceName = d.Item1;
                Config.Remote.DeviceType = d.Item2;

                var aff =
                    from _ in UiConfig<R>.SetDeviceName(DeviceName)
                    from __ in UiConfig<R>.SetDeviceType(DeviceType)
                    from ____ in Spotify<R>.RefreshRemoteState()
                    select Unit.Default;

                var run = await aff.Run(runtime);
            }).Subscribe();
    }
    public static SettingsViewModel<R> Instance { get; private set; }

    // #region Setup
    // public bool SetupCompleted
    // {
    //     get => _setupCompleted;
    //     set => this.RaiseAndSetIfChanged(ref _setupCompleted, value);
    // }
    //
    // public int SetupProgress
    // {
    //     get => _setupProgress;
    //     set => this.RaiseAndSetIfChanged(ref _setupProgress, value);
    // }
    // #endregion


    #region Window 

    public int Width
    {
        get => _width;
        set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => this.RaiseAndSetIfChanged(ref _height, value);
    }

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        set => this.RaiseAndSetIfChanged(ref _currentTheme, value);
    }

    public AppLocale CurrentLocale
    {
        get => _currentLocale;
        set => this.RaiseAndSetIfChanged(ref _currentLocale, value);
    }

    public DeviceType DeviceType
    {
        get => _deviceType;
        set => this.RaiseAndSetIfChanged(ref _deviceType, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        set => this.RaiseAndSetIfChanged(ref _deviceName, value);
    }
    public Seq<AppLocale> AvailableLocales { get; }
    public SpotifyConfig Config { get; set; }

    #endregion

    public void OnNavigatedTo(object? parameter)
    {

    }

    public void OnNavigatedFrom()
    {
    }
}

public record AppLocale(CultureInfo Culture, string EnglishName, string NativeName)
{
    public static AppLocale EnglishUS { get; } = new(CultureInfo.GetCultureInfo("en-US"), "United States", "English");
    public static AppLocale Korean { get; } = new(CultureInfo.GetCultureInfo("ko-KR"), "Korean", "한국어");
    public static AppLocale Japanese { get; } = new(CultureInfo.GetCultureInfo("ja-JP"), "Japanese", "日本語");
    public static AppLocale Dutch { get; } = new(CultureInfo.GetCultureInfo("nl-NL"), "Dutch", "Nederlands");

    public static AppLocale Find(string locale)
    {
        return locale switch
        {
            "en-US" => EnglishUS,
            "ko-KR" => Korean,
            "ja-JP" => Japanese,
            "nl-NL" => Dutch,
            _ => EnglishUS
        };
    }
}

public enum AppTheme : int
{
    System = 0,
    Light = 1,
    Dark = 2
}