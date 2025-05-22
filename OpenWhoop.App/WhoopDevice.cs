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
using OpenWhoop.App.Protocol;
using System.Threading;
using OpenWhoop.Core.Entities;

namespace OpenWhoop.App;

public class WhoopDevice : IDisposable
{
    private readonly IDevice _peripheral;
    private readonly IAdapter _adapter;

    private IService _whoopService;
    private ICharacteristic _cmdToStrapCharacteristic;
    private ICharacteristic _dataFromStrapCharacteristic;
    private ICharacteristic _cmdFromStrapCharacteristic;
    private ICharacteristic _eventsFromStrapCharacteristic;
    private ICharacteristic _memfaultCharacteristicGuid;

    public event EventHandler<ParsedWhoopPacket> DataFromStrapReceived;
    public event EventHandler<ParsedWhoopPacket> CmdFromStrapReceived;
    public event EventHandler<ParsedWhoopPacket> EventsFromStrapReceived;
    public event EventHandler Disconnected;

    public string Name => _peripheral.Name;
    public Guid Id => _peripheral.Id;
    public DeviceState State => _peripheral.State;
    public DeviceBondState BondState => _peripheral.BondState;

    public WhoopDevice(IDevice peripheral)
    {
        _peripheral = peripheral ?? throw new ArgumentNullException(nameof(peripheral));
        _adapter = CrossBluetoothLE.Current.Adapter;

        Console.WriteLine($"[WhoopDevice] Attaching DeviceDisconnected and DeviceConnectionLost handlers for {Name}");
        _adapter.DeviceDisconnected += OnDeviceDisconnectedHandler;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLostHandler;
    }
    private void OnDeviceDisconnectedHandler(object sender, DeviceEventArgs e)
    {
        if (e.Device.Id == _peripheral.Id)
        {
            Console.WriteLine($"[WhoopDevice] Device {Name} disconnected.");
            CleanupCharacteristicsAndSubscriptions();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnDeviceConnectionLostHandler(object sender, DeviceErrorEventArgs e)
    {
        if (e.Device.Id == _peripheral.Id)
        {
            Console.WriteLine($"[WhoopDevice] Connection lost to device {Name}. Error: {e.ErrorMessage}");
            CleanupCharacteristicsAndSubscriptions();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
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

        if (_peripheral.BondState != DeviceBondState.Bonded)
        {
            Console.WriteLine($"[WhoopDevice] Attempting to create bond with {Name}...");
            try
            {
                await _adapter.BondAsync(_peripheral);

                await Task.Delay(1000, cancellationToken); // Give time for bond state to update

                if (_peripheral.BondState == DeviceBondState.Bonded)
                {
                    Console.WriteLine($"[WhoopDevice] Successfully bonded with {Name}. BondState: {_peripheral.BondState}");
                }
                else
                {
                    Console.WriteLine($"[WhoopDevice] Failed to bond with {Name}, or bond attempt timed out. BondState: {_peripheral.BondState}. Continuing without guaranteed bond.");
                }
            }
            catch (DeviceConnectionException ex)
            {
                Console.WriteLine($"[WhoopDevice] Error creating bond with {Name}: {ex.Message}. BondState: {_peripheral.BondState}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[WhoopDevice] Bonding attempt with {Name} cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WhoopDevice] An unexpected error occurred during bonding with {Name}: {ex.Message}. BondState: {_peripheral.BondState}");
            }
        }
        else
        {
            Console.WriteLine($"[WhoopDevice] {Name} is already bonded. BondState: {_peripheral.BondState}");
        }
        return State == DeviceState.Connected;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (State != DeviceState.Connected)
        {
            Console.WriteLine($"[WhoopDevice] {Name} is not connected. Cannot initialize.");
            return false;
        }
        if (_peripheral.BondState != DeviceBondState.Bonded)
        {
            Console.WriteLine($"[WhoopDevice] {Name} is not bonded. Certain characteristics might not be available or work correctly.");
            // Depending on strictness, you might return false here.
        }

        try
        {
            Console.WriteLine($"[WhoopDevice] Initializing {Name}: Discovering services and characteristics...");
            _whoopService = await _peripheral.GetServiceAsync(WhoopConstants.WhoopServiceGuid, cancellationToken);
            if (_whoopService == null)
            {
                Console.WriteLine($"[WhoopDevice] Whoop service ({WhoopConstants.WhoopServiceGuid}) not found on {Name}.");
                return false;
            }
            Console.WriteLine("[WhoopDevice] Whoop service found.");

            _cmdToStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.CmdToStrapCharacteristicGuid);
            _dataFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.DataFromStrapCharacteristicGuid);
            _cmdFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.CmdFromStrapCharacteristicGuid);
            _eventsFromStrapCharacteristic = await _whoopService.GetCharacteristicAsync(WhoopConstants.EventsFromStrapCharacteristicGuid);
            _memfaultCharacteristicGuid = await _whoopService.GetCharacteristicAsync(WhoopConstants.MemfaultCharacteristicGuid);

            bool allFound = true;
            if (_cmdToStrapCharacteristic == null) { Console.WriteLine("[WhoopDevice] CMD_TO_STRAP characteristic not found."); allFound = false; }
            if (_dataFromStrapCharacteristic == null) { Console.WriteLine("[WhoopDevice] DATA_FROM_STRAP characteristic not found."); allFound = false; }
            if (_cmdFromStrapCharacteristic == null) { Console.WriteLine("[WhoopDevice] CMD_FROM_STRAP characteristic not found."); allFound = false; }
            if (_eventsFromStrapCharacteristic == null) { Console.WriteLine("[WhoopDevice] EVENTS_FROM_STRAP characteristic not found."); allFound = false; }

            if (!allFound)
            {
                Console.WriteLine("[WhoopDevice] One or more required characteristics were not found.");
                return false;
            }
            Console.WriteLine("[WhoopDevice] All required characteristics found.");

            await SubscribeToCharacteristic(_dataFromStrapCharacteristic, OnDataFromStrapValueUpdated, "DATA_FROM_STRAP", cancellationToken);
            await SubscribeToCharacteristic(_cmdFromStrapCharacteristic, OnCmdFromStrapValueUpdated, "CMD_FROM_STRAP", cancellationToken);
            await SubscribeToCharacteristic(_eventsFromStrapCharacteristic, OnEventsFromStrapValueUpdated, "EVENTS_FROM_STRAP", cancellationToken);
            await SubscribeToCharacteristic(_memfaultCharacteristicGuid, OnMemFaultFromStrapValueUpdated, "MF_FROM_STRAP", cancellationToken);

            // --- Add EnterHighFreqSync command ---
            Debug.WriteLine("[WhoopDevice] Sending start commands...");
            await SendCommandAsync(WhoopPacketBuilder.GetHelloHarvard(), cancellationToken);
            await SendCommandAsync(WhoopPacketBuilder.SetTime(), cancellationToken);
            await SendCommandAsync(WhoopPacketBuilder.GetName(), cancellationToken);
            bool sentHighFreqSync = await SendCommandAsync(WhoopPacketBuilder.EnterHighFreqSync(), cancellationToken);
            if (sentHighFreqSync)
            {
                Console.WriteLine("[WhoopDevice] EnterHighFreqSync command sent successfully.");
            }
            else
            {
                Console.WriteLine("[WhoopDevice] Failed to send EnterHighFreqSync command.");
                // You might decide if this is a critical failure or not.
            }

            Console.WriteLine($"[WhoopDevice] {Name} initialized successfully.");
            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[WhoopDevice] Initialization of {Name} cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WhoopDevice] Error initializing {Name}: {ex.Message}");
        }
        return false;
    }

    private void OnCharacteristicValueUpdated(CharacteristicUpdatedEventArgs args, Action<ParsedWhoopPacket> raiseEvent, string characteristicName)
    {
        string rawDataHex = BitConverter.ToString(args.Characteristic.Value);
        //Console.WriteLine("----------Receiving  " + string.Join(",", args.Characteristic.Value.Select(b => b.ToString())));


        if (ParsedWhoopPacket.TryParse(args.Characteristic.Value, out var parsedPacket))
        {
            // Successfully parsed and CRCs are valid
            //if (parsedPacket.PacketType == PacketType.CommandResponse)
            //{
            //    Console.WriteLine($"Type={parsedPacket.PacketType}, Cmd/Evt={(CommandNumber)parsedPacket.CommandOrEventNumber}, Seq={parsedPacket.Sequence:X2}, PayloadLen={parsedPacket.Payload.Length}");
            //}
            //else if (parsedPacket.PacketType == PacketType.Event)
            //{
            //    Console.WriteLine($"Type={parsedPacket.PacketType}, Cmd/Evt={(EventNumber)parsedPacket.CommandOrEventNumber}, Seq={parsedPacket.Sequence:X2}, PayloadLen={parsedPacket.Payload.Length}");
            //}
            //else
            //{
            //    Console.WriteLine($"Type={parsedPacket.PacketType}, Cmd/Evt={parsedPacket.CommandOrEventNumber:X2}, Seq={parsedPacket.Sequence:X2}, PayloadLen={parsedPacket.Payload.Length}");
            //}
            //Debug.WriteLine($"[WhoopDevice PARSED OK {characteristicName}]: Type={parsedPacket.PacketType}, Cmd/Evt={parsedPacket.CommandOrEventNumber:X2}, Seq={parsedPacket.Sequence:X2}, PayloadLen={parsedPacket.Payload.Length}");
            raiseEvent?.Invoke(parsedPacket);
        }
        else
        {
            // Parsing failed or CRCs are invalid
            Console.WriteLine($"[WhoopDevice PARSE FAILED {characteristicName}]: Error={parsedPacket.Error}, Msg='{parsedPacket.ErrorMessage}'. Raw={rawDataHex}");
            Debug.WriteLine($"[WhoopDevice PARSE FAILED {characteristicName}]: Error={parsedPacket.Error}, Msg='{parsedPacket.ErrorMessage}'. Raw={rawDataHex}");
            // Optionally, raise a different event for parse errors or pass the partially parsed packet
        }
    }

    private void OnDataFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        OnCharacteristicValueUpdated(args, p => DataFromStrapReceived?.Invoke(this, p), "DATA_FROM_STRAP");
    }

    private void OnCmdFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        OnCharacteristicValueUpdated(args, p => CmdFromStrapReceived?.Invoke(this, p), "CMD_FROM_STRAP");
    }

    private void OnEventsFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        OnCharacteristicValueUpdated(args, p => EventsFromStrapReceived?.Invoke(this, p), "EVENTS_FROM_STRAP");
    }

    private void OnMemFaultFromStrapValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
    {
        OnCharacteristicValueUpdated(args, p => { }, "MF_FROM_STRAP");
    }
    private async Task SubscribeToCharacteristic(ICharacteristic characteristic, EventHandler<CharacteristicUpdatedEventArgs> handler, string name, CancellationToken cancellationToken)
    {
        if (characteristic != null && characteristic.CanUpdate)
        {
            Console.WriteLine($"[WhoopDevice] Attempting to subscribe to {name} (ID: {characteristic.Id}, CanUpdate: {characteristic.CanUpdate})");
            try
            {
                characteristic.ValueUpdated += handler;
                await characteristic.StartUpdatesAsync(cancellationToken);
                Console.WriteLine($"[WhoopDevice] Successfully called StartUpdatesAsync for {name}");
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
            try
            {
                characteristic.ValueUpdated -= handler;
                await characteristic.StopUpdatesAsync();
                Console.WriteLine($"[WhoopDevice] Unsubscribed from {name} characteristic notifications.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WhoopDevice] Error unsubscribing from {name}: {ex.Message}");
            }
        }
    }

    public async Task<bool> SendCommandAsync(byte[] commandData, CancellationToken cancellationToken = default)
    {
        if (State != DeviceState.Connected || _cmdToStrapCharacteristic == null)
        {
            Console.WriteLine($"[WhoopDevice] {Name} is not connected or CMD_TO_STRAP characteristic is not initialized.");
            return false;
        }
        if (!_cmdToStrapCharacteristic.CanWrite)
        {
            Console.WriteLine("[WhoopDevice] CMD_TO_STRAP characteristic does not support writes.");
            return false;
        }
        try
        {

            Console.WriteLine("----------Sending  " + string.Join(",", commandData.Select(b => b.ToString())));
            bool success = await _cmdToStrapCharacteristic.WriteAsync(commandData, cancellationToken) == 0;
            Thread.Sleep(50);
            if (!success)
            {
                Console.WriteLine("[WhoopDevice] Failed to send command (WriteAsync returned false).");
            }
            return success;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[WhoopDevice] Sending command to {Name} cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WhoopDevice] Error sending command to {Name}: {ex.Message}");
        }
        return false;
    }

    public async Task DisconnectAsync()
    {
        if (State == DeviceState.Disconnected || State == DeviceState.Limited)
        {
            Console.WriteLine($"[WhoopDevice] {Name} is already disconnected or connection is limited.");
            return;
        }
        try
        {
            Console.WriteLine($"[WhoopDevice] Disconnecting from {Name}...");
            await UnsubscribeFromCharacteristic(_dataFromStrapCharacteristic, OnDataFromStrapValueUpdated, "DATA_FROM_STRAP");
            await UnsubscribeFromCharacteristic(_cmdFromStrapCharacteristic, OnCmdFromStrapValueUpdated, "CMD_FROM_STRAP");
            await UnsubscribeFromCharacteristic(_eventsFromStrapCharacteristic, OnEventsFromStrapValueUpdated, "EVENTS_FROM_STRAP");

            if (_peripheral.State != DeviceState.Disconnected)
            {
                await _adapter.DisconnectDeviceAsync(_peripheral);
            }
            Console.WriteLine($"[WhoopDevice] {Name} disconnected by request.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WhoopDevice] Error disconnecting from {Name}: {ex.Message}");
        }
        finally
        {
            CleanupCharacteristicsAndSubscriptions(false);
        }
    }

    private void CleanupCharacteristicsAndSubscriptions(bool removeAdapterHandlers = true)
    {
        if (_dataFromStrapCharacteristic != null) _dataFromStrapCharacteristic.ValueUpdated -= OnDataFromStrapValueUpdated;
        if (_cmdFromStrapCharacteristic != null) _cmdFromStrapCharacteristic.ValueUpdated -= OnCmdFromStrapValueUpdated;
        if (_eventsFromStrapCharacteristic != null) _eventsFromStrapCharacteristic.ValueUpdated -= OnEventsFromStrapValueUpdated;

        _cmdToStrapCharacteristic = null;
        _dataFromStrapCharacteristic = null;
        _cmdFromStrapCharacteristic = null;
        _eventsFromStrapCharacteristic = null;
        _whoopService = null;

        if (removeAdapterHandlers)
        {
            _adapter.DeviceDisconnected -= OnDeviceDisconnectedHandler;
            _adapter.DeviceConnectionLost -= OnDeviceConnectionLostHandler;
        }
    }

    public void Dispose()
    {
        Console.WriteLine($"[WhoopDevice] Disposing WhoopDevice {Name}...");
        _adapter.DeviceDisconnected -= OnDeviceDisconnectedHandler;
        _adapter.DeviceConnectionLost -= OnDeviceConnectionLostHandler;

        if (State == DeviceState.Connected)
        {
            Task.Run(async () => await DisconnectAsync()).Wait(TimeSpan.FromSeconds(2)); // Shorter timeout for dispose
        }
        CleanupCharacteristicsAndSubscriptions(false);
        Console.WriteLine($"[WhoopDevice] WhoopDevice {Name} disposed.");
    }
}