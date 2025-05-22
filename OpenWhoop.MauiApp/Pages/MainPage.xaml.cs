using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using OpenWhoop.Core.Data;
using Plugin.BLE.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using OpenWhoop.App; // For WhoopDevice, WhoopPacketBuilder, Enums
using System.Linq;
using System.Text;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using OpenWhoop.App.Protocol;
using OpenWhoop.Core.Entities;
using OpenWhoop.App.Services;

namespace OpenWhoop.MauiApp.Pages
{
    public partial class MainPage : ContentPage
    {
        private BluetoothService _btService;
        public ObservableCollection<DiscoveredDeviceInfo> Devices => _btService?.DiscoveredDevices;

        private WhoopDevice? _connectedWhoopDevice;
        private DbService _dbService;
        private AppDbContext _dbContext;
        private const int HeartRateBatchSize = 50;
        private readonly List<HeartRateSample> _hrSampleBuffer = new();
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
        public ISeries[] Series { get; set; } = [
            new LineSeries<double>
            {
                Values = [2, 1, 3, 5, 3, 4, 6,3,4,5,7,5,3,6,4,2,3,2,4,5,6,7,8,8,9,9,8,56,4,4,3,3,3,2],
                Fill = null,
                GeometrySize = 5
            }
        ];

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

        private async void OnMainPageDisappearing(object? sender, EventArgs e)
        {
            LogToConsole("MainPage disappearing. Cleaning up resources.");
            if (_connectedWhoopDevice != null)
            {
                await DisconnectCurrentDevice();
                _connectedWhoopDevice = null;
            }
            if (_btService != null)
            {
                await _btService.StopScanAsync();
                _btService.Dispose();
                _btService = null;
            }
            await DisconnectCurrentDevice();
        }
        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await TryReconnectSavedDeviceAsync();
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

