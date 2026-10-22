namespace SocLucia.FineTune;

/// <summary>
/// Versiones fijadas de todo lo que hace falta para enseñar a una IA (como <c>EnginePin</c> con
/// llama.cpp): uv (Apache-2.0/MIT) monta un Python aislado sin tocar el del sistema; las bibliotecas
/// de entrenamiento (PyTorch BSD, Transformers/PEFT/Accelerate Apache-2.0, bitsandbytes MIT) van con
/// version exacta; los convertidores a GGUF salen del codigo de llama.cpp de la misma version que el motor.
/// </summary>
public static class FineTunePin
{
    /// <summary>Cambia cuando cambia algo de lo de abajo: el entorno se rehace solo.</summary>
    public const string EnvVersion = "1";

    public const string UvVersion = "0.12.23";
    public const string UvUrl = "https://github.com/astral-sh/uv/releases/download/" + UvVersion + "/uv-x86_64-pc-windows-msvc.zip";
    public const string UvSha256 = "75d05de6762778c31ee183398de7dd15093fad0ed90b1f236d8205ea5ec00c90";

    public const string PythonVersion = "3.12";

    /// <summary>El codigo fuente de llama.cpp de la version del motor: de el salen convert_lora_to_gguf.py y convert_hf_to_gguf.py.</summary>
    public const string LlamaCppUrl = "https://github.com/ggml-org/llama.cpp/archive/refs/tags/" + Engine.EnginePin.Build + ".tar.gz";
    public const string LlamaCppSha256 = "1a856e6951bbe5c45a61e4523ef7536b1dfcf62c6d935464fa1a94550761db24";

    public const string Torch = "torch==2.14.1";
    /// <summary>PyTorch con CUDA 13 (driver NVIDIA 580 o mas nuevo) o solo CPU.</summary>
    public const string TorchCudaIndex = "https://download.pytorch.org/whl/cu130";
    public const string TorchCpuIndex = "https://download.pytorch.org/whl/cpu";

    public static readonly string[] Packages =
    [
        "transformers==5.19.0", "peft==0.21.2", "accelerate==1.15.0", "bitsandbytes==0.50.2",
        "safetensors==0.8.0", "sentencepiece==0.2.2", "protobuf", "numpy", "pyyaml",
    ];
}
