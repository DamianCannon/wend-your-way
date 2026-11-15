using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WendYourWay.Web.Phone;

/// <summary>One of the laptop's network adapters, as far as choosing an address goes.</summary>
/// <param name="Virtual">Hyper-V's, WSL's, Docker's or a VPN's: not one a phone on the Wi-Fi can reach.</param>
/// <param name="HasGateway">A default gateway: it reaches a network, not just the laptop itself.</param>
public sealed record NetworkCard(string Name, bool Up, bool Virtual, bool HasGateway, IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// The laptop's address on the home network (T099), which the phone is sent to: an adapter that's
/// up, not virtual, with a default gateway, at a private IPv4 address (10/8, 172.16/12,
/// 192.168/16). Never a public, link-local or loopback one.
/// </summary>
public static class LanAddress
{
    // Adapters a phone on the Wi-Fi can't reach, by how Windows names them.
    private static readonly string[] VirtualNames = ["hyper-v", "vethernet", "wsl", "docker", "virtualbox", "vmware", "loopback", "tap-", "wireguard", "tailscale", "vpn"];

    public static IPAddress? Choose(IEnumerable<NetworkCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards
            .Where(card => card.Up && !card.Virtual && card.HasGateway)
            .SelectMany(card => card.Addresses)
            .FirstOrDefault(IsPrivate);
    }

    /// <summary>Whether it's a private IPv4 address, one a home network gives out.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>This laptop's adapters, as they are now.</summary>
    public static IEnumerable<NetworkCard> OfThisMachine() =>
        NetworkInterface.GetAllNetworkInterfaces().Select(n =>
        {
            var properties = n.GetIPProperties();
            var name = $"{n.Name} {n.Description}".ToLowerInvariant();
            return new NetworkCard(
                n.Name,
                n.OperationalStatus == OperationalStatus.Up,
                n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel || VirtualNames.Any(name.Contains),
                properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)),
                [.. properties.UnicastAddresses.Select(u => u.Address)]);
        });
}
