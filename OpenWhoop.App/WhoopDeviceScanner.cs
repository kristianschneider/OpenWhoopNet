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
public class WhoopDeviceScanner
{

    private readonly IAdapter _bluetoothAdapter;
    public ObservableCollection<DiscoveredDeviceInfo> DiscoveredDevices { get; }
    private CancellationTokenSource _scanCancellationTokenSource;

    public WhoopDeviceScanner()
    {
        _bluetoothAdapter = CrossBluetoothLE.Current.Adapter;
        DiscoveredDevices = new ObservableCollection<DiscoveredDeviceInfo>();

        _bluetoothAdapter.DeviceDiscovered += OnDeviceDiscovered;
        _bluetoothAdapter.ScanTimeoutElapsed += OnScanTimeoutElapsed;
        // Consider handling Adapter.DeviceConnectionLost, Adapter.DeviceDisconnected
    }

    private void OnDeviceDiscovered(object sender, DeviceEventArgs args)
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

    private void OnScanTimeoutElapsed(object sender, EventArgs e)
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
        _scanCancellationTokenSource = new CancellationTokenSource();

        try
        {
            // ScanFilter for specific service UUIDs
            var scanFilterOptions = new ScanFilterOptions();
            // Note: ServiceData in ScanFilterOptions is for filtering by advertised service data,
            // not just the presence of a service UUID in the advertisement packet's service list.
            // To filter by advertised service UUIDs, you often check after discovery or rely on name.
            // Some platforms/libraries might support direct filtering on advertised Service UUIDs.
            // Plugin.BLE's IAdapter.StartScanningForDevicesAsync has an optional serviceUuids parameter.

            _bluetoothAdapter.ScanTimeout = (int)scanDuration.TotalMilliseconds;
            await _bluetoothAdapter.StartScanningForDevicesAsync();
            //await _bluetoothAdapter.StartScanningForDevicesAsync(
            //    serviceUuids: new Guid[] { WhoopConstants.WhoopServiceGuid }, // Attempt to filter by Whoop service
            //    deviceFilter: (device) => !string.IsNullOrEmpty(device.Name) && device.Name.ToLowerInvariant().Contains("whoop"), // Example device filter
            //    allowDuplicatesKey: false,
            //    cancellationToken: _scanCancellationTokenSource.Token);

            Console.WriteLine("Scan started. Listening for devices...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting scan: {ex.Message}");
            CleanupScan();
        }
    }

    public async Task StopScanAsync()
    {
        if (_bluetoothAdapter.IsScanning)
        {
            Console.WriteLine("Stopping scan...");
            _scanCancellationTokenSource?.Cancel();
            await _bluetoothAdapter.StopScanningForDevicesAsync();
            Console.WriteLine("Scan stopped.");
        }
        CleanupScan();
    }

    private void CleanupScan()
    {
        _scanCancellationTokenSource?.Dispose();
        _scanCancellationTokenSource = null;
    }

    /// <summary>
    /// Finds a specific Whoop peripheral by its ID (Guid) or name.
    /// Note: Plugin.BLE uses Guid for device identification. MAC addresses are not directly exposed cross-platform.
    /// If 'whoopIdentifier' is a MAC address, this method needs adjustment or a different lookup strategy.
    /// For now, assumes 'whoopIdentifier' could be a name or a Guid string.
    /// </summary>
    public async Task<IDevice> FindWhoopPeripheralAsync(string whoopIdentifier, TimeSpan scanTimeout)
    {
        Console.WriteLine($"Attempting to find Whoop device: {whoopIdentifier} for {scanTimeout.TotalSeconds} seconds...");

        if (CrossBluetoothLE.Current.State == BluetoothState.Off)
        {
            Console.WriteLine("Bluetooth is off. Cannot find device.");
            return null;
        }

        IDevice foundDevice = null;
        _scanCancellationTokenSource = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<IDevice>();

        EventHandler<DeviceEventArgs> deviceDiscoveredHandler = async (s, e) =>
        {
            var device = e.Device;
            bool match = false;
            if (Guid.TryParse(whoopIdentifier, out Guid deviceId))
            {
                if (device.Id == deviceId) match = true;
            }
            else if (!string.IsNullOrEmpty(device.Name) && device.Name.Equals(whoopIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                match = true;
            }
            // Add MAC address comparison if you have a way to get it via Plugin.BLE (platform-specific or via advertisement data)

            if (match)
            {
                // Optional: verify Whoop service before returning
                try
                {
                    Console.WriteLine($"Potential match: {device.Name}. Connecting to verify service...");
                    var connectParameters = new ConnectParameters(autoConnect: false, forceBleTransport: true);
                    await _bluetoothAdapter.ConnectToDeviceAsync(device, connectParameters, _scanCancellationTokenSource.Token);
                    var services = await device.GetServicesAsync(_scanCancellationTokenSource.Token);
                    if (services.Any(serv => serv.Id == WhoopConstants.WhoopServiceGuid))
                    {
                        Console.WriteLine($"Whoop service confirmed for {device.Name}.");
                        tcs.TrySetResult(device); // Device remains connected
                    }
                    else
                    {
                        Console.WriteLine($"Whoop service NOT confirmed for {device.Name}.");
                        await _bluetoothAdapter.DisconnectDeviceAsync(device); // Disconnect if not the one
                    }
                }
                catch (DeviceConnectionException dex)
                {
                    Console.WriteLine($"Connection error verifying {device.Name}: {dex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error verifying {device.Name}: {ex.Message}");
                }
            }
        };

        _bluetoothAdapter.DeviceDiscovered += deviceDiscoveredHandler;

        try
        {
            await _bluetoothAdapter.StartScanningForDevicesAsync(
                serviceUuids: new Guid[] { WhoopConstants.WhoopServiceGuid }, // Filter for Whoop service
                cancellationToken: _scanCancellationTokenSource.Token);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(scanTimeout, _scanCancellationTokenSource.Token));

            if (completedTask == tcs.Task)
            {
                foundDevice = tcs.Task.Result; // This device is connected
            }
            else
            {
                Console.WriteLine("Scan timed out or cancelled while searching for specific device.");
                tcs.TrySetResult(null); // Ensure TCS completes
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Scan for specific device was cancelled.");
            tcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during specific device scan: {ex.Message}");
            tcs.TrySetResult(null);
        }
        finally
        {
            if (_bluetoothAdapter.IsScanning)
            {
                await _bluetoothAdapter.StopScanningForDevicesAsync();
            }
            _bluetoothAdapter.DeviceDiscovered -= deviceDiscoveredHandler;
            _scanCancellationTokenSource?.Dispose();
            _scanCancellationTokenSource = null;
        }

        if (foundDevice != null)
        {
            Console.WriteLine($"Successfully found and connected to Whoop device: {foundDevice.Name} (ID: {foundDevice.Id})");
        }
        else
        {
            Console.WriteLine($"Could not find Whoop device '{whoopIdentifier}' or confirm service within the timeout.");
        }
        return foundDevice;
    }

    // Call this method when your app is closing or the scanner is no longer needed
    public void Dispose()
    {
        _bluetoothAdapter.DeviceDiscovered -= OnDeviceDiscovered;
        _bluetoothAdapter.ScanTimeoutElapsed -= OnScanTimeoutElapsed;
        StopScanAsync().Wait(); // Ensure scan is stopped
    }
}
