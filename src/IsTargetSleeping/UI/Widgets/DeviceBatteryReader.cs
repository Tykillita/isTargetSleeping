using Windows.Devices.Enumeration.Pnp;

namespace IsTargetSleeping.UI;

/// Lee de Windows la batería de los accesorios Bluetooth que informan de su nivel (ratón, teclado, auriculares, mando…): es una propiedad del propio
/// dispositivo que Windows rellena para los de baja energía y para los de manos libres. No abre conexiones ni pide permisos.
public static class DeviceBatteryReader
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly Dictionary<string, string> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{4d36e96f-e325-11ce-b511-08002be1ed7a}"] = "Mouse",
        ["{4d36e96b-e325-11ce-b511-08002be1ed7a}"] = "Keyboard",
        ["{745a17a0-74d3-11d0-b6fe-00a0c90f57da}"] = "HIDClass",
        ["{4d36e96c-e325-11ce-b511-08002be1ed7a}"] = "MEDIA",
    };

    public static async Task<List<DeviceBattery>> ReadAsync()
    {
        string[] props = [BatteryKey, "System.ItemNameDisplay", "System.Devices.ContainerId", "System.Devices.ClassGuid"];
        IReadOnlyList<PnpObject> all;
        try { all = await PnpObject.FindAllAsync(PnpObjectType.Device, props, "System.Devices.Present:=System.StructuredQueryType.Boolean#True"); }
        catch { all = await PnpObject.FindAllAsync(PnpObjectType.Device, props); }

        string Name(PnpObject o) => o.Properties.TryGetValue("System.ItemNameDisplay", out var v) ? v?.ToString() ?? "" : "";
        Guid Container(PnpObject o) => o.Properties.TryGetValue("System.Devices.ContainerId", out var v) && v is Guid g ? g : Guid.Empty;
        string Class(PnpObject o) => o.Properties.TryGetValue("System.Devices.ClassGuid", out var v) && v is Guid g && Classes.TryGetValue($"{{{g}}}", out var name) ? name : "";

        var byContainer = all.Where(o => Container(o) != Guid.Empty).ToLookup(Container);
        var found = new List<DeviceBattery>();
        foreach (var o in all)
        {
            if (!o.Properties.TryGetValue(BatteryKey, out var raw) || raw is null) continue;
            int percent;
            try { percent = Convert.ToInt32(raw); } catch { continue; }
            var name = DeviceKinds.CleanName(Name(o));
            var siblings = byContainer[Container(o)].Where(s => s != o).ToList();
            var kind = DeviceKinds.Classify(name, siblings.Select(Name), siblings.Select(Class).Where(c => c.Length > 0), handsFree: o.Id.Contains("0000111E", StringComparison.OrdinalIgnoreCase));
            found.Add(new DeviceBattery(o.Id, name, kind, percent));
        }
        return DeviceKinds.Merge(found);
    }
}
