using System.Windows;
using SocLucia.Services;

namespace SocLucia.Engine;

/// <summary>
/// La descarga de una IA del catalogo vive aqui, fuera de la ventana de Ajustes: se puede cerrar
/// Ajustes y seguir preguntando mientras baja. Una descarga a la vez. Al acabar, la IA queda
/// activa y apuntada en la lista de instaladas. Los avisos salen siempre en el hilo de la interfaz.
/// </summary>
public static class ModelDownloads
{
    public sealed record Status(CatalogModel Model, long Received, long? Total)
    {
        public double? Fraction => Total is > 0 ? (double)Received / Total : null;
    }

    private static CancellationTokenSource? _cancel;

    /// <summary>La descarga en marcha, o null.</summary>
    public static Status? Current { get; private set; }
    public static bool Busy => _cancel is not null;

    /// <summary>Progreso (en el hilo de la interfaz).</summary>
    public static event Action? Changed;
    /// <summary>Fin: error null = instalada; cancelada = OperationCanceledException.</summary>
    public static event Action<CatalogModel, Exception?>? Finished;

    public static void Start(CatalogModel model)
    {
        if (_cancel is not null)
            return;
        _cancel = new CancellationTokenSource();
        Current = new Status(model, 0, null);
        Raise(Changed);
        _ = RunAsync(model, _cancel.Token);
    }

    public static void Cancel() => _cancel?.Cancel();

    private static async Task RunAsync(CatalogModel model, CancellationToken cancel)
    {
        Exception? error = null;
        try
        {
            var file = await ModelCatalog.PickFileAsync(model, cancel);
            var progress = new Progress<Downloader.Progress>(p =>
            {
                Current = new Status(model, p.Received, p.Total);
                Raise(Changed);
            });
            var path = await ModelCatalog.DownloadAsync(model, file, progress, cancel);
            var settings = AppSettings.Current;
            settings.Remember(new InstalledModel { Name = model.Name, Path = path, License = model.License });
            settings.ModelPath = path;
            settings.ModelName = model.Name;
            settings.ModelLicense = model.License;
            settings.Save();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _cancel?.Dispose();
            _cancel = null;
            Current = null;
        }
        Raise(() => Finished?.Invoke(model, error));
    }

    private static void Raise(Action? action)
    {
        if (action is null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
