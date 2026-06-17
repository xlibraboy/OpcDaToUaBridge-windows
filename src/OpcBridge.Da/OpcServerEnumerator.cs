using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OpcBridge.Da;

public static class OpcServerEnumerator
{
    // OPC DA 2.0 category GUID
    private const string OpcDaCategoryGuid = "{63D5F430-CFE4-11D1-B2C8-0060083BA1FB}";

    /// <summary>
    /// Enumerates locally registered OPC DA servers by scanning the registry.
    /// For remote hosts, falls back to DCOM via IOPCServerList2.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<OpcServerInfo> Enumerate(string? host)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("OPC DA enumeration requires Windows.");

        string? normalizedHost = NormalizeHost(host);

        return normalizedHost is null
            ? EnumerateLocal()
            : EnumerateRemote(normalizedHost);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<OpcServerInfo> EnumerateLocal()
    {
        var results = new List<OpcServerInfo>();

        // Check both 32-bit (WOW6432Node) and 64-bit CLSID hives
        string[] hives = [
            @"SOFTWARE\WOW6432Node\Classes\CLSID",
            @"SOFTWARE\Classes\CLSID"
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string hive in hives)
        {
            using RegistryKey? clsidRoot = Registry.LocalMachine.OpenSubKey(hive);
            if (clsidRoot is null) continue;

            foreach (string clsidStr in clsidRoot.GetSubKeyNames())
            {
                using RegistryKey? clsidKey = clsidRoot.OpenSubKey(clsidStr);
                if (clsidKey is null) continue;

                using RegistryKey? catKey = clsidKey.OpenSubKey(@"Implemented Categories\" + OpcDaCategoryGuid);
                if (catKey is null) continue;

                if (!seen.Add(clsidStr)) continue; // deduplicate between 32/64 hives

                string description = clsidKey.GetValue(null) as string ?? string.Empty;

                string progId = string.Empty;
                using (RegistryKey? progIdKey = clsidKey.OpenSubKey("ProgID"))
                {
                    progId = progIdKey?.GetValue(null) as string ?? string.Empty;
                }

                if (progId.Length == 0) continue;

                results.Add(new OpcServerInfo(progId, description, clsidStr.ToUpperInvariant()));
            }
        }

        results.Sort((a, b) => string.Compare(a.Description.Length > 0 ? a.Description : a.ProgId,
                                               b.Description.Length > 0 ? b.Description : b.ProgId,
                                               StringComparison.OrdinalIgnoreCase));
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<OpcServerInfo> EnumerateRemote(string host)
    {
        // Remote enumeration via OpcEnum COM (IOPCServerList2)
        Type? serverListType = Type.GetTypeFromCLSID(OpcServerListClsid, host, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"OpcEnum is not accessible on host '{host}'. Check DCOM settings and firewall (TCP port 135).");

        object comObject = Activator.CreateInstance(serverListType)
            ?? throw new InvalidOperationException("Failed to create OpcEnum instance.");

        try
        {
            var serverList = (IOPCServerList2)comObject;
            Guid[] categories = [new Guid(OpcDaCategoryGuid)];
            serverList.EnumClassesOfCategories(1, categories, 0, [], out IEnumGuid enumerator);

            var results = new List<OpcServerInfo>();
            Guid[] batch = new Guid[32];

            while (true)
            {
                enumerator.Next(batch.Length, batch, out int fetched);
                if (fetched == 0) break;

                for (int i = 0; i < fetched; i++)
                {
                    Guid clsid = batch[i];
                    try
                    {
                        serverList.GetClassDetails(ref clsid, out string progId, out string description, out _);
                        results.Add(new OpcServerInfo(progId, description, clsid.ToString("B").ToUpperInvariant()));
                    }
                    catch { /* skip */ }
                }
            }

            return results;
        }
        finally
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }

    private static string? NormalizeHost(string? host)
    {
        string trimmed = host?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 ||
            string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return null;
        return trimmed;
    }

    private static readonly Guid OpcServerListClsid = new("13486D44-4821-11D2-A494-3CB306C10000");

    [ComImport]
    [Guid("9DD0B56C-AD9E-43EE-8305-487F3188BF7A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOPCServerList2
    {
        void EnumClassesOfCategories(int catIn,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] catInArray,
            int catNotIn,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] Guid[] catNotInArray,
            out IEnumGuid ppenumClsid);

        void GetClassDetails(ref Guid clsid,
            [MarshalAs(UnmanagedType.LPWStr)] out string progId,
            [MarshalAs(UnmanagedType.LPWStr)] out string description,
            [MarshalAs(UnmanagedType.LPWStr)] out string verIndProgId);

        void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);
    }

    [ComImport]
    [Guid("0002E000-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumGuid
    {
        [PreserveSig] int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] elements, out int fetched);
        [PreserveSig] int Skip(int count);
        [PreserveSig] int Reset();
        void Clone(out IEnumGuid enumGuid);
    }
}

public sealed record OpcServerInfo(string ProgId, string Description, string Clsid);
