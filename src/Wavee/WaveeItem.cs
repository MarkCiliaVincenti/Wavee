using System.Buffers.Text;
using System.Collections.Immutable;
using System.Net;
using LanguageExt;

namespace Wavee;

public interface IWaveeItem
{
    string Name { get; }
    string Id { get; }
}

public interface IWaveePlayableItem
{
    TimeSpan Duration { get; }
    string? Id { get; }
}

public readonly struct UrlImage
{
    public required string Url { get; init; }
    public required uint? Width { get; init; }
    public required uint? Height { get; init; }

    public required UrlImageSizeType CommonSize { get; init; }
}

public enum UrlImageSizeType
{
    Default,
    Small,
    Medium,
    Large,
    ExtraLarge
}

public interface IWaveeAlbumArtist : IWaveeItem
{
}

public enum WaveeRepeatStateType
{
    None,
    Context,
    Track
}

public interface IWaveeTrackAlbum : IWaveeItem
{
    Seq<UrlImage> Images { get; }
    string Id { get; }
    int Year { get; }
}

public interface IWaveeAlbum : IWaveeItem
{
    Seq<UrlImage> Images { get; }
    Seq<IWaveeAlbumArtist> Artists { get; }
    string Id { get; }
    int Year { get; }
    string Type { get; }
    int TotalTracks { get; }
}

public readonly record struct LocalFile : IWaveePlayableItem
{
    public required string Path { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Id { get; init; }

    public static LocalFile FromPath(string path)
    {
        using var tag = TagLib.File.Create(path);
        var duration = tag.Properties.Duration;
        return new LocalFile
        {
            Path = path,
            Duration = duration,
            Id = $"local:track:{SafeUrl(path)}"
        };
    }

    private static string SafeUrl(string path)
    {
        //url encode
        var x = WebUtility.UrlEncode(path);
        return x;
    }
}