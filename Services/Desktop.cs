using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SocLucia.Services;

/// <summary>
/// Lo que las ventanas y las herramientas piden a Windows: abrir con su programa, elegir ficheros y
/// carpetas, el portapapeles, el registro del arranque con Windows y cerrar la aplicacion. En la
/// aplicacion es <see cref="RealDesktop"/>; las pruebas ponen un doble en <see cref="Desktop.Current"/>
/// y asi manejan las ventanas sin dialogos de Windows y sin tocar el portapapeles ni el registro de
/// quien trabaja.
/// </summary>
public interface IDesktop
{
    /// <summary>Abre un fichero, carpeta o enlace con su programa (como Process.Start con UseShellExecute).</summary>
    void Open(string target);

    /// <summary>Elegir una carpeta. Null si se cancela.</summary>
    string? PickFolder(Window owner, string? initial, string? title);

    /// <summary>Elegir ficheros. Null si se cancela.</summary>
    string[]? PickFiles(Window owner, string? title, string filter, bool multiselect);

    /// <summary>Dialogo de guardar. Null si se cancela.</summary>
    string? PickSave(Window? owner, string fileName, string filter, string defaultExt);

    bool ClipboardHasText();

    string ClipboardText();

    void SetClipboardText(string text);

    /// <summary>Ficheros copiados en el portapapeles, o null si no hay.</summary>
    string[]? ClipboardFiles();

    /// <summary>Una imagen en el portapapeles, o null si no hay.</summary>
    BitmapSource? ClipboardImage();

    /// <summary>HKCU (para «Arrancar con Windows»).</summary>
    RegistryKey CurrentUser { get; }

    /// <summary>Cerrar la aplicacion («Salir» en la bandeja).</summary>
    void Shutdown();
}

/// <summary>Donde esta el de esta ejecucion.</summary>
public static class Desktop
{
    public static IDesktop Current { get; set; } = new RealDesktop();
}

/// <summary>El de verdad.</summary>
public sealed class RealDesktop : IDesktop
{
    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    public string? PickFolder(Window owner, string? initial, string? title)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = initial ?? string.Empty };
        if (title is not null)
            dialog.Title = title;
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public string[]? PickFiles(Window owner, string? title, string filter, bool multiselect)
    {
        var dialog = new OpenFileDialog { Multiselect = multiselect, Filter = filter, CheckFileExists = true };
        if (title is not null)
            dialog.Title = title;
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : null;
    }

    public string? PickSave(Window? owner, string fileName, string filter, string defaultExt)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = filter, DefaultExt = defaultExt };
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true ? dialog.FileName : null;
    }

    public bool ClipboardHasText() => Clipboard.ContainsText();

    public string ClipboardText() => Clipboard.GetText();

    public void SetClipboardText(string text) => Clipboard.SetText(text);

    public string[]? ClipboardFiles() =>
        Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string?>().OfType<string>().ToArray() : null;

    public BitmapSource? ClipboardImage() => Clipboard.ContainsImage() ? Clipboard.GetImage() : null;

    public RegistryKey CurrentUser => Registry.CurrentUser;

    public void Shutdown() => Application.Current.Shutdown();
}
