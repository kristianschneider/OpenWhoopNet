using System;

namespace OpenWhoop.App;
public static class WhoopConstants
{
    // Service UUID from Rust: pub const WHOOP_SERVICE: Uuid = uuid!("61080001-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid WhoopServiceGuid = Guid.Parse("61080001-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Characteristic for sending commands to the strap (Client to Server)
    // Rust: pub const CMD_TO_STRAP: Uuid = uuid!("61080002-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid CmdToStrapCharacteristicGuid = Guid.Parse("61080002-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Characteristic for receiving general data from the strap (Server to Client - Notifications)
    // Rust: pub const DATA_FROM_STRAP: Uuid = uuid!("61080005-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid DataFromStrapCharacteristicGuid = Guid.Parse("61080005-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Characteristic for receiving command-related responses or specific commands from the strap
    // Rust: pub const CMD_FROM_STRAP: Uuid = uuid!("61080003-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid CmdFromStrapCharacteristicGuid = Guid.Parse("61080003-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Characteristic for receiving events from the strap
    // Rust: pub const EVENTS_FROM_STRAP: Uuid = uuid!("61080004-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid EventsFromStrapCharacteristicGuid = Guid.Parse("61080004-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Memfault characteristic (if needed)
    // Rust: pub const MEMFAULT: Uuid = uuid!("61080007-8d6d-82b8-614a-1c8cb0f8dcc6");
    public static readonly Guid MemfaultCharacteristicGuid = Guid.Parse("61080007-8d6d-82b8-614a-1c8cb0f8dcc6");

    // Note: The previous names like WhoopRxCharacteristicGuid, WhoopTxCharacteristicGuid, WhoopSensorCharacteristicGuid
    // should be mentally mapped or updated in WhoopDevice.cs to use these more specific GUIDs and potentially more descriptive names.
    // For example:
    // - WhoopRxCharacteristicGuid maps to CmdToStrapCharacteristicGuid.
    // - WhoopTxCharacteristicGuid could map to DataFromStrapCharacteristicGuid or CmdFromStrapCharacteristicGuid depending on usage.
    // - WhoopSensorCharacteristicGuid could map to EventsFromStrapCharacteristicGuid or another specific data characteristic.
}
