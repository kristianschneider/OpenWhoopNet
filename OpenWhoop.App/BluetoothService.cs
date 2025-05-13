using OpenWhoop.App;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
public class BluetoothService : IDisposable // Added IDisposable
{

    private readonly IAdapter _bluetoothAdapter;
    public ObservableCollection<DiscoveredDeviceInfo> DiscoveredDevices { get; }
    private CancellationTokenSource? _scanCancellationTokenSource; // Made nullable

    public bool IsScanning => _bluetoothAdapter.IsScanning;
    public IAdapter Adapter => _bluetoothAdapter;

    public BluetoothService()
    {
        _bluetoothAdapter = CrossBluetoothLE.Current.Adapter;
        DiscoveredDevices = new ObservableCollection<DiscoveredDeviceInfo>();

        _bluetoothAdapter.DeviceDiscovered += OnDeviceDiscovered;
        _bluetoothAdapter.ScanTimeoutElapsed += OnScanTimeoutElapsed;
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs args) // sender nullable
    {
        var device = args.Device;
        // Check if the device advertises the Whoop service or has a relevant name
        // Plugin.BLE's IDevice.AdvertisementRecords can be inspected.
        // For simplicity, we might filter later or by name if reliable.
        // The Rust code filters by service UUID during the scan.
        // Plugin.BLE allows ScanFilter, but it's not universally implemented.
        // A common approach is to check services after connecting or by name.

        if (device != null &&
            !DiscoveredDevices.Any(d => d.Id == device.Id) &&
            (!string.IsNullOrEmpty(device.Name) && device.Name.ToLowerInvariant().Contains("whoop"))) // Basic name filter
        {
            // To confirm it's a Whoop device by service, you'd typically connect and then check services.
            // For scanning display, we might add it and let user choose, or do a quick connect-check-disconnect.
            // For now, let's add based on name and allow further checks upon selection.
            DiscoveredDevices.Add(new DiscoveredDeviceInfo(device));
            Console.WriteLine($"Discovered: {device.Name} (ID: {device.Id}, RSSI: {device.Rssi})");
        }
    }

    private void OnScanTimeoutElapsed(object? sender, EventArgs e) // sender nullable
    {
        Console.WriteLine("Scan timeout elapsed.");
        CleanupScan();
    }

    public async Task StartScanAsync(TimeSpan scanDuration)
    {
        if (_bluetoothAdapter.IsScanning)
        {
            Console.WriteLine("Already scanning.");
            return;
        }

        // Check Bluetooth state
        if (CrossBluetoothLE.Current.State == BluetoothState.Off)
        {
            Console.WriteLine("Bluetooth is off. Please turn it on.");
            // In a MAUI app, you might prompt the user to turn it on.
            return;
        }

        // Permissions should be handled by the MAUI app.

        Console.WriteLine($"Starting scan for Whoop devices for {scanDuration.TotalSeconds} seconds...");
        DiscoveredDevices.Clear();
        _scanCancellationTokenSource = new CancellationTokenSource(); // Initialized here

        try
        {
            // Scan for devices that advertise the Whoop service UUID if possible,
            // or filter by name as a fallback.
            // The Plugin.BLE library might require filtering after discovery if direct UUID filtering isn't supported on all platforms during scan.
            await _bluetoothAdapter.StartScanningForDevicesAsync(
                // serviceUuids: new[] { WhoopConstants.PrimaryServiceUuid }, // This would be ideal if WhoopConstants.PrimaryServiceUuid is defined and filtering works
                deviceFilter: null, // No specific filter, will check name in OnDeviceDiscovered
                allowDuplicatesKey: false, // Don't raise event for same device multiple times
                cancellationToken: _scanCancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting scan: {ex.Message}");
            CleanupScan();
        }
    }

    public Task StopScanAsync() // Changed from async Task to Task
    {
        if (IsScanning)
        {
            Console.WriteLine("Stopping scan...");
            if (_scanCancellationTokenSource != null && !_scanCancellationTokenSource.IsCancellationRequested)
            {
                _scanCancellationTokenSource.Cancel();
            }
            Console.WriteLine("Scan stop requested via token.");
        }
        CleanupScan();
        return Task.CompletedTask; // Return a completed task
    }

    private void CleanupScan()
    {
        _scanCancellationTokenSource?.Dispose();
        _scanCancellationTokenSource = null;
    }

   
    public void Dispose()
    {
        _bluetoothAdapter.DeviceDiscovered -= OnDeviceDiscovered;
        _bluetoothAdapter.ScanTimeoutElapsed -= OnScanTimeoutElapsed;
        CleanupScan();
        GC.SuppressFinalize(this); // Added for IDisposable pattern
    }

    public async Task<IDevice> ConnectToKnownDeviceAsync(Guid id)
    {
        return await _bluetoothAdapter.ConnectToKnownDeviceAsync(id);
    }
}
