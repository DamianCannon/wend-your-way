using System.Net;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Phone;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for sending a route to the phone (T099): nothing listens, the files are kept.</summary>
internal sealed class FakePhoneShare : IPhoneShare
{
    /// <summary>A made-up link on a made-up home network.</summary>
    public static readonly Uri Url = new("http://192.168.1.50:51234/AAAAAAAAAAAAAAAAAAAAAA.gpx");

    /// <summary>Every file shared, in turn.</summary>
    public List<GpxFile> Shared { get; } = [];

    /// <summary>With no home network, nothing is shared.</summary>
    public bool NoNetwork { get; set; }

    /// <summary>How long a link lasts from when it's shared: ten minutes, or already gone.</summary>
    public TimeSpan Lasts { get; set; } = TimeSpan.FromMinutes(10);

    public Task<PhoneLink?> ShareAsync(GpxFile gpx, CancellationToken cancellationToken = default)
    {
        if (NoNetwork) return Task.FromResult<PhoneLink?>(null);
        Shared.Add(gpx);
        return Task.FromResult<PhoneLink?>(new PhoneLink(Url, IPAddress.Parse("192.168.1.50"), DateTimeOffset.Now + Lasts));
    }
}
