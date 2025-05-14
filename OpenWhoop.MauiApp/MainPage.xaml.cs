using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using OpenWhoop.Core.Data;
using Plugin.BLE.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenWhoop.App; // For WhoopDevice, WhoopPacketBuilder, Enums
using System.Linq;
using System.Text;
using OpenWhoop.App.Protocol;
using OpenWhoop.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using OpenWhoop.App.Services;

namespace OpenWhoop.MauiApp
{
    public partial class MainPage : ContentPage
    {
        private BluetoothService _btService;
        public ObservableCollection<DiscoveredDeviceInfo> Devices => _btService?.DiscoveredDevices;

        private WhoopDevice? _connectedWhoopDevice;
        private DbService _dbService;
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
                    OnPropertyChanged();
                }
            }
        }

        public MainPage(DbService dbService)
        {
            InitializeComponent();
            _btService = new BluetoothService();
            _dbService = dbService;
            _dbContext = _dbService.Context;
            BindingContext = this;
            this.Disappearing += OnMainPageDisappearing;
            UpdateCommandButtonsState(false); // Initially disable all command buttons

        }

        private void SetBatteryLevel(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            double fillWidth = 40 * percent / 100.0;
            BatteryFill.WidthRequest = fillWidth;
            BatteryLabel.Text = $"{percent}%";
            if (percent > 50)
                BatteryFill.Color = Colors.LimeGreen;
            else if (percent > 20)
                BatteryFill.Color = Colors.Gold;
            else
                BatteryFill.Color = Colors.Red;
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

            if (_btService.DiscoveredDevices.Any()) _btService.DiscoveredDevices.Clear();
            if (sender is Button scanButton) scanButton.IsEnabled = false;

            LogToConsole("Starting BLE scan...");
            await _btService.StartScanAsync(TimeSpan.FromSeconds(10));
            LogToConsole("Scan finished or timed out.");
            StatusLabel.Text = _btService.DiscoveredDevices.Any() ? "Status: Scan complete. Select device." : "Status: No devices found.";

            if (sender is Button scanButtonAfterScan) scanButtonAfterScan.IsEnabled = true;
            if (!_btService.DiscoveredDevices.Any()) LogToConsole("No devices found.");
        }
        private async void OnDeviceSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is DiscoveredDeviceInfo selectedUiDevice)
            {
                LogToConsole($"Device selected: {selectedUiDevice.Name}");
                StatusLabel.Text = $"Status: Connecting to {selectedUiDevice.Name}...";
                await _btService.StopScanAsync();
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
                        // Save connected device setting to database
                        try
                        {
                            var existingSetting = await _dbContext.StoredDeviceSettings
                                .FirstOrDefaultAsync(s => s.DeviceId == bleDevice.Id.ToString());
                            if (existingSetting == null)
                            {
                                existingSetting = new StoredDeviceSetting
                                {
                                    DeviceId = bleDevice.Id.ToString(),
                                    DeviceName = bleDevice.Name,
                                    LastConnectedUtc = DateTime.UtcNow
                                };
                                _dbContext.StoredDeviceSettings.Add(existingSetting);
                            }
                            else
                            {
                                existingSetting.DeviceName = bleDevice.Name;
                                existingSetting.LastConnectedUtc = DateTime.UtcNow;
                                _dbContext.StoredDeviceSettings.Update(existingSetting);
                            }
                            await _dbContext.SaveChangesAsync();
                            LogToConsole("Saved connected device information.");
                        }
                        catch (Exception ex)
                        {
                            LogToConsole($"Error saving device setting: {ex.Message}");
                        }

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
            GetHello.IsEnabled = isEnabled;
            ToggleHrOnButton.IsEnabled = isEnabled;
            ToggleHrOffButton.IsEnabled = isEnabled;
            //AbortHistoricalButton.IsEnabled = isEnabled;
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
            bool sent = await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.GetBatteryLevel());
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

            LogToConsole($"[MainPage DATA_FROM_STRAP]: Type={packet.PacketType}, Cmd/Evt={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Length}");

            if (packet.PacketType == PacketType.RealtimeData)
            {
                LogToConsole("[MainPage DATA_FROM_STRAP] Received RealtimeData packet.");
                if (packet.Payload.Length >= 1)
                {
                    byte heartRate = packet.Payload[2];
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
                LogToConsole($"[MainPage DATA_FROM_STRAP] Received HistoricalData packet! PayloadLen={packet.Payload.Length}");
                if (!_isHistoricalSyncActive)
                {
                    LogToConsole("Received HistoricalData but sync is not marked active. Ignoring.");
                    return;
                }

                const int recordSize = 5; // 4 for timestamp, 1 for HR
                int numRecordsProcessed = 0;

                int currentOffsetInPayload = 0;
                while (currentOffsetInPayload + recordSize <= packet.Payload.Length)
                {
                    //uint recordTimestampUnix = BitConverter.ToUInt32(packet.Payload.Array, packet.Payload.Offset + currentOffsetInPayload);
                    //byte hr = packet.Payload.Array[packet.Payload.Offset + currentOffsetInPayload + 4];
                    //DateTimeOffset recordTime = DateTimeOffset.FromUnixTimeSeconds(recordTimestampUnix);

                    //LogToConsole($"  Raw Historical Record: TimestampUnix={recordTimestampUnix} ({recordTime:yyyy-MM-dd HH:mm:ss} UTC), HR={hr}");

                    //if (recordTime >= _historicalSyncStartTime && recordTime < _historicalSyncEndTime)
                    //{
                    //    LogToConsole($"    VALID (in window): HR: {hr} at {recordTime:HH:mm:ss}");
                    //    numRecordsProcessed++;
                    //}
                    //else if (recordTime >= _historicalSyncEndTime)
                    //{
                    //    LogToConsole($"    Record timestamp {recordTime:HH:mm:ss} is BEYOND sync window end time. Requesting abort.");
                    //    MainThread.BeginInvokeOnMainThread(async () =>
                    //    {
                    //        //  await AbortHistoricalSyncIfNeeded(); // Implement this method if needed
                    //    });
                    //    _isHistoricalSyncActive = false; // Stop sync as we are past the window
                    //    StatusLabel.Text = "Status: Historical sync window ended.";
                    //    LogToConsole("Historical sync stopped: data beyond end time.");
                    //    // Consider sending AbortHistoricalTransmits command here
                    //    // byte[] abortPacket = WhoopPacketBuilder.AbortHistoricalTransmits();
                    //    // await _connectedWhoopDevice.SendCommandAsync(abortPacket);
                    //    break;
                    //}
                    //else
                    //{
                    //    LogToConsole($"    Record timestamp {recordTime:HH:mm:ss} is BEFORE sync window start time. Skipping.");
                    //}
                    currentOffsetInPayload += recordSize;
                }

                if (numRecordsProcessed > 0)
                {
                    MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Status: Got {numRecordsProcessed} historical HR.");
                }
                if (currentOffsetInPayload < packet.Payload.Length && packet.Payload.Length > 0) // Check for partial record
                {
                    LogToConsole($"  Warning: Partial historical record at end of payload. Remaining bytes: {packet.Payload.Length - currentOffsetInPayload}");
                }
                // If no records were processed and we are still active, it might be the end of data or an empty packet.
                // The strap might send an empty HistoricalData packet to signify end of transmission.
                // Check openwhoop behavior for how it detects end of historical sync.
                if (packet.Payload.Length == 0 && _isHistoricalSyncActive)
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

            LogToConsole($"[MainPage EVENTS_FROM_STRAP]: Type={packet.PacketType}, EventNum={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Length}");

            if (packet.PacketType == PacketType.Event)
            {
                EventNumber eventNumber = (EventNumber)packet.CommandOrEventNumber;
                LogToConsole($"Parsed Event: {eventNumber}");

                if (eventNumber == EventNumber.BatteryLevel)
                {
                    if (packet.Payload.Length >= 1)
                    {
                        byte batteryLevelValue = packet.Payload[2];
                        LogToConsole($"Battery Level Event: {batteryLevelValue}%");
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
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
                }
                else if (eventNumber == EventNumber.DoubleTap)
                {
                    LogToConsole("--- !!! Received DoubleTap event from strap! ---");
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
            // LogToConsole($"[MainPage CMD_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage CMD_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }

            LogToConsole($"[MainPage CMD_FROM_STRAP]: Type={packet.PacketType}, OrigCmd={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Length}");

            if (packet.PacketType == PacketType.CommandResponse)
            {
                CommandNumber originalCommand = (CommandNumber)packet.CommandOrEventNumber;
                LogToConsole($"Parsed CommandResponse for: {originalCommand}");

                switch (originalCommand)
                {
                    case CommandNumber.GetBatteryLevel:
                        if (packet.Payload.Length >= 1)
                        {
                            byte batteryLevel = packet.Payload[2];
                            LogToConsole($"Battery Level (from CommandResponse): {batteryLevel}%");
                            MainThread.BeginInvokeOnMainThread(async () => { SetBatteryLevel(batteryLevel); });
                        }
                        else
                        {
                            LogToConsole("GetBatteryLevel CommandResponse packet has insufficient payload length.");
                        }
                        break;
                }
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
                MainThread.BeginInvokeOnMainThread(async () =>
                {
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
        private async void OnMainPageDisappearing(object? sender, EventArgs e)
        {
            LogToConsole("MainPage disappearing. Cleaning up resources.");
            if (_btService != null)
            {
                await _btService.StopScanAsync();
                _btService.Dispose();
                _btService = null;
            }
            await DisconnectCurrentDevice();
            if (_dbContext != null)
            {
                await _dbContext.DisposeAsync();
                _dbContext = null;
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await TryReconnectSavedDeviceAsync();
        }

        private async Task TryReconnectSavedDeviceAsync()
        {
            try
            {
                var saved = await _dbContext.StoredDeviceSettings
                    .OrderByDescending(s => s.LastConnectedUtc)
                    .FirstOrDefaultAsync();
                if (saved != null)
                {
                    ScanButton.IsVisible = false;
                    LogToConsole($"Found saved device {saved.DeviceName}, attempting reconnect...");
                    StatusLabel.Text = $"Status: Reconnecting to {saved.DeviceName}...";

                    IDevice bleDevice;
                    try
                    {
                        bleDevice = await _btService.ConnectToKnownDeviceAsync(Guid.Parse(saved.DeviceId));
                    }
                    catch (Exception ex)
                    {
                        LogToConsole($"Auto-reconnect failed: {ex.Message}");
                        ScanButton.IsVisible = true;
                        return;
                    }

                    _connectedWhoopDevice = new WhoopDevice(bleDevice, _dbContext);
                    _connectedWhoopDevice.Disconnected += OnDeviceDisconnectedHandler;
                    _connectedWhoopDevice.DataFromStrapReceived += OnDataFromStrapReceivedHandler;
                    _connectedWhoopDevice.CmdFromStrapReceived += OnCmdFromStrapReceivedHandler;
                    _connectedWhoopDevice.EventsFromStrapReceived += OnEventsFromStrapReceivedHandler;

                    // Initialize device
                    bool initialized = await _connectedWhoopDevice.InitializeAsync();
                    if (initialized)
                    {
                        LogToConsole($"Reconnected to {saved.DeviceName}. Ready for commands.");
                        UpdateCommandButtonsState(true);
                        await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.GetBatteryLevel());
                    }
                    else
                    {
                        LogToConsole($"Initialization after reconnect failed for {saved.DeviceName}.");
                        ScanButton.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error during auto-reconnect: {ex.Message}");
                ScanButton.IsVisible = true;
            }
        }
    }
}