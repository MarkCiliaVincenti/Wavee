﻿
using System.Windows.Input;

namespace Wavee.UI.Features.Album.ViewModels;

public sealed class AlbumTrackViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int Number { get; init; }

    public string DurationString
    {
        get
        {
            var totalHours = (int)Duration.TotalHours;
            var totalMinutes = (int)Duration.TotalMinutes;
            var totalSeconds = (int)Duration.TotalSeconds;
            if (totalHours > 0)
            {
                //=> Duration.ToString(@"mm\:ss");
                return Duration.ToString(@"hh\:mm\:ss");
            }
            else
            {
                return Duration.ToString(@"mm\:ss");
            }
        }
    }

    public required ICommand PlayCommand { get; init; }
    public object This => this;
    public required AlbumViewModel Album { get; init; }
}