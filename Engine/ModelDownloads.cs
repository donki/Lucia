using System.Windows;
using SocLucia.Services;

namespace SocLucia.Engine;

/// <summary>
/// Las descargas de IA del catalogo viven aqui, fuera de la ventana de Ajustes: se puede cerrar
/// Ajustes y seguir preguntando mientras bajan. Varias a la vez, cada una con su progreso y su
/// cancelacion; la misma IA no se baja dos veces a la vez. Al acabar, la IA queda apuntada en la
/// lista de instaladas y, si es la ultima que se pidio, activa. Los avisos salen siempre en el hilo
/// de la interfaz.
/// </summary>
public static class ModelDownloads
{
    public sealed record Status(CatalogModel Model, long Received, long? Total)
    {
        public double? Fraction => Total is > 0 ? (double)Received / Total : null;
    }

    private sealed class Running(CatalogModel model, CancellationTokenSource cancel, long order)
    {
        public CatalogModel Model { get; } = model;
        public CancellationTokenSource Cancel { get; } = cancel;
        public long Order { get; } = order;
        public Status Status { get; set; } = new(model, 0, null);
        /// <summary>El .part que se esta escribiendo (para tirarlo si se cancela).</summary>
        public string? PartFile { get; set; }
    }

    private static readonly object Lock = new();
    private static readonly List<Running> Downloads = [];
    private static long _order;

    /// <summary>Las descargas en marcha, en el orden en que se pidieron.</summary>
    public static IReadOnlyList<Status> All
    {
        get { lock (Lock) return Downloads.Select(d => d.Status).ToList(); }
    }

    /// <summary>La primera descarga en marcha, o null.</summary>
    public static Status? Current => All.FirstOrDefault();
    public static bool Busy { get { lock (Lock) return Downloads.Count > 0; } }

    /// <summary>Si esa IA (por repositorio) se esta bajando.</summary>
    public static bool IsDownloading(string repo)
    {
        lock (Lock) return Downloads.Any(d => string.Equals(d.Model.Repo, repo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Progreso (en el hilo de la interfaz).</summary>
    public static event Action? Changed;
    /// <summary>Fin de una: error null = instalada; cancelada = OperationCanceledException.</summary>
    public static event Action<CatalogModel, Exception?>? Finished;

    public static void Start(CatalogModel model)
    {
        Running running;
        lock (Lock)
        {
            if (Downloads.Any(d => string.Equals(d.Model.Repo, model.Repo, StringComparison.OrdinalIgnoreCase)))
                return;
            running = new Running(model, new CancellationTokenSource(), ++_order);
            Downloads.Add(running);
        }
        var settings = AppSettings.Current;
        settings.PendingDownloads.RemoveAll(p => string.Equals(p.Repo, model.Repo, StringComparison.OrdinalIgnoreCase));
        settings.PendingDownloads.Add(new PendingDownload { Name = model.Name, Repo = model.Repo, ApproxBytes = model.ApproxBytes, License = model.License, Blurb = model.Blurb });
        settings.Save();
        Raise(Changed);
        _ = RunAsync(running);
    }

    /// <summary>Cancela todas las descargas.</summary>
    public static void Cancel()
    {
        lock (Lock)
            foreach (var d in Downloads) d.Cancel.Cancel();
    }

    /// <summary>Cancela la descarga de esa IA (por repositorio).</summary>
    public static void Cancel(string repo)
    {
        lock (Lock)
            foreach (var d in Downloads.Where(d => string.Equals(d.Model.Repo, repo, StringComparison.OrdinalIgnoreCase)))
                d.Cancel.Cancel();
    }

    /// <summary>Si la aplicacion se cerro con descargas a medias, se retoman donde iban (los .part se reutilizan).</summary>
    public static void ResumePending()
    {
        var settings = AppSettings.Current;
        // Versiones anteriores guardaban una sola descarga pendiente.
        if (settings.Pending is { } old)
        {
            if (!settings.PendingDownloads.Any(p => p.Repo == old.Repo)) settings.PendingDownloads.Add(old);
            settings.Pending = null;
        }
        foreach (var p in settings.PendingDownloads.ToList())
            Start(new CatalogModel(p.Name, p.Repo, p.ApproxBytes, p.License, p.Blurb));
    }

    private static async Task RunAsync(Running running)
    {
        var model = running.Model;
        var cancel = running.Cancel.Token;
        Exception? error = null;
        try
        {
            var file = await ModelCatalog.PickFileAsync(model, cancel);
            running.PartFile = System.IO.Path.Combine(Paths.Models, System.IO.Path.GetFileName(file.Name)) + ".part";
            var progress = new Progress<Downloader.Progress>(p =>
            {
                running.Status = new Status(model, p.Received, p.Total);
                Raise(Changed);
            });
            var path = await ModelCatalog.DownloadAsync(model, file, progress, cancel);
            var settings = AppSettings.Current;
            settings.Remember(new InstalledModel { Name = model.Name, Path = path, License = model.License, Repo = model.Repo });
            // Con varias a la vez, se queda activa la ultima que se pidio (o esta, si no hay ninguna activa).
            bool latest;
            lock (Lock) latest = running.Order == _order;   // nadie pidio otra despues (aunque esa ya haya acabado)
            if (latest || !settings.HasModel)
            {
                settings.ModelPath = path;
                settings.ModelName = model.Name;
                settings.ModelLicense = model.License;
            }
            settings.Save();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            lock (Lock) Downloads.Remove(running);
            running.Cancel.Dispose();
            // Acabada o cancelada a mano: ya no queda pendiente (al cancelar se tira su .part).
            // Un error de red la deja pendiente para retomarla en el siguiente arranque.
            if (error is null || error is OperationCanceledException)
            {
                if (error is OperationCanceledException) DeletePart(running.PartFile);
                AppSettings.Current.PendingDownloads.RemoveAll(p => string.Equals(p.Repo, model.Repo, StringComparison.OrdinalIgnoreCase));
                AppSettings.Current.Save();
            }
        }
        Raise(() => Finished?.Invoke(model, error));
    }

    private static void DeletePart(string? part)
    {
        try
        {
            if (part is not null && System.IO.File.Exists(part))
                System.IO.File.Delete(part);
        }
        catch (Exception) { }
    }

    private static void Raise(Action? action)
    {
        if (action is null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
