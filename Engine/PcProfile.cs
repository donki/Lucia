using Microsoft.Win32;

namespace SocLucia.Engine;

/// <summary>
/// Lo que da de si este PC para una IA local: procesador, memoria y grafica con su memoria dedicada.
/// Con ello el catalogo marca las IA optimas (caben enteras en la grafica, o son ligeras para el
/// procesador) y avisa de las que iran lentas. Todo sale del registro: sin drivers ni herramientas.
/// </summary>
public sealed record PcProfile(string Cpu, int Cores, long RamBytes, string? Gpu, long VramBytes)
{
    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>Una grafica cuenta si el motor la usa (CUDA/Vulkan) y tiene memoria dedicada de verdad (las integradas anuncian 512 MB).</summary>
    public bool HasUsableGpu => VramBytes >= 2 * GiB;

    public static PcProfile Detect(string accelerator)
    {
        var cpu = "CPU";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0)
                cpu = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ");
        }
        catch (Exception) { }

        string? gpu = null;
        long vram = 0;
        if (accelerator != "cpu")
        {
            try
            {
                // Clase de adaptadores de pantalla: cada 000N es una grafica con su memoria dedicada en qwMemorySize.
                using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                foreach (var sub in cls?.GetSubKeyNames() ?? [])
                {
                    if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                    using var key = cls!.OpenSubKey(sub);
                    if (key?.GetValue("DriverDesc") is not string desc) continue;
                    var size = key.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long l => l,
                        int i => (uint)i,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0L,
                    };
                    if (size > vram) { vram = size; gpu = desc; }
                }
            }
            catch (Exception) { }
        }
        return new PcProfile(cpu, Environment.ProcessorCount, ModelCatalog.TotalRamBytes(), gpu, vram);
    }
}

/// <summary>Como le ira una IA del catalogo en este PC.</summary>
public enum ModelFit
{
    /// <summary>Cabe entera en la grafica (o es ligera para el procesador): rapida y sin sorpresas.</summary>
    Optimal,
    /// <summary>Funciona, pero va mas lenta: parte del modelo queda fuera de la grafica.</summary>
    Ok,
    /// <summary>Cabe en memoria pero ira despacio.</summary>
    Slow,
    /// <summary>No cabe en la memoria del PC.</summary>
    TooBig,
}
