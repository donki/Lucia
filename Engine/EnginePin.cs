using System.IO;
using System.Runtime.InteropServices;

namespace SocAiChat.Engine;

/// <summary>
/// El motor es <c>llama-server</c> de llama.cpp (MIT), en la version fijada aqui: se descarga de
/// las releases oficiales de GitHub la primera vez, se comprueba su SHA-256 y se desempaqueta en
/// <c>engine\&lt;build&gt;-&lt;acelerador&gt;</c>. No va dentro del instalador: son decenas de megas
/// por acelerador y asi el exe se queda pequeño. Subir de version es cambiar estas constantes.
/// </summary>
public static class EnginePin
{
    /// <summary>Etiqueta de la release de llama.cpp que aloja los zips.</summary>
    public const string Build = "b10621";

    public sealed record Archive(string Accelerator, string Url, string Sha256, string FileName, Archive? Companion = null);

    private const string Base = "https://github.com/ggml-org/llama.cpp/releases/download/" + Build + "/";

    /// <summary>Windows x64 con cualquier GPU que hable Vulkan (NVIDIA, AMD, Intel).</summary>
    public static readonly Archive WinVulkanX64 = new("vulkan", Base + "llama-" + Build + "-bin-win-vulkan-x64.zip",
        "2672d85bf87c8280d94dee01eb6a86280046878f70a07d786a93637fa9081163", "llama-" + Build + "-bin-win-vulkan-x64.zip");

    /// <summary>Windows x64 con NVIDIA: mas rapido que Vulkan. Necesita el zip de cudart al lado.</summary>
    public static readonly Archive WinCudaX64 = new("cuda", Base + "llama-" + Build + "-bin-win-cuda-12.4-x64.zip",
        "81c2ff62e14b549cd5c766ccdd5c61f09e821a171655c3047bdccfddc2d1a1e2", "llama-" + Build + "-bin-win-cuda-12.4-x64.zip",
        new Archive("cudart", Base + "cudart-llama-bin-win-cuda-12.4-x64.zip",
            "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6", "cudart-llama-bin-win-cuda-12.4-x64.zip"));

    /// <summary>Windows en ARM: solo CPU.</summary>
    public static readonly Archive WinCpuArm64 = new("cpu", Base + "llama-" + Build + "-bin-win-cpu-arm64.zip",
        "c072e8bb057751587243c1e0ed28d82e23c7e0544a426e0d476f1e77792bf3ce", "llama-" + Build + "-bin-win-cpu-arm64.zip");

    /// <summary>Que zip toca en esta maquina: CUDA si hay driver NVIDIA, Vulkan en x64, CPU en ARM.</summary>
    public static Archive ForThisMachine(bool allowCuda = true)
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return WinCpuArm64;
        if (allowCuda && HasNvidiaDriver())
            return WinCudaX64;
        return WinVulkanX64;
    }

    public static bool HasNvidiaDriver()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return File.Exists(Path.Combine(system, "nvcuda.dll"));
    }

    /// <summary>Capas en GPU: todas salvo en CPU puro.</summary>
    public static int GpuLayers(Archive archive) => archive.Accelerator == "cpu" ? 0 : 99;
}
