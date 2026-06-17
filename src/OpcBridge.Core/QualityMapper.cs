namespace OpcBridge.Core;

public static class QualityMapper
{
    private const int DaQualityMask = 0xC0;
    private const int DaQualityGood = 0xC0;

    public static bool IsGoodDaQuality(int quality)
    {
        return (quality & DaQualityMask) == DaQualityGood;
    }
}
