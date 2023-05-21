using System;
using System.Linq;
using CommunityToolkit.Labs.WinUI;
using FontAwesome6;
using LanguageExt;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading;
using Wavee.UI.Infrastructure.Live;
using Wavee.UI.Infrastructure.Sys;

namespace Wavee.UI.WinUI.Views.Artist;

public sealed partial class ArtistAbout : UserControl
{
    public ArtistAbout(string artistId)
    {
        this.InitializeComponent();

        System.Threading.Tasks.Task.Run(async () =>
        {
            const string fetch_uri = "hm://creatorabout/v0/artist-insights/{0}?format=json&locale={1}";
            var url = string.Format(fetch_uri, artistId, "en");
            var aff =
                from mercuryClient in Spotify<WaveeUIRuntime>.Mercury().Map(x => x)
                from response in mercuryClient.Get(url, CancellationToken.None).ToAff()
                select response;
            var result = await aff.Run(runtime: App.Runtime);
            var r = result.ThrowIfFail();

            using var jsonDocument = JsonDocument.Parse(r.Payload);
            var name = jsonDocument.RootElement.GetProperty("name").GetString();
            var mainimageUrl = jsonDocument.RootElement.GetProperty("mainImageUrl").GetString();

            var autobiography = jsonDocument.RootElement.GetProperty("autobiography");
            var body = autobiography.TryGetProperty("body", out var b) ? b.GetString() : null;
            var links = autobiography.TryGetProperty("links", out var lk) ? lk.EnumerateObject()
                .Map(x => new ArtistLink(x.Name, x.Value.GetString())).ToArr().ToSeq()
                    : Seq<ArtistLink>.Empty;
            var biography = jsonDocument.RootElement.TryGetProperty("biography", out var bio) ? bio.GetString() : null;
            var images = jsonDocument.RootElement.TryGetProperty("images", out var imgs) ? (imgs.EnumerateArray()
                .Map(x => new Artwork
                {
                    Uri = x.GetProperty("uri").GetString(),
                    Width = x.GetProperty("width").GetInt32(),
                    Height = x.GetProperty("height").GetInt32()
                }).ToArr().ToSeq()) : Seq<Artwork>.Empty;
            var globalChartPosition = jsonDocument.RootElement.GetProperty("globalChartPosition").GetInt32();
            var monthlyListeners = jsonDocument.RootElement.GetProperty("monthlyListeners").GetUInt64();
            var monthlyListenersDelta = jsonDocument.RootElement.GetProperty("monthlyListenersDelta").GetInt64();
            var followers = jsonDocument.RootElement.GetProperty("followerCount").GetUInt64();
            var followingCount = jsonDocument.RootElement.GetProperty("followingCount").GetUInt32();
            var city = jsonDocument.RootElement.GetProperty("cities").EnumerateArray()
                .Map((i, x) => new ArtistCity
                {
                    Country = x.GetProperty("country")
                        .GetString(),
                    Region = x.GetProperty("region")
                        .GetString(),
                    City = x.GetProperty("city")
                        .GetString(),
                    Listeners = x.GetProperty("listeners")
                        .GetUInt64(),
                    Index = (uint)i
                }).ToArr().ToSeq();

            var info = new ArtistAboutView
            {
                Images = images,
                GlobalChartPosition = globalChartPosition,
                MonthlyListeners = monthlyListeners,
                MonthlyListenersDelta = monthlyListenersDelta,
                Followers = followers,
                FollowingCount = followingCount,
                City = city,
                Autobiography = body,
                Links = links,
                Biography = biography,
                Name = name,
                Image = mainimageUrl
            };

            this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                Info = info;
                //(2)|(1/1), so we need 3 images
                if (info.Images.Count > 2)
                {
                    LeftImage.Source = new BitmapImage(new Uri(info.Images[0].Uri));
                    TopRightImage.Source = new BitmapImage(new Uri(info.Images[1].Uri));
                    BottomRightImage.Source = new BitmapImage(new Uri(info.Images[2].Uri));
                }
                else if (info.Images.Count > 1)
                {
                    LeftImage.Source = new BitmapImage(new Uri(info.Images[0].Uri));
                    TopRightImage.Source = new BitmapImage(new Uri(info.Images[1].Uri));
                    //set rowspan to 2 and hide bottom right image
                    //also set the ratio of columns to 1:1
                    BottomRightImage.Visibility = Visibility.Collapsed;
                    Grid.SetRowSpan(TopRightImage, 2);

                    LeftGrid.Width = new GridLength(1, GridUnitType.Star);
                    RightGrid.Width = new GridLength(1, GridUnitType.Star);
                }
                else if (info.Images.Count > 0)
                {
                    LeftImage.Source = new BitmapImage(new Uri(info.Images[0].Uri));
                    //hide top right and bottom right images
                    //also set the ratio of columns to 1 (fulL), so hide the right grid
                    TopRightImage.Visibility = Visibility.Collapsed;
                    BottomRightImage.Visibility = Visibility.Collapsed;
                    RightGrid.Width = new GridLength(0, GridUnitType.Pixel);
                }
                else
                {
                    //no images. hide gallery
                    Gallery.Visibility = Visibility.Collapsed;
                }

                if (!links.Any())
                {
                    LinksPanel.Visibility = Visibility.Collapsed;
                }
                if (!string.IsNullOrEmpty(info.Autobiography))
                {
                    BiographySegments.Items.Add(new SegmentedItem
                    {
                        Content = "Autobiography",
                        Tag = "auto"
                    });
                }
                if (!string.IsNullOrEmpty(info.Biography))
                {
                    BiographySegments.Items.Add(new SegmentedItem
                    {
                        Content = "Biography",
                        Tag = "biography"
                    });
                }

                if (BiographySegments.Items.Count == 1)
                {
                    BiographySegments.Visibility = Visibility.Collapsed;
                }
                if (!BiographySegments.Items.Any())
                {
                    Biographies.Visibility = Visibility.Collapsed;
                }
                PostedByPicture.ProfilePicture = new BitmapImage(new Uri(info.Image));
                this.Bindings.Update();
            });
        });
    }
    public void Clear()
    {

    }

    public ArtistAboutView Info { get; set; }

    private void BiographySegments_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //BiographyContent
        var item = (SegmentedItem)e.AddedItems[0];
        if (item.Tag is "auto")
        {
            BiographyContent.Text = Info.Autobiography;
            PostedByArtist.Visibility = Visibility.Visible;
        }
        else if (item.Tag is "biography")
        {
            BiographyContent.Text = Info.Biography;
            PostedByArtist.Visibility = Visibility.Collapsed;
        }
    }

    public string FormatMonthlyListeners(ulong val)
    {
        return val.ToString("N0");
    }
}

