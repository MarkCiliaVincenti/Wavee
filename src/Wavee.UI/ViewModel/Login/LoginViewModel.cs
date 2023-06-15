﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Eum.Spotify;
using Eum.Spotify.connectstate;
using Google.Protobuf;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Wavee.Spotify;
using Wavee.Spotify.Infrastructure.Connection;
using Wavee.UI.Core;
using Wavee.UI.Core.Sys.Live;

namespace Wavee.UI.ViewModel.Login;

public sealed class LoginViewModel : ObservableObject
{
    private Action<IAppState> _done;
    private bool _isLoading;
    private string _password;
    private string _username;
    private string? _errorMessage;

    public LoginViewModel(Action<IAppState> done)
    {
        _done = done;

        SignInCommand = new AsyncRelayCommand(SignInAsync, () => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password));

    }

    public async Task AttemptLoginStored(CancellationToken ct)
    {
        var defaultUser = await Task.Run(() => Global.RetrieveDefaultUsername!(), ct);

        if (defaultUser.IsNone)
            return;
        var password = await Task.Run(() => Global.RetrievePasswordFromVaultForUserFunc!(defaultUser.ValueUnsafe()), ct);
        if (password.IsNone)
            return;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            Global.SpotifyConfig = new SpotifyConfig(
                remote: new SpotifyRemoteConfig(
                    deviceName: "Wavee",
                    deviceType: DeviceType.Computer
                ),
                playback: new SpotifyPlaybackConfig(
                    crossfadeDuration: TimeSpan.FromSeconds(10),
                    preferedQuality: PreferredQualityType.High
                ),
                cache: new SpotifyCacheConfig(
                    cacheRoot: Global.GetPersistentStoragePath()
                )
            );
            var client = await Task.Run(() => SpotifyClient.CreateAsync(Global.SpotifyConfig, new LoginCredentials
            {
                Username = defaultUser.ValueUnsafe(),
                AuthData = ByteString.FromBase64(password.ValueUnsafe()),
                Typ = AuthenticationType.AuthenticationStoredSpotifyCredentials
            }), ct);
            var state = new LiveAppState(client);
            await Task.Run(() => Global.SavePasswordToVaultForUserAction!(client.WelcomeMessage.CanonicalUsername,
                client.WelcomeMessage.ReusableAuthCredentials.ToBase64()), ct);
            //await Task.Delay(2000, arg);
            // var mockState = new MockAppState(new UserProfile(Username, Username, Option<string>.None));
            _done(state);
            _done = null;
            return;
        }
        catch (SpotifyAuthenticationException authException)
        {
            ErrorMessage = authException.ErrorCode.ErrorCode switch
            {
                ErrorCode.BadCredentials => "Invalid username or password",
                _ => authException.ErrorCode.ErrorCode.ToString()
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SignInAsync(CancellationToken arg)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            if (Global.IsMock)
            {
                Global.SpotifyConfig = new SpotifyConfig(
                    remote: new SpotifyRemoteConfig(
                        deviceName: "Wavee",
                        deviceType: DeviceType.Computer
                    ),
                    playback: new SpotifyPlaybackConfig(
                        crossfadeDuration: TimeSpan.FromSeconds(10),
                        preferedQuality: PreferredQualityType.High
                    ),
                    cache: new SpotifyCacheConfig(
                        cacheRoot: Global.GetPersistentStoragePath()
                    )
                );
                var client = await SpotifyClient.CreateAsync(Global.SpotifyConfig, new LoginCredentials
                {
                    Username = Username,
                    AuthData = ByteString.CopyFromUtf8(Password),
                    Typ = AuthenticationType.AuthenticationUserPass
                });
                var state = new LiveAppState(client);

                await Task.Run(() => Global.SavePasswordToVaultForUserAction!(client.WelcomeMessage.CanonicalUsername, client.WelcomeMessage.ReusableAuthCredentials.ToBase64()), arg);
                //await Task.Delay(2000, arg);
                // var mockState = new MockAppState(new UserProfile(Username, Username, Option<string>.None));
                _done(state);
                _done = null;
                return;
            }
        }
        catch (SpotifyAuthenticationException authException)
        {
            ErrorMessage = authException.ErrorCode.ErrorCode switch
            {
                ErrorCode.BadCredentials => "Invalid username or password",
                _ => authException.ErrorCode.ErrorCode.ToString()
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                SignInCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                SignInCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public AsyncRelayCommand SignInCommand { get; }
}