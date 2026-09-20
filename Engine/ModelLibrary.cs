using System.IO;
using SocAiChat.Services;

namespace SocAiChat.Engine;

/// <summary>
/// La carpeta de los modelos se puede cambiar de sitio (otro disco con espacio). Al cambiarla se
/// mueven los GGUF que hay, con progreso, y se apunta el modelo activo a su nueva ruta. Antes se
/// para el motor, que tiene el fichero abierto. En el mismo volumen es un renombrado; entre discos,
/// copia y borrado, fichero a fichero, sin dejar nada a medias.
/// </summary>
public static class ModelLibrary
{
    public sealed record MoveProgress(string File, long Done, long Total);

    public static async Task MoveAsync(string newFolder, IProgress<MoveProgress>? progress, CancellationToken cancel)
    {
        var settings = AppSettings.Current;
        var oldFolder = Path.GetFullPath(Paths.Models);
        newFolder = Path.GetFullPath(newFolder);
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            return;
        if (newFolder.StartsWith(oldFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Loc.Get("ModelsFolderInside"));
        Directory.CreateDirectory(newFolder);
        App.Engine.Stop();

        var files = Directory.Exists(oldFolder) ? Directory.GetFiles(oldFolder, "*.gguf") : [];
        var total = files.Sum(f => new FileInfo(f).Length);
        long done = 0;
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();
            var target = Path.Combine(newFolder, Path.GetFileName(file));
            var size = new FileInfo(file).Length;
            var sameVolume = string.Equals(Path.GetPathRoot(file), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
            if (sameVolume)
            {
                File.Move(file, target, overwrite: true);
            }
            else
            {
                var temp = target + ".part";
                await using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
                await using (var dest = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
                {
                    var buffer = new byte[4 << 20];
                    int read;
                    long copied = 0;
                    while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                    {
                        await dest.WriteAsync(buffer.AsMemory(0, read), cancel);
                        copied += read;
                        progress?.Report(new MoveProgress(Path.GetFileName(file), done + copied, total));
                    }
                }
                File.Move(temp, target, overwrite: true);
                File.Delete(file);
            }
            if (string.Equals(settings.ModelPath, file, StringComparison.OrdinalIgnoreCase))
                settings.ModelPath = target;
            done += size;
            progress?.Report(new MoveProgress(Path.GetFileName(file), done, total));
        }
        settings.ModelsFolder = string.Equals(newFolder, Path.GetFullPath(Paths.DefaultModels), StringComparison.OrdinalIgnoreCase) ? null : newFolder;
        settings.Save();
    }
}
