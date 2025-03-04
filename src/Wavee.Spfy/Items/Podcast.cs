using System.Collections.Immutable;
using LanguageExt;

namespace Wavee.Spfy.Items;

public readonly record struct SpotifySimpleEpisode : ISpotifyPlayableItem
{
    public required SpotifyId Uri { get; init; }
    public required string Name { get; init; }
    public Seq<SpotifyPlayableItemDescription> Descriptions { get; }
    public ISpotifyPlayableItemGroup Group { get; }
    public Seq<SpotifyAudioFile> AudioFiles { get; }
    public Seq<SpotifyAudioFile> PreviewFiles { get; }
    public required TimeSpan Duration { get; init; }
    public required ImmutableArray<UrlImage> Images { get; init; }
    public required string Description { get; init; }
    public string? Id => Uri.ToString();
    public bool Explicit { get; }
}
public readonly struct SpotifySimpleShow : ISpotifyItem
{
    public required SpotifyId Uri { get; init; }
    public string Id => Uri.ToString();

    public string Name => throw new NotImplementedException();
}
