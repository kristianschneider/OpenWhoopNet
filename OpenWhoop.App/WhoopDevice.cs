using OpenWhoop.Core.Data;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using Plugin.BLE.Abstractions;
using Plugin.BLE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OpenWhoop.App;

public class WhoopDevice : IDisposable
{
    private readonly IDevice _peripheral;
    private readonly AppDbContext _dbContext;
    private readonly IAdapter _adapter;

    private IService _whoopService;
    private ICharacteristic _cmdToStrapCharacteristic;    // Write commands to this (formerly _rxCharacteristic)
    private ICharacteristic _dataFromStrapCharacteristic; // General data, historical (formerly _txCharacteristic)
    private ICharacteristic _cmdFromStrapCharacteristic;  // Command responses
    private ICharacteristic _eventsFromStrapCharacteristic; // Specific device events (formerly _sensorCharacteristic)
                                                            // private ICharacteristic _memfaultCharacteristic; // If needed later

    public event EventHandler<byte[]> DataFromStrapReceived;
    public event EventHandler<byte[]> CmdFromStrapReceived;
    public event EventHandler<byte[]> EventsFromStrapReceived;
    // public event EventHandler<byte[]> MemfaultDataReceived; // If needed
    public event EventHandler Disconnected;


    public string Name => _peripheral.Name;
    public Guid Id => _peripheral.Id;
    public DeviceState State => _peripheral.State;

    public WhoopDevice(IDevice peripheral, AppDbContext dbContext)
    {
        _peripheral = peripheral ?? throw new ArgumentNullException(nameof(peripheral));
        _dbContext = dbContext;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDisconnected += OnDeviceDisconnectedHandler;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLostHandler;
    }

    public async Task<bool> ConnectAndBondAsync(CancellationToken cancellationToken = default)
    {
        if (State == DeviceState.Connected && _peripheral.BondState == DeviceBondState.Bonded) 
        {
            Console.WriteLine($"[WhoopDevice] {Name} is already connected and bonded.");
            return true;
        }
        if (State == DeviceState.Connected && _peripheral.BondState != DeviceBondState.Bonded)
        {
            Console.WriteLine($"[WhoopDevice] {Name} is connected but not bonded. Attempting to bond...");
        }
        else if (State != DeviceState.Connected)
        {
            Console.WriteLine($"[WhoopDevice] Connecting to {Name} (ID: {Id})...");
            try
            {
                var connectParameters = new ConnectParameters(autoConnect: false, forceBleTransport: true);
                await _adapter.ConnectToDeviceAsync(_peripheral, connectParameters, cancellationToken);
                Console.WriteLine($"[WhoopDevice] {Name} connected successfully.");
            }
            catch (DeviceConnectionException ex)
            {
                Console.WriteLine($"[WhoopDevice] Error connecting to {Name}: {ex.Message}");
                return false;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[WhoopDevice] Connection attempt to {Name} cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WhoopDevice] An unexpected error occurred while connecting to {Name}: {ex.Message}");
                return false;
            }
        }

