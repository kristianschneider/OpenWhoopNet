using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using OpenWhoop.Core.Data;
using Plugin.BLE.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenWhoop.App; // For WhoopDevice, WhoopPacketBuilder, Enums
using System.Linq;

namespace OpenWhoop.MauiApp
{
    public partial class MainPage : ContentPage
    {
        private WhoopDeviceScanner _scanner;
        public ObservableCollection<DiscoveredDeviceInfo> Devices => _scanner?.DiscoveredDevices;

        private WhoopDevice _connectedWhoopDevice;
        private AppDbContext _dbContext;

        private string _consoleOutput = string.Empty;
        public string ConsoleOutput
        {
            get => _consoleOutput;
            set
            {
                if (_consoleOutput != value)
                {
                    _consoleOutput = value;
                    OnPropertyChanged(nameof(ConsoleOutput));
                }
            }
        }

        public MainPage()
        {
            InitializeComponent();
            _scanner = new WhoopDeviceScanner();
            SetupDbContext();
            BindingContext = this;
            this.Disappearing += OnMainPageDisappearing;
            StatusLabel.Text = "Status: Ready. Scan for devices.";
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
                _dbContext.Database.Migrate();
                LogToConsole("Database migrations applied successfully.");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error applying migrations: {ex.Message}");
                DisplayAlert("Database Error", $"Could not apply migrations: {ex.Message}", "OK");
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            LogToConsole("Scan button clicked.");
            StatusLabel.Text = "Status: Scanning...";

            bool permissionsGranted = await CheckAndRequestBluetoothPermissions();
            if (!permissionsGranted)
            {
                LogToConsole("Bluetooth/Location permissions denied.");
                StatusLabel.Text = "Status: Permissions denied.";
                await DisplayAlert("Permission Denied", "Bluetooth & Location permissions are required to scan for devices.", "OK");
                return;
            }
            LogToConsole("Permissions granted.");

            if (_scanner.DiscoveredDevices.Any()) _scanner.DiscoveredDevices.Clear();
            if (sender is Button scanButton) scanButton.IsEnabled = false;

            LogToConsole("Starting BLE scan...");
            await _scanner.StartScanAsync(TimeSpan.FromSeconds(10));
            LogToConsole("Scan finished or timed out.");
            StatusLabel.Text = _scanner.DiscoveredDevices.Any() ? "Status: Scan complete. Select device." : "Status: No devices found.";

            if (sender is Button scanButtonAfterScan) scanButtonAfterScan.IsEnabled = true;
            if (!_scanner.DiscoveredDevices.Any()) LogToConsole("No devices found.");
        }

        private async void OnDeviceSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is DiscoveredDeviceInfo selectedUiDevice)
            {
                LogToConsole($"Device selected: {selectedUiDevice.Name}");
                StatusLabel.Text = $"Status: Connecting to {selectedUiDevice.Name}...";
                await _scanner.StopScanAsync();
                IDevice bleDevice = selectedUiDevice.Device;

                if (_connectedWhoopDevice != null && _connectedWhoopDevice.Id == bleDevice.Id && _connectedWhoopDevice.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                {
                    LogToConsole($"{_connectedWhoopDevice.Name} is already connected. BondState: {bleDevice.BondState}");
                    StatusLabel.Text = $"Status: Already connected to {_connectedWhoopDevice.Name} (Bonded: {bleDevice.BondState == Plugin.BLE.Abstractions.DeviceBondState.Bonded})";
                    if (sender is ListView listView) listView.SelectedItem = null;
                    return;
                }

                if (_connectedWhoopDevice != null) await DisconnectCurrentDevice();

                _connectedWhoopDevice = new WhoopDevice(bleDevice, _dbContext);
                _connectedWhoopDevice.Disconnected += OnDeviceDisconnectedHandler;

                LogToConsole($"Attempting to connect and bond to {bleDevice.Name}...");
                bool connectedAndBondAttempted = await _connectedWhoopDevice.ConnectAndBondAsync();

                if (connectedAndBondAttempted && _connectedWhoopDevice.State == Plugin.BLE.Abstractions.DeviceState.Connected)
                {
                    LogToConsole($"Successfully connected to {bleDevice.Name}. Current BondState: {_connectedWhoopDevice.BondState}");
                    StatusLabel.Text = $"Status: Connected to {bleDevice.Name} (Bonded: {_connectedWhoopDevice.BondState}). Initializing...";

                    _connectedWhoopDevice.DataFromStrapReceived += OnDataFromStrapReceivedHandler;
                    _connectedWhoopDevice.CmdFromStrapReceived += OnCmdFromStrapReceivedHandler;
                    _connectedWhoopDevice.EventsFromStrapReceived += OnEventsFromStrapReceivedHandler;

                    bool initialized = await _connectedWhoopDevice.InitializeAsync();
                    if (initialized)
                    {
                        LogToConsole($"{bleDevice.Name} initialized successfully. Ready for commands.");
                        StatusLabel.Text = $"Status: {bleDevice.Name} initialized (Bonded: {_connectedWhoopDevice.BondState}). Sending test commands...";
                        await SendTestCommands();
                    }
                    else
                    {
                        LogToConsole($"Failed to initialize {bleDevice.Name}.");
                        StatusLabel.Text = $"Status: Failed to initialize {bleDevice.Name}.";
                        await DisplayAlert("Initialization Failed", $"Failed to initialize {bleDevice.Name}.", "OK");
                        await DisconnectCurrentDevice();
                    }
                }
                else
                {
                    LogToConsole($"Failed to connect and/or bond to {bleDevice.Name}. State: {_connectedWhoopDevice?.State}");
                    StatusLabel.Text = $"Status: Failed to connect/bond to {bleDevice.Name}.";
                    await DisplayAlert("Connection/Bonding Failed", $"Failed to connect/bond to {bleDevice.Name}.", "OK");
                    await DisconnectCurrentDevice();
                }
            }
            if (sender is ListView listViewAfterSelection) listViewAfterSelection.SelectedItem = null;
        }

