using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpcBridge.Core;

namespace OpcBridge.Da;

/// <summary>
/// COM-visible sink that receives <c>IOPCDataCallback</c> notifications from an OPC DA
/// group subscription. Converts the marshalled arrays into <see cref="BridgeValue"/>s
/// and forwards them via the constructor-supplied callback.
/// </summary>
[SupportedOSPlatform("windows")]
[ComVisible(true)]
[Guid("2F7C9A3E-4B1D-4E2A-9F08-B3D4A1E7C205")]
internal sealed class OpcDaCallbackSink : OpcDaClient.IOPCDataCallback
{
    private readonly string sourceId_;
    private readonly IReadOnlyDictionary<int, string> clientHandleToItemId_;
    private readonly Action<IReadOnlyList<BridgeValue>> onValues_;

    /// <param name="sourceId">DA source identifier to stamp onto emitted values.</param>
    /// <param name="clientHandleToItemId">Maps OPC DA client handles (1-based index) to item IDs.</param>
    /// <param name="onValues">Receives unpacked values; invoked on the COM apartment thread.</param>
    public OpcDaCallbackSink(
        string sourceId,
        IReadOnlyDictionary<int, string> clientHandleToItemId,
        Action<IReadOnlyList<BridgeValue>> onValues)
    {
        sourceId_ = sourceId;
        clientHandleToItemId_ = clientHandleToItemId;
        onValues_ = onValues;
    }

    public int OnDataChange(
        int dwTransid,
        int hGroup,
        int hrMasterquality,
        int hrQuality,
        int dwCount,
        IntPtr phClientItems,
        IntPtr pvValues,
        IntPtr pwQualities,
        IntPtr pftTimeStamps,
        IntPtr pErrors)
    {
        return UnpackAndForward(dwCount, phClientItems, pvValues, pwQualities, pftTimeStamps, pErrors);
    }

    public int OnReadComplete(
        int dwTransid,
        int hGroup,
        int hrMasterquality,
        int hrQuality,
        int dwCount,
        IntPtr phClientItems,
        IntPtr pvValues,
        IntPtr pwQualities,
        IntPtr pftTimeStamps,
        IntPtr pErrors)
    {
        return UnpackAndForward(dwCount, phClientItems, pvValues, pwQualities, pftTimeStamps, pErrors);
    }

    public int OnWriteComplete(
        int dwTransid,
        int hGroup,
        int hrMasterquality,
        int hrQuality,
        int dwCount,
        IntPtr phClientItems,
        IntPtr pvValues,
        IntPtr pwQualities,
        IntPtr pftTimeStamps,
        IntPtr pErrors)
    {
        return 0; // S_OK — write acknowledgements are not forwarded.
    }

    public int OnCancelComplete(int dwTransid, int hGroup)
    {
        return 0; // S_OK
    }

    private int UnpackAndForward(
        int count,
        IntPtr phClientItems,
        IntPtr pvValues,
        IntPtr pwQualities,
        IntPtr pftTimeStamps,
        IntPtr pErrors)
    {
        if (count <= 0)
        {
            return 0;
        }

        int[] clientHandles = new int[count];
        Marshal.Copy(phClientItems, clientHandles, 0, count);

        short[] qualities = new short[count];
        Marshal.Copy(pwQualities, qualities, 0, count);

        long[] timestamps = new long[count];
        Marshal.Copy(pftTimeStamps, timestamps, 0, count);

        int[] errors = new int[count];
        Marshal.Copy(pErrors, errors, 0, count);

        List<BridgeValue> values = new(count);
        for (int i = 0; i < count; i++)
        {
            if (!clientHandleToItemId_.TryGetValue(clientHandles[i], out string? itemId))
            {
                continue;
            }

            int error = errors[i];
            if (error < 0)
            {
                values.Add(new BridgeValue(sourceId_, itemId, null, DateTime.UtcNow, 0, false));
                continue;
            }

            IntPtr variantPtr = IntPtr.Add(pvValues, i * 16); // VARIANT is 16 bytes on x86/x64
            object? value = Marshal.GetObjectForNativeVariant(variantPtr);

            int quality = (ushort)qualities[i];
            DateTime timestamp = timestamps[i] <= 0
                ? DateTime.UtcNow
                : DateTime.FromFileTimeUtc(timestamps[i]);

            values.Add(new BridgeValue(sourceId_, itemId, value, timestamp, quality, QualityMapper.IsGoodDaQuality(quality)));
        }

        if (values.Count > 0)
        {
            onValues_(values);
        }

        return 0; // S_OK
    }
}
