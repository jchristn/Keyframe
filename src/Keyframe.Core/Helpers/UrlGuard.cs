namespace Keyframe.Core.Helpers
{
    using System;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// Classifies addresses for server-side request forgery protection.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class UrlGuard
    {
        /// <summary>
        /// Whether an address is loopback, link-local, private, carrier-grade NAT, multicast, or unspecified.
        /// </summary>
        /// <param name="address">Address.</param>
        /// <returns>True when the address must not be reached from a media URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when address is null.</exception>
        public static bool IsRestricted(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return true;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                if (b[0] == 10) return true;
                if (b[0] == 127) return true;
                if (b[0] == 0) return true;
                if (b[0] == 169 && b[1] == 254) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
                if (b[0] >= 224) return true;
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
                byte[] b = address.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return true;
                return false;
            }

            return true;
        }
    }
}
