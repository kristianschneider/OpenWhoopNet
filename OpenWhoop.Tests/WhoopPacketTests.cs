using OpenWhoop.App;
using OpenWhoop.App.Protocol;

namespace OpenWhoop.Tests;
public class WhoopPacketTests
{
    [Fact]
    public void TestPacketCreation()
    {
        // Arrange
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        // Simulate: PacketType.Command, sequence=1, command=5, payload
        // Your builder does not take sequence/command directly, so we use CommandNumber (e.g. 5)
        var packet = WhoopPacketBuilder.CreateCommandPacket((CommandNumber)5, payload);

        // Act & Assert
        Assert.True(packet.Length > 4); // Should be longer than just header
        Assert.Equal(0xAA, packet[0]);  // SOF (if your protocol uses 0xAA as SOF)
    }

    [Fact]
    public void TestPacketParsing()
    {
        // Arrange
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var originalPacket = WhoopPacketBuilder.CreateCommandPacket((CommandNumber)5, payload);

        // Act
        bool parsedOk = ParsedWhoopPacket.TryParse(originalPacket, out var parsed);

        // Assert
        Assert.True(parsedOk, parsed?.ErrorMessage);
        Assert.Equal(PacketType.Command, parsed.PacketType);
        Assert.Equal((byte)5, parsed.CommandOrEventNumber);
        Assert.Equal(payload, parsed.Payload.ToArray());
    }
}