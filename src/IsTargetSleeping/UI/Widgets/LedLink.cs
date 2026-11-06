using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IsTargetSleeping.UI;

/// La conexión Bluetooth de baja energía con una tira LED: se conecta sola al mandarle algo, se reconecta si se cae y manda las órdenes de una en una,
/// quedándose solo con la última de cada tipo (arrastrar un control manda decenas de valores por segundo: a la tira solo le llega el último).
public sealed class LedLink : IDisposable
{
    private static readonly Guid Service = Guid.Parse(LedProtocol.ServiceUuid), Characteristic = Guid.Parse(LedProtocol.WriteUuid);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, byte[]> pending = [];
    private readonly object pendingLock = new();
    private BluetoothLEDevice? device;
    private GattCharacteristic? write;
    private bool pumping, disposed;

    public ulong Address { get; }
    public LedFamily Family { get; }
    public bool Connected => write is not null && device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    /// Lo último que dijo la conexión: «conectando…», «no se encuentra»… (vacío si todo va bien).
    public string Problem { get; private set; } = "";
    public event Action? StateChanged;

    public LedLink(ulong address, LedFamily family) { Address = address; Family = family; }

    public async Task<bool> ConnectAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (Connected) return true;
            write = null;
            device?.Dispose();
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(Address);
            if (device is null) { Fail("no se encuentra"); return false; }
            device.ConnectionStatusChanged += (_, _) => StateChanged?.Invoke();
            // Windows tarda en abrir la conexión con una tira dormida: se reintenta un par de veces antes de rendirse.
            GattDeviceService? service = null;
            for (int attempt = 0; attempt < 3 && service is null; attempt++)
            {
                var services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                service = services.Status == GattCommunicationStatus.Success ? services.Services.FirstOrDefault(x => x.Uuid == Service) : null;
                if (service is null) await Task.Delay(700);
            }
            if (service is null) { Fail("no responde"); return false; }
            var chars = await service.GetCharacteristicsForUuidAsync(Characteristic, BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) { Fail("no es una tira compatible"); return false; }
            write = chars.Characteristics[0];
            Problem = "";
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception e) { AppLog.Write($"luces: conectar {Address:X12}: {e.Message}"); Fail("no se pudo conectar"); return false; }
        finally { gate.Release(); }
    }

    private void Fail(string problem) { Problem = problem; StateChanged?.Invoke(); }

    /// Deja una orden en cola con su clave («color», «brillo»…): si ya había otra con esa clave sin mandar, se sustituye.
    public void Post(string key, byte[] frame)
    {
        lock (pendingLock) pending[key] = frame;
        if (pumping) return;
        pumping = true;
        _ = Pump();
    }

    private async Task Pump()
    {
        try
        {
            while (!disposed)
            {
                (string Key, byte[] Frame)[] batch;
                lock (pendingLock) { batch = pending.Select(p => (p.Key, p.Value)).ToArray(); pending.Clear(); }
                if (batch.Length == 0) break;
                foreach (var (_, frame) in batch)
                {
                    if (!await SendAsync(frame)) break;
                    await Task.Delay(35);   // la tira pierde órdenes si llegan más deprisa
                }
                await Task.Delay(40);
            }
        }
        finally { pumping = false; lock (pendingLock) { if (pending.Count > 0 && !disposed) { pumping = true; _ = Pump(); } } }
    }

    private async Task<bool> SendAsync(byte[] frame)
    {
        if (!Connected && !await ConnectAsync()) return false;
        try
        {
            var characteristic = write;
            if (characteristic is null) return false;
            using var writer = new DataWriter();
            writer.WriteBytes(frame);
            var status = await characteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            if (status == GattCommunicationStatus.Success) return true;
            write = null;   // la próxima orden reconecta
            Fail("se perdió la conexión");
            return false;
        }
        catch (Exception e) { AppLog.Write($"luces: enviar {Address:X12}: {e.Message}"); write = null; Fail("se perdió la conexión"); return false; }
    }

    public void Disconnect()
    {
        write = null;
        device?.Dispose();
        device = null;
        StateChanged?.Invoke();
    }

    public void Dispose() { disposed = true; Disconnect(); }
}

/// Busca tiras LED cercanas con la enumeración Bluetooth LE de Windows y se queda con los nombres ELK o MELK.
public static class LedScanner
{
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string PresentKey = "System.Devices.Aep.IsPresent";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string NameKey = "System.ItemNameDisplay";

    public static async Task<List<(ulong Address, string Name, int Rssi)>> ScanAsync(int seconds = 45)
    {
        var found = new Dictionary<ulong, (string Name, int Rssi)>();
        var known = new Dictionary<string, DeviceInformation>();
        var gate = new object();
        void Record(ulong address, string? name, int rssi)
        {
            if (!LedProtocol.IsLedName(name)) return;
            lock (gate) found[address] = (name!, rssi);
        }

        DeviceWatcher? devices = null;
        try
        {
            devices = DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
                [AddressKey, PresentKey, ConnectedKey, NameKey], DeviceInformationKind.AssociationEndpoint);
            devices.Added += (_, info) =>
            {
                lock (gate) known[info.Id] = info;
                RecordDevice(info);
            };
            devices.Updated += (_, update) =>
            {
                DeviceInformation? info;
                lock (gate)
                {
                    if (!known.TryGetValue(update.Id, out info)) return;
                    info.Update(update);
                }
                RecordDevice(info);
            };
            devices.Start();
        }
        catch (Exception e) { AppLog.Write($"luces: enumerar dispositivos BLE: {e.Message}"); }

        await Task.Delay(seconds * 1000);
        if (devices?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) devices.Stop();
        lock (gate) return found.Select(f => (f.Key, f.Value.Name, f.Value.Rssi)).OrderByDescending(f => f.Rssi).ToList();

        void RecordDevice(DeviceInformation info)
        {
            if (!info.Properties.TryGetValue(PresentKey, out var present) || present is not true
                || !info.Properties.TryGetValue(AddressKey, out var rawAddress)
                || !LedDevice.TryParseBluetoothAddress(rawAddress as string, out var address)) return;
            Record(address, info.Name, -100);
        }
    }
}