                _connectedWhoopDevice = new WhoopDevice(bleDevice);
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
            GetClockButton.IsEnabled = isEnabled;
            ToggleHrOnButton.IsEnabled = isEnabled;
            ToggleHrOffButton.IsEnabled = isEnabled;
            AbortHistoricalButton.IsEnabled = isEnabled;
            DisconnectButton.IsEnabled = isEnabled;
            SyncButton.IsEnabled = isEnabled;
            ResetButton.IsEnabled = isEnabled;

        }
        private async void OnGetClockClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending GetClock command...");
            var cmd = WhoopPacketBuilder.GetClock();
            bool sent = await _connectedWhoopDevice.SendCommandAsync(cmd);
            LogToConsole(sent ? "GetClock command sent." : "Failed to send GetClock command.");
        }
        private async void OnResetClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending Reset command...");
            var cmd = WhoopPacketBuilder.Reset();
            bool sent = await _connectedWhoopDevice.SendCommandAsync(cmd);
            LogToConsole(sent ? "Reset command sent." : "Failed to send Reset command.");
        }
        private async void OnAbortHistoricalClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;
            LogToConsole("Sending AbortHistoricalTransmits command...");
            var cmd = WhoopPacketBuilder.AbortHistoricalTransmits();
            bool sent = await _connectedWhoopDevice.SendCommandAsync(cmd);
            if (sent)
            {
                LogToConsole("AbortHistoricalTransmits command sent.");
                StatusLabel.Text = "Status: Sync aborted.";
            }
            else
            {
                LogToConsole("Failed to send AbortHistoricalTransmits command.");
            }
        }
        private async void OnSyncHistoryClicked(object sender, EventArgs e)
        {
            if (!IsDeviceConnectedAndReady()) return;

            LogToConsole("Sending SendHistoricalData(start: true) command...");
            bool syncStarted = await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.SendHistoricalData());
            if (syncStarted)
            {
                LogToConsole("SendHistoricalData(true) command sent. Sync active.");
                StatusLabel.Text = "Status: Syncing history...";
            }
            else
            {
                LogToConsole("Failed to send SendHistoricalData(true) command.");
            }
            UpdateCommandButtonsState(true);
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
        private async void OnDataFromStrapReceivedHandler(object sender, ParsedWhoopPacket packet) // Changed from byte[]
        {
            //LogToConsole($"[MainPage DATA_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage DATA_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }

            //  LogToConsole($"[MainPage DATA_FROM_STRAP]: Type={packet.PacketType}, Cmd/Evt={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Length}");
            switch (packet.PacketType)
            {
                case PacketType.RealtimeData:
                    LogToConsole("[MainPage DATA_FROM_STRAP] Received RealtimeData packet.");
                    if (packet.Payload.Length >= 1)
                    {
                        byte heartRate = packet.Payload[5];
                        LogToConsole($"--- HEART RATE (RealtimeData): {heartRate} bpm ---");
                        MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Status: HR: {heartRate} bpm");
                    }
                    else
                    {
                        LogToConsole("RealtimeData packet has insufficient payload length.");
                    }

                    break;
                case PacketType.Metadata:
                    await HandleHistoricalMetaData(packet);
                    break;
                case PacketType.ConsoleLogs:
                    //LogToConsole("CL: " + Encoding.ASCII.GetString(packet.Payload));
                    break;
                case PacketType.HistoricalData:
                    LogToConsole($"[MainPage DATA_FROM_STRAP] Received HistoricalData packet! PayloadLen={packet.Payload.Length}");
                    await HandleHistoricalData(packet);
                    break;
            }
        }

        private async Task HandleHistoricalData(ParsedWhoopPacket packet)
        {
            var payload = packet.Payload;
            int offset = 0;

            while (offset + 24 <= payload.Length) // 4+4+6+1+1+8+4 = 28, but only 24 are required for minimum valid packet
            {
                // 1. Skip 4 bytes
                offset += 4;

                // 2. Read 4 bytes unix timestamp (little-endian)
                uint unix = BitConverter.ToUInt32(payload, offset);
                offset += 4;

                // 3. Skip 6 bytes
                offset += 6;

                // 4. Read 1 byte bpm
                byte bpm = payload[offset];
                offset += 1;

                // 5. Read 1 byte rr_count
                byte rr_count = payload[offset];
                offset += 1;

                // 6. Read up to 4 RR intervals (2 bytes each, little-endian)
                var rr = new List<ushort>();
                for (int i = 0; i < 4; i++)
                {
                    ushort rrValue = BitConverter.ToUInt16(payload, offset);
                    offset += 2;
                    if (rrValue != 0)
                        rr.Add(rrValue);
                }
                if (rr.Count != rr_count)
                {
                    //  LogToConsole($"RR count mismatch: expected {rr_count}, got {rr.Count}. Skipping sample.");
                    // Optionally skip this sample or continue
                    continue;
                }

                // 7. Read 4 bytes activity ID (little-endian)
                uint activity = BitConverter.ToUInt32(payload, offset);
                offset += 4;

                // Save to DB
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
                var hrSample = new HeartRateSample
                {
                    TimestampUtc = timestamp.UtcDateTime,
                    Value = bpm,
                    CreatedAtUtc = DateTime.UtcNow,
                    ActivityId = (int)activity,
                    RrIntervals = rr
                };
                _hrSampleBuffer.Add(hrSample);
                if (_hrSampleBuffer.Count >= HeartRateBatchSize)
                {
                    await SaveHeartRateSamplesInBuffer();
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

                switch (eventNumber)
                {
                    case EventNumber.BatteryLevel:
                        if (packet.Payload.Length >= 1)
                        {
                            byte batteryLevelValue = packet.Payload[2];
                            LogToConsole($"Battery Level Event: {batteryLevelValue}%");
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                StatusLabel.Text = $"Status: Battery Event: {batteryLevelValue}%";
                            });
                        }
                        else
                        {
                            LogToConsole("BatteryLevel Event packet has insufficient payload length.");
                        }
                        break;
                    case EventNumber.DoubleTap:
                        LogToConsole("!!! Received DoubleTap event from strap! ---");
                        break;
                    case EventNumber.Error:
                        LogToConsole($"!!! whoop reported An error {Encoding.ASCII.GetString(packet.Payload)}");
                        break;
                }
            }
            else
            {
                LogToConsole($"Received packet type {packet.PacketType} on EVENTS_FROM_STRAP, but expected Event ({PacketType.Event}).");
            }
        }
        private async void OnCmdFromStrapReceivedHandler(object sender, ParsedWhoopPacket packet) // Changed from byte[]
        {
            // LogToConsole($"[MainPage CMD_FROM_STRAP RAW]: {BitConverter.ToString(packet.RawData)} (Valid: {packet.IsValid}, Error: {packet.Error})");
            if (!packet.IsValid)
            {
                LogToConsole($"[MainPage CMD_FROM_STRAP] Invalid packet received: {packet.ErrorMessage}");
                return;
            }

            // LogToConsole($"[MainPage CMD_FROM_STRAP]: Type={packet.PacketType}, OrigCmd={packet.CommandOrEventNumber:X2}, Seq={packet.Sequence:X2}, PayloadLen={packet.Payload.Length}");

            if (packet.PacketType == PacketType.CommandResponse)
            {
                CommandNumber originalCommand = (CommandNumber)packet.CommandOrEventNumber;
                LogToConsole($"Parsed CommandResponse for: {originalCommand}");

                switch (originalCommand)
                {
                    case CommandNumber.HistoricalDataResult:
                        await HandleHistoricalData(packet);
                        break;
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
                    case CommandNumber.AbortHistoricalTransmits:
                        await SaveHeartRateSamplesInBuffer();
                        break;
                    case CommandNumber.GetClock:
                        if (packet.Payload.Length >= 1)
                        {
                            var payload = BitConverter.ToString(packet.Payload);
                            uint unixTime = BitConverter.ToUInt32(packet.Payload, 2);
                            DateTime utcDateTime = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                            LogToConsole($"Clock on strap: {utcDateTime:u}");
                        }
                        else
                        {
                            LogToConsole("GetBatteryLevel CommandResponse packet has insufficient payload length.");
                        }
                        break;
                    case CommandNumber.SendHistoricalData:
                        if (packet.Payload.Length >= 1)
                        {
                            var payload = packet.Payload;

                        }
                        else
                        {
                            LogToConsole("CommandResponse packet has insufficient payload length.");
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
                    if (_connectedWhoopDevice?.Id == disconnectedDevice.Id)
                    {
                        // No need to re-dispose or re-unsubscribe here, DisconnectCurrentDevice or OnMainPageDisappearing handles it
                        _connectedWhoopDevice = null;
                    }
                });
            }
        }
        private async Task HandleHistoricalMetaData(ParsedWhoopPacket packet)
        {
            //CREATE METADATA
            var cmd = packet.CommandOrEventNumber;
            int metaoffset = 0;
            uint metaunix = BitConverter.ToUInt32(packet.Payload, metaoffset);
            metaoffset += 4;
            metaoffset += 6;
            uint data = BitConverter.ToUInt32(packet.Payload, metaoffset);

            var metadata = new HistoryMetadata(metaunix, data, (MetadataType)cmd);
            Console.WriteLine($"Got Historical Metadata {metadata.Cmd} with {metadata.Data}");
            if (metadata.Cmd == MetadataType.HistoryEnd)
            {
                var nextHistoryPacket = WhoopPacketBuilder.SendHistoryEnd(metadata.Data);
                await _connectedWhoopDevice.SendCommandAsync(nextHistoryPacket);
            }

            //if (metadata.Cmd == MetadataType.HistoryStart)
            //{
            //    var nextHistoryPacket = WhoopPacketBuilder.SendHistoricalData();
            //    await _connectedWhoopDevice.SendCommandAsync(nextHistoryPacket);
            //}
            
            //Thread.Sleep(200);
            //await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.SendHistoricalData());
        }
        private async Task DisconnectCurrentDevice()
        {
            if (_connectedWhoopDevice != null)
            {
                try
                {
                    await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.AbortHistoricalTransmits());
                    await Task.Delay(500); // Wait a moment to ensure the command is sent
                    await _connectedWhoopDevice.SendCommandAsync(WhoopPacketBuilder.ExitHighFreqSync());
                    await Task.Delay(500); // Wait a moment to ensure the command is sent

                    LogToConsole($"Disconnecting from {_connectedWhoopDevice.Name}...");
                    StatusLabel.Text = $"Status: Disconnecting from {_connectedWhoopDevice.Name}...";
                    _connectedWhoopDevice.DataFromStrapReceived -= OnDataFromStrapReceivedHandler;
                    _connectedWhoopDevice.CmdFromStrapReceived -= OnCmdFromStrapReceivedHandler;
                    _connectedWhoopDevice.EventsFromStrapReceived -= OnEventsFromStrapReceivedHandler;
                    await _connectedWhoopDevice.DisconnectAsync();
                    await SaveHeartRateSamplesInBuffer();
                    LogToConsole("Disconnected.");
                    StatusLabel.Text = "Status: Disconnected. Scan for devices.";
                }
                catch (Exception e)
                {
                    LogToConsole(e.Message);
                }
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
            Debug.WriteLine(message);
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

                    _connectedWhoopDevice = new WhoopDevice(bleDevice);
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
                        Thread.Sleep(100);
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
        private async Task SaveHeartRateSamplesInBuffer()
        {
            LogToConsole($"Saved HR samples: {_hrSampleBuffer.Count}");
            if (_hrSampleBuffer.Count > 0)
            {
                _dbContext.HeartRateSamples.AddRange(_hrSampleBuffer);
                await _dbContext.SaveChangesAsync();
                _hrSampleBuffer.Clear();
            }
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
    }
}