        private async Task SendTestCommands()
        {
            if (_connectedWhoopDevice == null || _connectedWhoopDevice.State != Plugin.BLE.Abstractions.DeviceState.Connected) return;

            LogToConsole("Sending ToggleRealtimeHr(enable: true) command...");
            byte[] toggleHrPacket = WhoopPacketBuilder.ToggleRealtimeHr(true);
            bool sentHrToggle = await _connectedWhoopDevice.SendCommandAsync(toggleHrPacket);
            LogToConsole(sentHrToggle ? "ToggleRealtimeHr(true) command sent." : "Failed to send ToggleRealtimeHr(true) command.");

            await Task.Delay(500); // Small delay

            LogToConsole("Sending GetBatteryLevel command...");
            byte[] getBatteryPacket = WhoopPacketBuilder.GetBatteryLevel();
            bool sentBattery = await _connectedWhoopDevice.SendCommandAsync(getBatteryPacket);
            LogToConsole(sentBattery ? "GetBatteryLevel command sent." : "Failed to send GetBatteryLevel command.");
        }


        private void OnDataFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[MainPage DATA_FROM_STRAP RAW]: {BitConverter.ToString(data)}");
            if (data == null || data.Length < 3) return;

            PacketType packetType = (PacketType)data[0];
            byte payloadLengthInHeader = data[2];

