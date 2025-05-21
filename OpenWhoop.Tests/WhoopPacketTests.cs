using OpenWhoop.App;
using OpenWhoop.App.Protocol;

namespace OpenWhoop.Tests;
public class WhoopPacketTests
{
    [Fact]
    public void TestParseMetadata()
    {
        // Test case 1: HistoryEnd metadata packet
        var historyEndBytes = StringToByteArray("aa1c00ab311002a9fc8367205337000000257e00000a0000000000007ac020f8");
        bool parsedEndOk = ParsedWhoopPacket.TryParse(historyEndBytes, out var parsedEnd);

        // Assert packet parsing success
        Assert.True(parsedEndOk, parsedEnd?.ErrorMessage);
        Assert.Equal(PacketType.Metadata, parsedEnd.PacketType);

        // Extract metadata fields: 
        //   unix timestamp (4 bytes), 
        //   skip padding (6 bytes), 
        //   data (4 bytes)
        //   metadata type is in CommandOrEventNumber
        int offset = 0;
        uint endUnixTime = BitConverter.ToUInt32(parsedEnd.Payload, offset);
        offset += 4;
        // Skip 6 bytes padding
        offset += 6;
        uint endData = BitConverter.ToUInt32(parsedEnd.Payload, offset);

        // Assert the extracted values match expected values
        Assert.Equal((byte)MetadataType.HistoryEnd, parsedEnd.CommandOrEventNumber);
        Assert.Equal(1736703145U, endUnixTime);
        Assert.Equal(32293U, endData);

        // Test case 2: HistoryStart metadata packet
        var historyStartBytes = StringToByteArray("aa2c005231010146fb8367404c0600000010000000020000002900000010000000030000000000000008020055fd251d");
        bool parsedStartOk = ParsedWhoopPacket.TryParse(historyStartBytes, out var parsedStart);

        // Assert packet parsing success
        Assert.True(parsedStartOk, parsedStart?.ErrorMessage);
        Assert.Equal(PacketType.Metadata, parsedStart.PacketType);

        // Extract metadata fields again
        offset = 0;
        uint startUnixTime = BitConverter.ToUInt32(parsedStart.Payload, offset);
        offset += 4;
        // Skip 6 bytes padding
        offset += 6;
        uint startData = BitConverter.ToUInt32(parsedStart.Payload, offset);

        // Assert the extracted values match expected values
        Assert.Equal((byte)MetadataType.HistoryStart, parsedStart.CommandOrEventNumber);
        Assert.Equal(1736702790U, startUnixTime);
        Assert.Equal(16U, startData);
    }

    // Helper method to convert hex string to byte array
    private static byte[] StringToByteArray(string hex)
    {
        int NumberChars = hex.Length;
        byte[] bytes = new byte[NumberChars / 2];
        for (int i = 0; i < NumberChars; i += 2)
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        return bytes;
    }

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