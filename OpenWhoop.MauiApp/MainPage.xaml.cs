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
using OpenWhoop.App.Protocol;

namespace OpenWhoop.MauiApp
{
    public partial class MainPage : ContentPage
    {
        private WhoopDeviceScanner _scanner;
        public ObservableCollection<DiscoveredDeviceInfo> Devices => _scanner?.DiscoveredDevices;

        private WhoopDevice _connectedWhoopDevice;
        private AppDbContext _dbContext;
        private bool _isHistoricalSyncActive = false;
        private DateTimeOffset _historicalSyncStartTime;
        private DateTimeOffset _historicalSyncEndTime;

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
            UpdateCommandButtonsState(false); // Initially disable all command buttons
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
                        UpdateCommandButtonsState(true); // Enable command buttons
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
        private void UpdateCommandButtonsState(bool isEnabled)
        {
            GetBatteryButton.IsEnabled = isEnabled;
            ToggleHrOnButton.IsEnabled = isEnabled;
            ToggleHrOffButton.IsEnabled = isEnabled;
            AbortHistoricalButton.IsEnabled = isEnabled;
            DisconnectButton.IsEnabled = isEnabled;
            Sync6HoursButton.IsEnabled = isEnabled;
        }
        private async void OnSyncLast6HoursClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            if (_isHistoricalSyncActive)
            {
                LogToConsole("Historical sync is already active. Abort first if you want to restart.");
                return;
            }

            _historicalSyncEndTime = DateTimeOffset.UtcNow;
            _historicalSyncStartTime = _historicalSyncEndTime.AddHours(-6);
            uint startTimestampUnix = (uint)_historicalSyncStartTime.ToUnixTimeSeconds();

            LogToConsole($"Requesting historical HR data from: {_historicalSyncStartTime:yyyy-MM-dd HH:mm:ss} UTC (Unix: {startTimestampUnix})");
            LogToConsole($"Window ends at: {_historicalSyncEndTime:yyyy-MM-dd HH:mm:ss} UTC (Unix: {(uint)_historicalSyncEndTime.ToUnixTimeSeconds()})");


            LogToConsole("Sending SetReadPointer command...");
            var pb = new WhoopPacketBuilder();
            byte[] setPointerPacket = pb.SetReadPointer(startTimestampUnix);
            bool pointerSent = await _connectedWhoopDevice.SendCommandAsync(setPointerPacket);
            if (!pointerSent)
            {
                LogToConsole("Failed to send SetReadPointer command.");
                return;
            }
            LogToConsole("SetReadPointer command sent. Waiting briefly...");
            await Task.Delay(500); // Give strap a moment to process

            LogToConsole("Sending SendHistoricalData(start: true) command...");
            var pb2 = new WhoopPacketBuilder();
            byte[] startSyncPacket = pb2.SendHistoricalData(true);
            bool syncStarted = await _connectedWhoopDevice.SendCommandAsync(startSyncPacket);