        // Attempt to create a bond
        if (_peripheral.BondState != DeviceBondState.Bonded)
        {
            Console.WriteLine($"[WhoopDevice] Attempting to create bond with {Name}...");
            try
            {
                await _adapter.BondAsync(_peripheral);
                
                await Task.Delay(1000, cancellationToken); // Adjust delay as needed

                if (_peripheral.BondState == DeviceBondState.Bonded)
                {
                    Console.WriteLine($"[WhoopDevice] Successfully bonded with {Name}. BondState: {_peripheral.BondState}");
                }
                else
                {
                    Console.WriteLine($"[WhoopDevice] Failed to bond with {Name}, or bond attempt timed out. BondState: {_peripheral.BondState}. Continuing without guaranteed bond.");
                    // Depending on the device, not being bonded might prevent further operations.
                    // You might choose to return false here if bonding is strictly required.
                }
            }
            catch (DeviceConnectionException ex) // Catching specific exception for bonding
            {
                Console.WriteLine($"[WhoopDevice] Error creating bond with {Name}: {ex.Message}. BondState: {_peripheral.BondState}");
                // return false; // Optionally fail if bonding fails
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[WhoopDevice] Bonding attempt with {Name} cancelled.");
                // return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WhoopDevice] An unexpected error occurred during bonding with {Name}: {ex.Message}. BondState: {_peripheral.BondState}");
                // return false; // Optionally fail if bonding fails
            }
        }
        else
        {
            Console.WriteLine($"[WhoopDevice] {Name} is already bonded. BondState: {_peripheral.BondState}");
        }
        return true; // Returns true if connected, bonding result is logged
    }

    private void OnDeviceDisconnectedHandler(object sender, DeviceEventArgs e)
    {
        if (e.Device.Id == _peripheral.Id)
        {
            Console.WriteLine($"Device {Name} disconnected.");
            CleanupCharacteristicsAndSubscriptions();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnDeviceConnectionLostHandler(object sender, DeviceErrorEventArgs e)
    {
        if (e.Device.Id == _peripheral.Id)
        {
            Console.WriteLine($"Connection lost to device {Name}. Error: {e.ErrorMessage}");
            CleanupCharacteristicsAndSubscriptions();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (State == DeviceState.Connected)
        {
            Console.WriteLine($"{Name} is already connected.");
            return true;
        }

        try
        {
            Console.WriteLine($"Connecting to {Name} (ID: {Id})...");
            var connectParameters = new ConnectParameters(autoConnect: false, forceBleTransport: true);
            await _adapter.ConnectToDeviceAsync(_peripheral, connectParameters, cancellationToken);
            Console.WriteLine($"{Name} connected successfully.");
            return true;
        }
        catch (DeviceConnectionException ex)
        {
            Console.WriteLine($"Error connecting to {Name}: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Connection attempt to {Name} cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred while connecting to {Name}: {ex.Message}");
        }
        return false;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (State != DeviceState.Connected)
        {
            Console.WriteLine($"{Name} is not connected. Cannot initialize.");
            return false;
        }

        try
        {
            Console.WriteLine($"Initializing {Name}: Discovering services and characteristics...");
            _whoopService = await _peripheral.GetServiceAsync(WhoopConstants.WhoopServiceGuid, cancellationToken);
            if (_whoopService == null)
            {
                Console.WriteLine($"Whoop service ({WhoopConstants.WhoopServiceGuid}) not found on {Name}.");
                return false;
            }
            Console.WriteLine("Whoop service found.");

            _cmdToStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.CmdToStrapCharacteristicGuid);
            _dataFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.DataFromStrapCharacteristicGuid);
            _cmdFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.CmdFromStrapCharacteristicGuid);
            _eventsFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.EventsFromStrapCharacteristicGuid);
            // _memfaultCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.MemfaultCharacteristicGuid, cancellationToken);


            bool allFound = true;
            if (_cmdToStrapCharacteristic == null) { Console.WriteLine("CMD_TO_STRAP characteristic not found."); allFound = false; }
            if (_dataFromStrapCharacteristic == null) { Console.WriteLine("DATA_FROM_STRAP characteristic not found."); allFound = false; }
            if (_cmdFromStrapCharacteristic == null) { Console.WriteLine("CMD_FROM_STRAP characteristic not found."); allFound = false; }
            if (_eventsFromStrapCharacteristic == null) { Console.WriteLine("EVENTS_FROM_STRAP characteristic not found."); allFound = false; }
            // if (_memfaultCharacteristic == null) { Console.WriteLine("MEMFAULT characteristic not found."); /* Optional? */ }


            if (!allFound)
            {
                Console.WriteLine("One or more required characteristics were not found.");
                return false;
            }
            Console.WriteLine("All required characteristics found.");

            // Subscribe to notifications
            await SubscribeToCharacteristic(_dataFromStrapCharacteristic, OnDataFromStrapValueUpdated, "DATA_FROM_STRAP", cancellationToken);
            await SubscribeToCharacteristic(_cmdFromStrapCharacteristic, OnCmdFromStrapValueUpdated, "CMD_FROM_STRAP", cancellationToken);
            await SubscribeToCharacteristic(_eventsFromStrapCharacteristic, OnEventsFromStrapValueUpdated, "EVENTS_FROM_STRAP", cancellationToken);
            // await SubscribeToCharacteristic(_memfaultCharacteristic, OnMemfaultValueUpdated, "MEMFAULT", cancellationToken);

            Console.WriteLine($"{Name} initialized successfully.");
            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Initialization of {Name} cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing {Name}: {ex.Message}");
        }
        return false;
    }

    private async Task SubscribeToCharacteristic(ICharacteristic characteristic, EventHandler<CharacteristicUpdatedEventArgs> handler, string name, CancellationToken cancellationToken)
    {
        if (characteristic != null && characteristic.CanUpdate)
        {
            Console.WriteLine($"[WhoopDevice] Attempting to subscribe to {name} (ID: {characteristic.Id}, CanUpdate: {characteristic.CanUpdate})");
            try
            {
                characteristic.ValueUpdated += handler; // Attach our internal handler
                await characteristic.StartUpdatesAsync(cancellationToken);
                Console.WriteLine($"[WhoopDevice] Successfully called StartUpdatesAsync for {name}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WhoopDevice] Error subscribing to {name} characteristic: {ex.Message}");
            }
        }
        else if (characteristic != null)
        {
            Console.WriteLine($"[WhoopDevice] Cannot subscribe to {name}: CanRead={characteristic.CanRead}, CanWrite={characteristic.CanWrite}, CanUpdate={characteristic.CanUpdate}");
        }
        else
        {
            Console.WriteLine($"[WhoopDevice] Cannot subscribe to {name}: characteristic is null.");
        }
    }

    private async Task UnsubscribeFromCharacteristic(ICharacteristic characteristic, EventHandler<CharacteristicUpdatedEventArgs> handler, string name)
    {
        if (characteristic != null)
        {
            characteristic.ValueUpdated -= handler;
            await characteristic.StopUpdatesAsync();
            Console.WriteLine($"Unsubscribed from {name} characteristic notifications.");
        }
    }

    private void OnDataFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        string message = $"[WhoopDevice INTERNAL RAW DATA_FROM_STRAP]: {BitConverter.ToString(args.Characteristic.Value)} (From Char: {args.Characteristic.Id})";
        Console.WriteLine(message); // To MAUI app console
        Debug.WriteLine(message);   // To Visual Studio Debug Output

        DataFromStrapReceived?.Invoke(this, args.Characteristic.Value);
    }

    private void OnCmdFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        string message = $"[WhoopDevice INTERNAL RAW CMD_FROM_STRAP]: {BitConverter.ToString(args.Characteristic.Value)} (From Char: {args.Characteristic.Id})";
        Console.WriteLine(message);
        Debug.WriteLine(message);

        CmdFromStrapReceived?.Invoke(this, args.Characteristic.Value);
    }

    private void OnEventsFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        string message = $"[WhoopDevice INTERNAL RAW EVENTS_FROM_STRAP]: {BitConverter.ToString(args.Characteristic.Value)} (From Char: {args.Characteristic.Id})";
        Console.WriteLine(message);
        Debug.WriteLine(message);

        EventsFromStrapReceived?.Invoke(this, args.Characteristic.Value);
    }

    public async Task<bool> SendCommandAsync(byte[] commandData, CancellationToken cancellationToken = default)
    {
        if (State != DeviceState.Connected || _cmdToStrapCharacteristic == null)
        {
            Console.WriteLine($"{Name} is not connected or CMD_TO_STRAP characteristic is not initialized.");
            return false;
        }

        if (!_cmdToStrapCharacteristic.CanWrite)
        {
            Console.WriteLine("CMD_TO_STRAP characteristic does not support writes.");
            return false;
        }

        try
        {
            // Console.WriteLine($"Sending command via CMD_TO_STRAP: {BitConverter.ToString(commandData)}");
            bool success = await _cmdToStrapCharacteristic.WriteAsync(commandData, cancellationToken) == 0;
            if (!success)
            {
                Console.WriteLine("Failed to send command (WriteAsync returned false).");
            }
            return success;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Sending command to {Name} cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending command to {Name}: {ex.Message}");
        }
        return false;
    }

    public async Task DisconnectAsync()
    {
        if (State == DeviceState.Disconnected || State == DeviceState.Limited)
        {
            Console.WriteLine($"{Name} is already disconnected or connection is limited.");
            return;
        }
        try
        {
            Console.WriteLine($"Disconnecting from {Name}...");
            await UnsubscribeFromCharacteristic(_dataFromStrapCharacteristic, OnDataFromStrapValueUpdated, "DATA_FROM_STRAP");
            await UnsubscribeFromCharacteristic(_cmdFromStrapCharacteristic, OnCmdFromStrapValueUpdated, "CMD_FROM_STRAP");
            await UnsubscribeFromCharacteristic(_eventsFromStrapCharacteristic, OnEventsFromStrapValueUpdated, "EVENTS_FROM_STRAP");
            // await UnsubscribeFromCharacteristic(_memfaultCharacteristic, OnMemfaultValueUpdated, "MEMFAULT");


            await _adapter.DisconnectDeviceAsync(_peripheral);
            Console.WriteLine($"{Name} disconnected by request.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disconnecting from {Name}: {ex.Message}");
        }
        finally
        {
            // Ensure characteristics are nulled even if unsubscription fails
            CleanupCharacteristicsAndSubscriptions(false); // Don't remove adapter event handlers here
        }
    }

    private void CleanupCharacteristicsAndSubscriptions(bool removeAdapterHandlers = true)
    {
        // Unsubscribe from characteristic events directly to avoid issues if StopUpdatesAsync failed or wasn't called
        if (_dataFromStrapCharacteristic != null) _dataFromStrapCharacteristic.ValueUpdated -= OnDataFromStrapValueUpdated;
        if (_cmdFromStrapCharacteristic != null) _cmdFromStrapCharacteristic.ValueUpdated -= OnCmdFromStrapValueUpdated;
        if (_eventsFromStrapCharacteristic != null) _eventsFromStrapCharacteristic.ValueUpdated -= OnEventsFromStrapValueUpdated;
        // if (_memfaultCharacteristic != null) _memfaultCharacteristic.ValueUpdated -= OnMemfaultValueUpdated;

        _cmdToStrapCharacteristic = null;
        _dataFromStrapCharacteristic = null;
        _cmdFromStrapCharacteristic = null;
        _eventsFromStrapCharacteristic = null;
        // _memfaultCharacteristic = null;
        _whoopService = null;

        if (removeAdapterHandlers)
        {
            _adapter.DeviceDisconnected -= OnDeviceDisconnectedHandler;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLostHandler;
        }
    }

    public void Dispose()
    {
        Console.WriteLine($"Disposing WhoopDevice {Name}...");
        // Unsubscribe from adapter events first
        _adapter.DeviceDisconnected -= OnDeviceDisconnectedHandler;
        _adapter.DeviceConnectionLost -= OnDeviceConnectionLostHandler;

        // Attempt to gracefully disconnect and unsubscribe from characteristics
        if (State == DeviceState.Connected)
        {
            // Run synchronously for dispose, but with a timeout.
            // Consider if this should be fully async and if Dispose can be async.
            Task.Run(async () => await DisconnectAsync()).Wait(TimeSpan.FromSeconds(5));
        }

        // Final cleanup of event subscriptions and characteristic references
        CleanupCharacteristicsAndSubscriptions(false); // Adapter handlers already removed

        Console.WriteLine($"WhoopDevice {Name} disposed.");
    }
}
