using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel; // For Permissions, DeviceInfo, MainThread
using System;
using System.Collections.ObjectModel;
using System.IO; // For Path
using System.Threading.Tasks;
using OpenWhoop.Core.Data; // For AppDbContext
using Plugin.BLE.Abstractions.Contracts; // For IDevice
using Microsoft.EntityFrameworkCore;
using OpenWhoop.App; // For DbContextOptionsBuilder and Migrate

namespace OpenWhoop.MauiApp
{
    public partial class MainPage : ContentPage
    {
        private WhoopDeviceScanner _scanner;
        public ObservableCollection<DiscoveredDeviceInfo> Devices => _scanner?.DiscoveredDevices;


        private WhoopDevice _connectedWhoopDevice;
        private AppDbContext _dbContext;

        // Example: Add a property for console-like output in the UI
        private string _consoleOutput = string.Empty;
        public string ConsoleOutput
        {
            get => _consoleOutput;
            set
            {
                _consoleOutput = value;
                OnPropertyChanged(nameof(ConsoleOutput)); // Notify UI of update
            }
        }

        public MainPage()
        {
            InitializeComponent();
            _scanner = new WhoopDeviceScanner();
            SetupDbContext();
            BindingContext = this; // Set BindingContext for ObservableCollection and ConsoleOutput

            // It's good practice to clean up resources when the page is no longer visible
            this.Disappearing += OnMainPageDisappearing;
        }

        private void SetupDbContext()
        {
            string dbFileName = "openwhoop_maui.db";
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, dbFileName);
            LogToConsole($"Database path: {dbPath}");

            var dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            _dbContext = new AppDbContext(dbContextOptions);

            try
            {
                _dbContext.Database.Migrate(); // Applies pending migrations
                LogToConsole("Database migrations applied successfully.");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error applying migrations: {ex.Message}");
                // Consider showing an alert to the user
                // await DisplayAlert("Database Error", $"Could not apply migrations: {ex.Message}", "OK");
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            LogToConsole("Scan button clicked.");
            bool permissionsGranted = await CheckAndRequestBluetoothPermissions();
            if (!permissionsGranted)
            {
                LogToConsole("Bluetooth/Location permissions denied.");
                await DisplayAlert("Permission Denied", "Bluetooth & Location permissions are required to scan for devices.", "OK");
                return;
            }
            LogToConsole("Permissions granted.");

            if (_scanner.DiscoveredDevices.Any())
            {
                _scanner.DiscoveredDevices.Clear();
            }

            // Disable scan button during scan
            if (sender is Button scanButton) scanButton.IsEnabled = false;

            LogToConsole("Starting BLE scan...");
            await _scanner.StartScanAsync(TimeSpan.FromSeconds(10)); // UI should update via ObservableCollection binding
            LogToConsole("Scan finished or timed out.");

            if (sender is Button scanButtonAfterScan) scanButtonAfterScan.IsEnabled = true;

            if (!_scanner.DiscoveredDevices.Any())
            {
                LogToConsole("No devices found.");
            }
        }