            if (syncStarted)
            {
                LogToConsole("SendHistoricalData(true) command sent. Sync active.");
                _isHistoricalSyncActive = true;
                StatusLabel.Text = "Status: Syncing last 6hrs HR...";
            }
            else
            {
                LogToConsole("Failed to send SendHistoricalData(true) command.");
                _isHistoricalSyncActive = false;
            }
            UpdateCommandButtonsState(true); // Re-evaluate button states
        }
        private async void OnGetBatteryLevelClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending GetBatteryLevel command...");
            byte[] packet = WhoopPacketBuilder.GetBatteryLevel();
            bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
            LogToConsole(sent ? "GetBatteryLevel command sent." : "Failed to send GetBatteryLevel command.");
        }
        private async void OnToggleRealtimeHrOnClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending ToggleRealtimeHr(enable: true) command...");
            byte[] packet = WhoopPacketBuilder.ToggleRealtimeHr(true);
            bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
            LogToConsole(sent ? "ToggleRealtimeHr(true) command sent." : "Failed to send ToggleRealtimeHr(true) command.");
        }

        private async void OnToggleRealtimeHrOffClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending ToggleRealtimeHr(enable: false) command...");
            byte[] packet = WhoopPacketBuilder.ToggleRealtimeHr(false);
            bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
            LogToConsole(sent ? "ToggleRealtimeHr(false) command sent." : "Failed to send ToggleRealtimeHr(false) command.");
        }

        private async void OnDisconnectClicked(object sender, EventArgs e)
        {
            await DisconnectCurrentDevice();
        }

        // --- New Historical Data Command Button Click Handlers ---
        private async void OnSetReadPointerZeroClicked(object sender, EventArgs e)
        {
            //if (!IsDeviceConnectedAndReady()) return;
            //LogToConsole("Sending SetReadPointer(0) command...");
            //// Using 0 as a common way to request data from the beginning
            //byte[] packet = WhoopPacketBuilder.SetReadPointer(0);
            //bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
            //LogToConsole(sent ? "SetReadPointer(0) command sent." : "Failed to send SetReadPointer(0) command.");
        }

        private async void OnStartHistoricalSyncClicked(object sender, EventArgs e)
        {
        //    if (!IsDeviceConnectedAndReady()) return;
        //    LogToConsole("Sending SendHistoricalData(start: true) command...");
        //    byte[] packet = WhoopPacketBuilder.SendHistoricalData(true);
        //    bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
        //    LogToConsole(sent ? "SendHistoricalData(true) command sent." : "Failed to send SendHistoricalData(true) command.");
        //    StatusLabel.Text = "Status: Historical sync requested...";
        }

        private async void OnAbortHistoricalSyncClicked(object sender, EventArgs e)
        {
            //if (!IsDeviceConnectedAndReady()) return;
            //LogToConsole("Sending AbortHistoricalTransmits command...");
            //byte[] packet = WhoopPacketBuilder.AbortHistoricalTransmits();
            //bool sent = await _connectedWhoopDevice.SendCommandAsync(packet);
            //LogToConsole(sent ? "AbortHistoricalTransmits command sent." : "Failed to send AbortHistoricalTransmits command.");
            //StatusLabel.Text = "Status: Historical sync abort requested.";
        }

        private bool IsDeviceConnectedAndReady()
        {
            if (_connectedWhoopDevice == null || _connectedWhoopDevice.State != Plugin.BLE.Abstractions.DeviceState.Connected)
            {
                LogToConsole("Device not connected or not ready.");
                return false;
            }
            return true;
        }


       private void OnDataFromStrapReceivedHandler(object sender, ParsedWhoopPacket packet) // Changed from byte[]
        {
            LogToConsole($"[MainPage DATA_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage DATA_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }

            LogToConsole($"[MainPage DATA_FROM_STRAP]: Type={packet.PacketType}, Cmd/Evt={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Count}");

            if (packet.PacketType == PacketType.RealtimeData)
            {
                LogToConsole("[MainPage DATA_FROM_STRAP] Received RealtimeData packet.");
                if (packet.Payload.Count >= 1)
                {
                    byte heartRate = packet.Payload.Array[packet.Payload.Offset];
                    LogToConsole($"--- HEART RATE (RealtimeData): {heartRate} bpm ---");
                    MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Status: HR: {heartRate} bpm");
                }
                else
                {
                    LogToConsole("RealtimeData packet has insufficient payload length.");
                }
            }
            else if (packet.PacketType == PacketType.HistoricalData)
            {
                LogToConsole($"[MainPage DATA_FROM_STRAP] Received HistoricalData packet! PayloadLen={packet.Payload.Count}");
                if (!_isHistoricalSyncActive)
                {
                    LogToConsole("Received HistoricalData but sync is not marked active. Ignoring.");
                    return;
                }

                const int recordSize = 5; // 4 for timestamp, 1 for HR
                int numRecordsProcessed = 0;
                
                int currentOffsetInPayload = 0;
                while (currentOffsetInPayload + recordSize <= packet.Payload.Count)
                {
                    uint recordTimestampUnix = BitConverter.ToUInt32(packet.Payload.Array, packet.Payload.Offset + currentOffsetInPayload);
                    byte hr = packet.Payload.Array[packet.Payload.Offset + currentOffsetInPayload + 4];
                    DateTimeOffset recordTime = DateTimeOffset.FromUnixTimeSeconds(recordTimestampUnix);

                    LogToConsole($"  Raw Historical Record: TimestampUnix={recordTimestampUnix} ({recordTime:yyyy-MM-dd HH:mm:ss} UTC), HR={hr}");

                    if (recordTime >= _historicalSyncStartTime && recordTime < _historicalSyncEndTime)
                    {
                        LogToConsole($"    VALID (in window): HR: {hr} at {recordTime:HH:mm:ss}");
                        numRecordsProcessed++;
                    }
                    else if (recordTime >= _historicalSyncEndTime)
                    {
                        LogToConsole($"    Record timestamp {recordTime:HH:mm:ss} is BEYOND sync window end time. Requesting abort.");
                        MainThread.BeginInvokeOnMainThread(async () => {
                          //  await AbortHistoricalSyncIfNeeded(); // Implement this method if needed
                        });
                        _isHistoricalSyncActive = false; // Stop sync as we are past the window
                        StatusLabel.Text = "Status: Historical sync window ended.";
                        LogToConsole("Historical sync stopped: data beyond end time.");
                        // Consider sending AbortHistoricalTransmits command here
                        // byte[] abortPacket = WhoopPacketBuilder.AbortHistoricalTransmits();
                        // await _connectedWhoopDevice.SendCommandAsync(abortPacket);
                        break; 
                    }
                    else
                    {
                        LogToConsole($"    Record timestamp {recordTime:HH:mm:ss} is BEFORE sync window start time. Skipping.");
                    }
                    currentOffsetInPayload += recordSize;
                }

                if (numRecordsProcessed > 0)
                {
                    MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Status: Got {numRecordsProcessed} historical HR.");
                }
                if (currentOffsetInPayload < packet.Payload.Count && packet.Payload.Count > 0) // Check for partial record
                {
                    LogToConsole($"  Warning: Partial historical record at end of payload. Remaining bytes: {packet.Payload.Count - currentOffsetInPayload}");
                }
                // If no records were processed and we are still active, it might be the end of data or an empty packet.
                // The strap might send an empty HistoricalData packet to signify end of transmission.
                // Check openwhoop behavior for how it detects end of historical sync.
                if (packet.Payload.Count == 0 && _isHistoricalSyncActive)
                {
                    LogToConsole("Received empty HistoricalData packet. Assuming end of sync.");
                    _isHistoricalSyncActive = false;
                    StatusLabel.Text = "Status: Historical sync complete (empty packet).";
                     // byte[] abortPacket = WhoopPacketBuilder.AbortHistoricalTransmits(); // Good practice to send abort
                     // await _connectedWhoopDevice.SendCommandAsync(abortPacket);
                }
            }
        }

      
        private void OnEventsFromStrapReceivedHandler(object sender, ParsedWhoopPacket packet) // Changed from byte[]
        {
            LogToConsole($"[MainPage EVENTS_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage EVENTS_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }

            LogToConsole($"[MainPage EVENTS_FROM_STRAP]: Type={packet.PacketType}, EventNum={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Count}");

            if (packet.PacketType == PacketType.Event)
            {
                EventNumber eventNumber = (EventNumber)packet.CommandOrEventNumber;
                LogToConsole($"Parsed Event: {eventNumber}");

                if (eventNumber == EventNumber.BatteryLevel)
                {
                    if (packet.Payload.Count >= 1)
                    {
                        byte batteryLevelValue = packet.Payload.Array[packet.Payload.Offset];
                        LogToConsole($"Battery Level Event: {batteryLevelValue}%");
                        MainThread.BeginInvokeOnMainThread(async () => {
                            StatusLabel.Text = $"Status: Battery Event: {batteryLevelValue}%";
                            await DisplayAlert("Battery Level", $"Strap Battery (Event): {batteryLevelValue}%", "OK");
                        });
                    }
                    else
                    {
                         LogToConsole("BatteryLevel Event packet has insufficient payload length.");
                    }
                }
                else if (eventNumber == EventNumber.BleBonded)
                {
                    LogToConsole("--- !!! Received BleBonded event from strap! ---");
                    MainThread.BeginInvokeOnMainThread(async () => await DisplayAlert("Bonding Event", "Received 'BleBonded' event from the strap!", "OK"));
                }
                else if (eventNumber == EventNumber.DoubleTap)
                {
                    LogToConsole("--- !!! Received DoubleTap event from strap! ---");
                    MainThread.BeginInvokeOnMainThread(async () => await DisplayAlert("DoubleTap", "Received 'DoubleTap' event from the strap!", "OK"));
                }
                // ... other event numbers
            }
            else 
            {
                LogToConsole($"Received packet type {packet.PacketType} on EVENTS_FROM_STRAP, but expected Event ({PacketType.Event}).");
            }
        }

        private void OnCmdFromStrapReceivedHandler(object sender, ParsedWhoopPacket packet) // Changed from byte[]
        {
            LogToConsole($"[MainPage CMD_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage CMD_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }
            
            LogToConsole($"[MainPage CMD_FROM_STRAP]: Type={packet.PacketType}, OrigCmd={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Count}");

            if (packet.PacketType == PacketType.CommandResponse)
            {
                CommandNumber originalCommand = (CommandNumber)packet.CommandOrEventNumber;
                LogToConsole($"Parsed CommandResponse for: {originalCommand}");

                if (originalCommand == CommandNumber.GetBatteryLevel)
                {
                    if (packet.Payload.Count >= 1)
                    {
                        byte batteryLevel = packet.Payload.Array[packet.Payload.Offset];
                        LogToConsole($"Battery Level (from CommandResponse): {batteryLevel}%");
                        MainThread.BeginInvokeOnMainThread(async () => {
                            StatusLabel.Text = $"Status: Battery: {batteryLevel}%";
                            await DisplayAlert("Battery Level", $"Strap Battery (CMD_RESP): {batteryLevel}%", "OK");
                        });
                    }
                     else
                    {
                        LogToConsole("GetBatteryLevel CommandResponse packet has insufficient payload length.");
                    }
                }
                // Add more command response handling here if needed
            }
            else
            {
                 LogToConsole($"Received packet type {packet.PacketType} on CMD_FROM_STRAP, but expected CommandResponse ({PacketType.CommandResponse}).");
            }
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