public readonly struct ArtistAboutView
{
    public required string? Autobiography { get; init; }
    public required Seq<ArtistLink> Links { get; init; }
    public required string? Biography { get; init; }
    public required Seq<Artwork> Images { get; init; }
    public required Option<int> GlobalChartPosition { get; init; }
    public required ulong MonthlyListeners { get; init; }
    public required long MonthlyListenersDelta { get; init; }
    public required ulong Followers { get; init; }
    public required uint FollowingCount { get; init; }
    public required Seq<ArtistCity> City { get; init; }
    public required string Name { get; init; }
    public required string Image { get; init; }
}

public readonly record struct ArtistLink(string Key, string Ref)
{
    public EFontAwesomeIcon GetIcon(string s)
    {
        return s switch
        {
            "facebook" => EFontAwesomeIcon.Brands_Facebook,
            "twitter" => EFontAwesomeIcon.Brands_Twitter,
            "instagram" => EFontAwesomeIcon.Brands_Instagram,
            "wikipedia" => EFontAwesomeIcon.Brands_WikipediaW,
            _ => EFontAwesomeIcon.Solid_Link
        };
    }
}

public readonly struct ArtistCity
{
    public required string Country { get; init; }
    public required string Region { get; init; }
    public required string City { get; init; }
    public required ulong Listeners { get; init; }
    public required uint Index { get; init; }

    public string FormatIndex(uint index0)
    {
        return (index0 + 1).ToString("N0");
    }

    public string FormatDisplayName(string s, string s1)
    {
        return $"{s}, {s1}";
    }

    public string FormatListeners(ulong @ulong)
    {
        return @ulong.ToString("N0");
    }
}
public readonly struct Artwork
{
    public required string Uri { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}