            if (packetType == PacketType.RealtimeData) // RealtimeData = 40
            {
                LogToConsole("[MainPage DATA_FROM_STRAP] Received RealtimeData packet.");
                if (payloadLengthInHeader >= 1 && data.Length >= 4)
                {
                    byte heartRate = data[3];
                    LogToConsole($"--- HEART RATE (RealtimeData): {heartRate} bpm ---");
                    MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Status: HR: {heartRate} bpm");
                }
                else LogToConsole("[MainPage DATA_FROM_STRAP] RealtimeData packet payload too short for HR.");
            }
        }

        private void OnCmdFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[MainPage CMD_FROM_STRAP RAW]: {BitConverter.ToString(data)}");
            if (data == null || data.Length < 4) return;

            PacketType packetType = (PacketType)data[0];
            byte payloadLength = data[2];

            if (packetType == PacketType.CommandResponse)
            {
                CommandNumber originalCommand = (CommandNumber)data[3];
                LogToConsole($"Parsed CommandResponse for: {originalCommand}");

                if (originalCommand == CommandNumber.GetBatteryLevel)
                {
                    if (payloadLength >= 2 && data.Length >= 5)
                    {
                        byte batteryLevel = data[4];
                        LogToConsole($"Battery Level (from CommandResponse): {batteryLevel}%");
                        MainThread.BeginInvokeOnMainThread(async () => {
                            StatusLabel.Text = $"Status: Battery: {batteryLevel}%";
                            await DisplayAlert("Battery Level", $"Strap Battery (CMD_RESP): {batteryLevel}%", "OK");
                        });
                    }
                }
            }
        }

        private void OnEventsFromStrapReceivedHandler(object sender, byte[] data)
        {
            LogToConsole($"[MainPage EVENTS_FROM_STRAP RAW]: {BitConverter.ToString(data)}");
            if (data == null || data.Length < 1) return;

            if (data[0] == 0xAA) // Handle the non-standard 0xAA prefixed events separately
            {
                LogToConsole($"Received 0xAA-prefixed event. Full data: {BitConverter.ToString(data)}. Further parsing needed based on device spec.");
                return;
            }

            if (data.Length < 4) return; // Standard Whoop Event packet check

            PacketType packetType = (PacketType)data[0];
            byte payloadLength = data[2];

            if (packetType == PacketType.Event) // Event = 48 (0x30)
            {
                if (payloadLength < 1) return;
                EventNumber eventNumber = (EventNumber)data[3];
                LogToConsole($"Parsed Event: {eventNumber}");

                if (eventNumber == EventNumber.BatteryLevel) // BatteryLevel = 3
                {
                    if (payloadLength >= 2 && data.Length >= 5)
                    {
                        byte batteryLevelValue = data[4];
                        LogToConsole($"Battery Level Event: {batteryLevelValue}%");
                        MainThread.BeginInvokeOnMainThread(async () => {
                            StatusLabel.Text = $"Status: Battery Event: {batteryLevelValue}%";
                            await DisplayAlert("Battery Level", $"Strap Battery (Event): {batteryLevelValue}%", "OK");
                        });
                    }
                }
                else if (eventNumber == EventNumber.BleBonded) // BleBonded = 23
                {
                    LogToConsole("--- !!! Received BleBonded event from strap! ---");
                    MainThread.BeginInvokeOnMainThread(async () => await DisplayAlert("Bonding Event", "Received 'BleBonded' event from the strap!", "OK"));
                }
            }
            else LogToConsole($"Received packet type {packetType} on EVENTS_FROM_STRAP, but expected Event (48).");
        }

        private void OnDeviceDisconnectedHandler(object sender, EventArgs e)
        {
            if (sender is WhoopDevice disconnectedDevice)
            {
                LogToConsole($"Device {disconnectedDevice.Name} has disconnected.");
                MainThread.BeginInvokeOnMainThread(async () => {
                    StatusLabel.Text = $"Status: {disconnectedDevice.Name} disconnected.";
                    await DisplayAlert("Disconnected", $"Device {disconnectedDevice.Name} has disconnected.", "OK");
                    if (_connectedWhoopDevice?.Id == disconnectedDevice.Id)
                    {
                        // No need to re-dispose or re-unsubscribe here, DisconnectCurrentDevice or OnMainPageDisappearing handles it
                        _connectedWhoopDevice = null;
                    }
                });
            }
        }

        private async Task DisconnectCurrentDevice()
        {
            if (_connectedWhoopDevice != null)
            {
                LogToConsole($"Disconnecting from {_connectedWhoopDevice.Name}...");
                StatusLabel.Text = $"Status: Disconnecting from {_connectedWhoopDevice.Name}...";
                _connectedWhoopDevice.DataFromStrapReceived -= OnDataFromStrapReceivedHandler;
                _connectedWhoopDevice.CmdFromStrapReceived -= OnCmdFromStrapReceivedHandler;
                _connectedWhoopDevice.EventsFromStrapReceived -= OnEventsFromStrapReceivedHandler;
                // _connectedWhoopDevice.Disconnected -= OnDeviceDisconnectedHandler; // Let it fire once

                await _connectedWhoopDevice.DisconnectAsync();
                _connectedWhoopDevice.Dispose(); // Call dispose after disconnect attempt
                _connectedWhoopDevice = null;
                LogToConsole("Disconnected.");
                StatusLabel.Text = "Status: Disconnected. Scan for devices.";
            }
        }

        public async Task<bool> CheckAndRequestBluetoothPermissions()
        {
            PermissionStatus overallStatus = PermissionStatus.Unknown;
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                if (DeviceInfo.Version.Major >= 12)
                {
                    var bluetoothPermissions = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                    if (bluetoothPermissions != PermissionStatus.Granted) bluetoothPermissions = await Permissions.RequestAsync<Permissions.Bluetooth>();
                    overallStatus = bluetoothPermissions;
                }
                else
                {
                    var locationPermission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                    if (locationPermission != PermissionStatus.Granted) locationPermission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    overallStatus = locationPermission;
                }
                return overallStatus == PermissionStatus.Granted;
            }
            return true;
        }

        private void LogToConsole(string message)
        {
            string logEntry = $"{DateTime.Now:HH:mm:ss}: {message}{Environment.NewLine}";
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ConsoleOutput = logEntry + ConsoleOutput; // Prepend for newest at top
                if (ConsoleOutput.Length > 5000) ConsoleOutput = ConsoleOutput.Substring(0, 5000);
            });
            System.Diagnostics.Debug.WriteLine(message);
        }

        private async void OnMainPageDisappearing(object sender, EventArgs e)
        {
            LogToConsole("MainPage disappearing. Cleaning up resources.");
            if (_scanner != null)
            {
                await _scanner.StopScanAsync();
                _scanner.Dispose();
                _scanner = null;
            }
            await DisconnectCurrentDevice();
            if (_dbContext != null)
            {
                await _dbContext.DisposeAsync();
                _dbContext = null;
            }
        }
    }
}