using System.Net.NetworkInformation;

namespace DRPG.Client.Net;

/// <summary>
/// The MAC addresses the <c>0x18</c> packet carries (✅ owner decision: the real ones, like the original; docs/cliente/cena-jogo.md).
/// They only go into that packet: nothing here is logged, and the caller must not log the result either.
/// </summary>
public static class HardwareAddresses
{
    /// <summary>The text of a MAC address as the capture has it: <c>AA-BB-CC-DD-EE-FF</c>, upper case.</summary>
    public static string Format(ReadOnlySpan<byte> address) => Convert.ToHexString(address).Chunk(2).Select(c => new string(c)).Aggregate((a, b) => $"{a}-{b}");

    /// <summary>Up to <paramref name="max"/> addresses of the machine's adapters, in the order the system lists them; none for a loopback, a tunnel or an adapter without a 6-byte address.</summary>
    public static IReadOnlyList<string> Read(int max)
    {
        var result = new List<string>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (result.Count >= max)
                    break;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var bytes = adapter.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 6 && bytes.Any(b => b != 0))
                    result.Add(Format(bytes));
            }
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // no adapter list: the packet goes with empty fields, the server does not answer it anyway
        }
        return result;
    }
}