        private async void OnDeviceSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is DiscoveredDeviceInfo selectedUiDevice)
            {
                LogToConsole($"Device selected: {selectedUiDevice.Name}");
                await _scanner.StopScanAsync();

                IDevice bleDevice = selectedUiDevice.Device;

                if (_connectedWhoopDevice != null && _connectedWhoopDevice.Id == bleDevice.Id && _connectedWhoopDevice.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                {
                    LogToConsole($"{_connectedWhoopDevice.Name} is already connected.");
                    // You might want to check bond state here too if re-selecting an already connected device
                    if (bleDevice.BondState == Plugin.BLE.Abstractions.DeviceBondState.Bonded)
                    {
                        LogToConsole($"{_connectedWhoopDevice.Name} is also bonded.");
                    }
                    else
                    {
                        LogToConsole($"{_connectedWhoopDevice.Name} is connected but NOT bonded. Consider re-initiating bond if needed.");
                    }
                    await DisplayAlert("Info", $"{_connectedWhoopDevice.Name} is already connected.", "OK");
                    if (sender is ListView listView) listView.SelectedItem = null;
                    return;
                }

                if (_connectedWhoopDevice != null)
                {
                    LogToConsole($"Disposing previous device: {_connectedWhoopDevice.Name}");
                    // Unsubscribe from events
                    _connectedWhoopDevice.DataFromStrapReceived -= OnDataFromStrapReceivedHandler;
                    _connectedWhoopDevice.CmdFromStrapReceived -= OnCmdFromStrapReceivedHandler;
                    _connectedWhoopDevice.EventsFromStrapReceived -= OnEventsFromStrapReceivedHandler;
                    _connectedWhoopDevice.Disconnected -= OnDeviceDisconnectedHandler;
                    _connectedWhoopDevice.Dispose();
                    _connectedWhoopDevice = null;
                }

                LogToConsole($"Creating WhoopDevice for {bleDevice.Name}.");
                _connectedWhoopDevice = new WhoopDevice(bleDevice, _dbContext);
                _connectedWhoopDevice.Disconnected += OnDeviceDisconnectedHandler;
                // Subscribe to data events AFTER successful initialization

                LogToConsole($"Attempting to connect and bond to {bleDevice.Name}...");
                // Use the new ConnectAndBondAsync method
                bool connectedAndBondAttempted = await _connectedWhoopDevice.ConnectAndBondAsync();

                if (connectedAndBondAttempted && _connectedWhoopDevice.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                {
                    LogToConsole($"Successfully connected to {bleDevice.Name}. Current BondState: {bleDevice.BondState}");
                    // Proceed to initialize only if connected
                    LogToConsole($"Initializing {bleDevice.Name}...");

                    // Subscribe to data events before initialization if you expect bonding events during it
                    _connectedWhoopDevice.DataFromStrapReceived += OnDataFromStrapReceivedHandler;
                    _connectedWhoopDevice.CmdFromStrapReceived += OnCmdFromStrapReceivedHandler;
                    _connectedWhoopDevice.EventsFromStrapReceived += OnEventsFromStrapReceivedHandler;

                    bool initialized = await _connectedWhoopDevice.InitializeAsync();
                    if (initialized)
                    {
                        LogToConsole($"{bleDevice.Name} initialized successfully. Ready for commands.");

                        // Now try sending commands like ToggleRealtimeHr or GetBatteryLevel
                        LogToConsole("Sending ToggleRealtimeHr(enable: true) command...");
                        byte[] toggleHrPacket = WhoopPacketBuilder.ToggleRealtimeHr(true);
                        bool sentHrToggle = await _connectedWhoopDevice.SendCommandAsync(toggleHrPacket);
                        LogToConsole(sentHrToggle ? "ToggleRealtimeHr(true) command sent." : "Failed to send ToggleRealtimeHr(true) command.");

                    }
                    else
                    {
                        LogToConsole($"Failed to initialize {bleDevice.Name}.");
                        await DisplayAlert("Initialization Failed", $"Failed to initialize {bleDevice.Name}.", "OK");
                        await _connectedWhoopDevice.DisconnectAsync();
                    }
                }
                else
                {
                    LogToConsole($"Failed to connect and/or bond to {bleDevice.Name}. State: {_connectedWhoopDevice.State}");
                    await DisplayAlert("Connection/Bonding Failed", $"Failed to connect/bond to {bleDevice.Name}.", "OK");
                    _connectedWhoopDevice.Dispose();
                    _connectedWhoopDevice = null;
                }
            }
            if (sender is ListView listViewAfterSelection) listViewAfterSelection.SelectedItem = null;
        }


        private void OnEventsFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[EVENTS_FROM_STRAP]: {BitConverter.ToString(data)}");

            if (data == null || data.Length < 1)
            {
                LogToConsole("Received empty or invalid event data.");
                return;
            }

            // The first byte of the raw data from the characteristic is NOT the Whoop PacketType.
            // The WhoopPacket structure (Type, Seq, Len, Payload, CRC) is what's inside 'data'
            // if the characteristic sends full Whoop packets.
            // Let's assume 'data' IS a full Whoop packet.

            if (data.Length < 4) // Minimum for Type, Seq, Len, EventNumber (as first byte of payload)
            {
                LogToConsole("Event data too short to be a valid Whoop event packet.");
                return;
            }

            PacketType packetType = (PacketType)data[0];
            // byte sequence = data[1];
            byte payloadLength = data[2]; // This is the length of (EventNumber + EventData)

            if (packetType == PacketType.Event)
            {
                // The actual event payload starts after Type, Sequence, Length
                // So, EventNumber is at data[3]
                if (payloadLength < 1) // Must have at least EventNumber
                {
                    LogToConsole("Event packet payload is too short.");
                    return;
                }

                EventNumber eventNumber = (EventNumber)data[3];
                LogToConsole($"Parsed Event: {eventNumber}");

                if (eventNumber == EventNumber.BatteryLevel)
                {
                    // For BatteryLevel event, the payload (after EventNumber) typically contains the battery percentage.
                    // Assuming the battery level is 1 byte following the EventNumber byte.
                    // So, battery level is at data[4]
                    if (payloadLength >= 2 && data.Length >= 5) // Check if there's data for battery level
                    {
                        byte batteryLevel = data[4];
                        LogToConsole($"Battery Level Event: {batteryLevel}%");
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await DisplayAlert("Battery Level", $"Strap Battery: {batteryLevel}%", "OK");
                        });
                    }
                    else
                    {
                        LogToConsole("BatteryLevel event packet is too short for battery data.");
                    }
                }
                // Add more else if blocks here to handle other EventNumbers
                // else if (eventNumber == EventNumber.SomeOtherEvent) { ... }
            }
            else
            {
                LogToConsole($"Received packet type {packetType}, not an Event as expected for battery level via this handler.");
            }
        }




        private async Task GetData(IDevice bleDevice)
        {
            LogToConsole($"{bleDevice.Name} initialized successfully. Ready for commands.");

            LogToConsole("Sending ToggleRealtimeHr(enable: true) command...");
            byte[] toggleHrPacket = WhoopPacketBuilder.ToggleRealtimeHr(true);
            bool sentHrToggle = await _connectedWhoopDevice.SendCommandAsync(toggleHrPacket);
            LogToConsole(sentHrToggle ? "ToggleRealtimeHr(true) command sent." : "Failed to send ToggleRealtimeHr(true) command.");


            // Example: Send a "Get Battery Level" command
            LogToConsole("Sending GetBatteryLevel command...");
            byte[] getBatteryPacket = WhoopPacketBuilder.GetBatteryLevel();
            bool sent = await _connectedWhoopDevice.SendCommandAsync(getBatteryPacket);
            LogToConsole(sent ? "GetBatteryLevel command sent." : "Failed to send GetBatteryLevel command.");
        }

        private void OnDataFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[DATA_FROM_STRAP]: {BitConverter.ToString(data)}");
            // Process general data, historical data packets
        }

        private void OnCmdFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[CMD_FROM_STRAP]: {BitConverter.ToString(data)}");
            if (data == null || data.Length < 4) // Min for Type, Seq, Len, OriginalCommandNumber
            {
                LogToConsole("CommandResponse data too short.");
                return;
            }

            PacketType packetType = (PacketType)data[0];
            // byte sequence = data[1];
            byte payloadLength = data[2]; // Length of (OriginalCommandNumber + ResponsePayload)

            if (packetType == PacketType.CommandResponse)
            {
                if (payloadLength < 1)
                {
                    LogToConsole("CommandResponse payload too short.");
                    return;
                }

                CommandNumber originalCommand = (CommandNumber)data[3]; // Original command this is a response to
                LogToConsole($"Parsed CommandResponse for: {originalCommand}");

                if (originalCommand == CommandNumber.GetBatteryLevel)
                {
                    // If GetBatteryLevel returns its value in a CommandResponse packet:
                    // The battery level data would be at data[4] onwards.
                    // Assuming 1 byte for battery level:
                    if (payloadLength >= 2 && data.Length >= 5)
                    {
                        byte batteryLevel = data[4];
                        LogToConsole($"Battery Level (from CommandResponse): {batteryLevel}%");
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await DisplayAlert("Battery Level", $"Strap Battery (CMD_RESP): {batteryLevel}%", "OK");
                        });
                    }
                    else
                    {
                        LogToConsole("BatteryLevel CommandResponse packet is too short for battery data.");
                    }
                }
                // Handle other command responses
            }
        }


        private void OnDeviceDisconnectedHandler(object sender, EventArgs e)
        {
            if (sender is WhoopDevice disconnectedDevice)
            {
                LogToConsole($"Device {disconnectedDevice.Name} has disconnected unexpectedly.");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Disconnected", $"Device {disconnectedDevice.Name} has disconnected.", "OK");
                    if (_connectedWhoopDevice?.Id == disconnectedDevice.Id)
                    {
                        _connectedWhoopDevice.Disconnected -= OnDeviceDisconnectedHandler; // Unsubscribe to prevent issues if re-instantiated
                        _connectedWhoopDevice.DataFromStrapReceived -= OnDataFromStrapReceivedHandler;
                        _connectedWhoopDevice.CmdFromStrapReceived -= OnCmdFromStrapReceivedHandler;
                        _connectedWhoopDevice.EventsFromStrapReceived -= OnEventsFromStrapReceivedHandler;
                        _connectedWhoopDevice.Dispose();
                        _connectedWhoopDevice = null;
                        // TODO: Update UI to reflect disconnection (e.g., disable command buttons)
                        LogToConsole("UI updated to reflect disconnection.");
                    }
                });
            }
        }

        public async Task<bool> CheckAndRequestBluetoothPermissions()
        {
            PermissionStatus overallStatus = PermissionStatus.Unknown;

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // For Android 12 (API 31) and above, Permissions.Bluetooth handles SCAN and CONNECT
                if (DeviceInfo.Version.Major >= 12)
                {
                    var bluetoothPermissions = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                    if (bluetoothPermissions != PermissionStatus.Granted)
                    {
                        bluetoothPermissions = await Permissions.RequestAsync<Permissions.Bluetooth>();
                    }
                    overallStatus = bluetoothPermissions;
                }
                else // For Android versions older than 12
                {
                    // Location is typically required for BLE scanning
                    var locationPermission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                    if (locationPermission != PermissionStatus.Granted)
                    {
                        locationPermission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    }
                    // And Bluetooth Admin/Bluetooth permissions (often install-time on older versions)
                    // Plugin.BLE relies on these being available.
                    // If location is granted, BLE scanning usually works.
                    overallStatus = locationPermission;
                }
                return overallStatus == PermissionStatus.Granted;
            }
            // For other platforms like iOS, Windows, permissions are handled differently
            // or might not need explicit runtime requests in the same way for basic BLE.
            // Plugin.BLE documentation might have platform-specific notes.
            // For now, assume granted or handled by OS for non-Android.
            return true;
        }

        private void LogToConsole(string message)
        {
            // Update the UI-bound property on the main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ConsoleOutput += $"{DateTime.Now:HH:mm:ss}: {message}{Environment.NewLine}";
                System.Diagnostics.Debug.WriteLine(message); // Also output to debug console
            });
        }

        private async void OnMainPageDisappearing(object sender, EventArgs e)
        {
            LogToConsole("MainPage disappearing. Cleaning up resources.");
            if (_scanner != null)
            {
                await _scanner.StopScanAsync(); // Ensure scanning is stopped
                _scanner.Dispose(); // Dispose scanner resources
                _scanner = null;
            }
            if (_connectedWhoopDevice != null)
            {
                _connectedWhoopDevice.Disconnected -= OnDeviceDisconnectedHandler;
                _connectedWhoopDevice.DataFromStrapReceived -= OnDataFromStrapReceivedHandler;
                _connectedWhoopDevice.CmdFromStrapReceived -= OnCmdFromStrapReceivedHandler;
                _connectedWhoopDevice.EventsFromStrapReceived -= OnEventsFromStrapReceivedHandler;
                await _connectedWhoopDevice.DisconnectAsync(); // Attempt graceful disconnect
                _connectedWhoopDevice.Dispose(); // Dispose device resources
                _connectedWhoopDevice = null;
            }
            if (_dbContext != null)
            {
                await _dbContext.DisposeAsync(); // Dispose DbContext
                _dbContext = null;
            }
        }
    }

}