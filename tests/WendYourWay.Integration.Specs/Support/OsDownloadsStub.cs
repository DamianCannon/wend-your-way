using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WendYourWay.Integration.Specs;

/// <summary>
/// A stand-in for OS's Downloads API (T171), answering an HttpClient: a product's release at
/// `products/{id}`, its files at `products/{id}/downloads`, and each file at its own address. What was
/// asked for is kept.
/// </summary>
internal sealed class OsDownloadsStub : HttpMessageHandler
{
    public const string Api = "https://os.test/downloads/v1/";

    private readonly Dictionary<string, (string Version, string Format, string FileName, byte[] Bytes)> _products = [];

    /// <summary>Every address asked for, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>Set, the downloads list gives this MD5 instead of the file's own.</summary>
    public string? WrongMd5 { get; set; }

    /// <summary>Offers <paramref name="bytes"/> as <paramref name="product"/>'s GB file in <paramref name="format"/>, of <paramref name="version"/>.</summary>
    public OsDownloadsStub Offering(string product, string version, string format, string fileName, byte[] bytes)
    {
        _products[product] = (version, format, fileName, bytes);
        return this;
    }

    // MD5 because OS publishes it: a check the file arrived whole, not of who sent it.
#pragma warning disable CA5351
    public static string Md5Of(byte[] bytes) => Convert.ToHexStringLower(MD5.HashData(bytes));
#pragma warning restore CA5351

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Asked.Add(url);
        foreach (var (id, p) in _products)
        {
            var file = $"{Api}products/{id}/downloads?area=GB&format={Uri.EscapeDataString(p.Format)}&redirect";
            if (url == $"{Api}products/{id}")
                return Json($$"""{"id":"{{id}}","name":"OS {{id}}","version":"{{p.Version}}","areas":["GB"]}""");
            if (url == $"{Api}products/{id}/downloads")
                return Json($$"""
                    [{"md5":"{{WrongMd5 ?? Md5Of(p.Bytes)}}","size":{{p.Bytes.Length}},"url":"{{file}}","format":"{{p.Format}}","area":"GB","fileName":"{{p.FileName}}"},
                     {"md5":"00","size":1,"url":"{{Api}}products/{{id}}/downloads?area=GB&format=Other&redirect","format":"Other","area":"GB","fileName":"other.zip"}]
                    """);
            if (url == file)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(p.Bytes) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json(string json) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
}
