using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenWhoop.App;
public class DiscoveredDeviceInfo
{
    public string Name { get; }
    public Guid Id { get; } // Plugin.BLE uses Guid for device ID
    public int Rssi { get; }
    public IDevice Device { get; } // Keep a reference to the IDevice object

    // Constructor that takes an IDevice
    public DiscoveredDeviceInfo(IDevice device)
    {
        Device = device;
        Name = device.Name;
        Id = device.Id;
        Rssi = device.Rssi;
    }

    public override string ToString()
    {
        // The IDevice.Id is a Guid, not a MAC address directly in string form.
        // MAC address might be part of the Name or advertised data on some platforms,
        // but Plugin.BLE abstracts this.
        return $"Name: {Name ?? "N/A"}, ID: {Id}, RSSI: {Rssi}";
    }
}
