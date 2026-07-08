using OpcBridge.Da;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DaBrowseMetadataTests
{
    [Fact]
    public void OpcTagNode_CarriesNativeMetadata()
    {
        var node = new OpcTagNode(
            Name: "TagA",
            ItemId: "Channel.Device.TagA",
            CanonicalDataType: 5,
            AccessRights: 3);

        Assert.Equal((short)5, node.CanonicalDataType);
        Assert.Equal(3, node.AccessRights);
    }
}
