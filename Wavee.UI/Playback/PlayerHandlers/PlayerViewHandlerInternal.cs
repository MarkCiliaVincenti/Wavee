﻿using System.Threading.Channels;
using Wavee.UI.Interfaces.Playback;

namespace Wavee.UI.Playback.PlayerHandlers;

internal abstract class PlayerViewHandlerInternal : IDisposable
{
    protected PlayerViewHandlerInternal()
    {
        var channels = Channel.CreateUnbounded<IPlayerViewModelEvent>();
        Events = channels.Reader;
        EventsWriter = channels.Writer;
    }

    public ChannelReader<IPlayerViewModelEvent> Events
    {
        get;
    }
    protected ChannelWriter<IPlayerViewModelEvent> EventsWriter
    {
        get;
    }

    public abstract TimeSpan Position
    {
        get;
    }

    public virtual void Dispose()
    {
    }

    public abstract Task LoadTrackList(IPlayContext context);

    public abstract ValueTask Seek(double position);

    public abstract ValueTask Resume();
    public abstract ValueTask Pause();
}