using System.Net;
using System.Security.Cryptography;
using System.Text;
using WendYourWay.Data.Planning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace WendYourWay.Web.Phone;

/// <summary>A route on its way to the phone: its link, where it's served from, and until when.</summary>
public sealed record PhoneLink(Uri Url, IPAddress Address, DateTimeOffset Until);

/// <summary>Sends a route to the phone (T099).</summary>
public interface IPhoneShare
{
    /// <summary>The link the phone opens for this file; null with no home network to send it over.</summary>
    Task<PhoneLink?> ShareAsync(GpxFile gpx, CancellationToken cancellationToken = default);
}

/// <summary>
/// A route to the phone by QR code (PLAN T099; user, 2026-11-10: "QR code over home Wi-Fi"). The app
/// itself still listens on the laptop only; this is a listener of its own, started when asked and
/// for nothing else, on the home network's address: it serves the one file at a random link of 128
/// bits, once, for ten minutes, then stops. A new share ends the one before; so does the app's
/// closing. Nothing it does is logged: a request's path would hold the link.
/// </summary>
public sealed class PhoneShare(Func<IPAddress?> address, TimeSpan? lasts = null) : IPhoneShare, IAsyncDisposable
{
    /// <summary>How long a link lasts unless it's used first.</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(10);

    private readonly TimeSpan _lasts = lasts ?? Lasts;
    private readonly SemaphoreSlim _one = new(1, 1);
    private Share? _share;

    /// <summary>The home network's address, found afresh each time: the laptop moves.</summary>
    public static PhoneShare OnTheHomeNetwork() => new(() => LanAddress.Choose(LanAddress.OfThisMachine()));

    public async Task<PhoneLink?> ShareAsync(GpxFile gpx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gpx);
        await _one.WaitAsync(cancellationToken);
        try
        {
            await StopAsync();
            if (address() is not { } at) return null;
            _share = await Share.StartAsync(at, gpx, _lasts, cancellationToken);
            return _share.Link;
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _share, null) is { } share) await share.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _one.Dispose();
    }

    // One file, one link, one listener.
    private sealed class Share : IAsyncDisposable
    {
        private readonly WebApplication _host;
        private readonly byte[] _token;
        private readonly GpxFile _gpx;
        private readonly Timer _timer;
        private int _used;
        private int _stopped;

        private Share(WebApplication host, byte[] token, GpxFile gpx, TimeSpan lasts)
        {
            (_host, _token, _gpx) = (host, token, gpx);
            _timer = new Timer(_ => _ = StopAsync(), null, lasts, Timeout.InfiniteTimeSpan);
        }

        public PhoneLink Link { get; private set; } = null!;

        public static async Task<Share> StartAsync(IPAddress at, GpxFile gpx, TimeSpan lasts, CancellationToken cancellationToken)
        {
            // Its own host, from nothing of the app's: no URLs, no configuration, no logging.
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            // None of the app's settings: its appsettings.json allows only the laptop's own names
            // (AllowedHosts), which turned the phone away; its environment could name other URLs.
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Listen(at, 0);
            });
            var host = builder.Build();
            var token = RandomNumberGenerator.GetBytes(16);
            var share = new Share(host, token, gpx, lasts);
            host.Run(share.ServeAsync);
            await host.StartAsync(cancellationToken);

            var port = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
            share.Link = new PhoneLink(new Uri($"http://{at}:{port}/{Base64Url(token)}.gpx"), at, DateTimeOffset.Now + lasts);
            return share;
        }

        private async Task ServeAsync(HttpContext context)
        {
            var response = context.Response;
            response.Headers.CacheControl = "no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            if (!HttpMethods.IsGet(context.Request.Method) || !IsTheLink(context.Request.Path) || Interlocked.Exchange(ref _used, 1) == 1)
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            response.ContentType = "application/gpx+xml";
            response.Headers.ContentDisposition = $"attachment; filename=\"{_gpx.FileName}\"";
            // Once sent, its listener goes.
            response.OnCompleted(() =>
            {
                _ = StopAsync();
                return Task.CompletedTask;
            });
            await response.WriteAsync(_gpx.Content, Encoding.UTF8, context.RequestAborted);
        }

        // "/<token>.gpx", the token compared in constant time.
        private bool IsTheLink(PathString path)
        {
            var value = path.Value ?? "";
            if (!value.StartsWith('/') || !value.EndsWith(".gpx", StringComparison.Ordinal)) return false;
            var given = Encoding.ASCII.GetBytes(value[1..^4]);
            var expected = Encoding.ASCII.GetBytes(Base64Url(_token));
            return CryptographicOperations.FixedTimeEquals(given, expected);
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
            await _timer.DisposeAsync();
            await _host.StopAsync();
            await _host.DisposeAsync();
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
