using System.Net;
using System.Windows;
using System.Windows.Controls;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Varias IA bajando a la vez desde Ajustes: cada una con su barra y su cancelacion, y borrar las instaladas mientras tanto.</summary>
public class MultipleDownloadsTests : UiTest
{
    /// <summary>Un cuerpo de descarga que se queda esperando hasta que se abre la compuerta (o se cancela).</summary>
    private sealed class GateStream(TaskCompletionSource gate, byte[] data) : Stream
    {
        private int _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos == 0 && data.Length > 1)
            {
                // El primer trozo sale ya (asi hay .part y progreso); el resto espera a la compuerta.
                buffer.Span[0] = data[0];
                _pos = 1;
                return 1;
            }
            await gate.Task.WaitAsync(cancellationToken);
            var n = Math.Min(buffer.Length, data.Length - _pos);
            data.AsSpan(_pos, n).CopyTo(buffer.Span);
            _pos += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private readonly Dictionary<string, TaskCompletionSource> _gates = new();

    /// <summary>Cada repositorio del catalogo tiene un GGUF de 8 bytes que baja solo cuando se abre su compuerta.</summary>
    private void Serve()
    {
        Net.Other = request =>
        {
            var url = request.RequestUri!.ToString();
            var repo = ModelCatalog.All.FirstOrDefault(m => url.Contains("/" + m.Repo + "/", StringComparison.Ordinal))?.Repo;
            if (repo is null) return null;
            var file = repo[(repo.IndexOf('/') + 1)..].Replace("-GGUF", "") + "-Q4_K_M.gguf";
            if (url.EndsWith("/tree/main", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""[{"type":"file","path":"{{file}}","size":8}]""") };
            if (url.Contains("/resolve/main/", StringComparison.Ordinal))
            {
                TaskCompletionSource gate;
                lock (_gates)
                    if (!_gates.TryGetValue(repo, out gate!)) _gates[repo] = gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GateStream(gate, new byte[8])) { Headers = { ContentLength = 8 } } };
            }
            return null;
        };
    }

    private void Open(string repo)
    {
        lock (_gates)
        {
            if (!_gates.TryGetValue(repo, out var gate)) _gates[repo] = gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.TrySetResult();
        }
    }

    private static Button InstallButton(SettingsWindow s, string name)
    {
        // El catalogo se repinta fuera del hilo tras cada clic: se espera a que vuelva a estar la fila.
        // El titulo va en Runs (estrella, nombre, «Recomendada»): TextBlock.Text no los junta.
        static string Title(TextBlock t) => t.Inlines.Count > 0 ? string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) : t.Text;
        Grid? Row() => Ui.Find<StackPanel>(s, "CatalogList").Children.OfType<Grid>().FirstOrDefault(g => Title(Ui.All<TextBlock>(g).First()).Contains(name));
        Ui.WaitFor(() => Row() is not null && !((Controls.BusyIndicator)s.FindName("ModelBusy")).IsBusy);
        return Ui.All<Button>(Row()!).Single();
    }

    [Fact]
    public void Ajustes_DosDescargasALaVez_CancelarUna_YBorrarUnaInstaladaMientrasTanto()
    {
        Serve();
        var first = ModelCatalog.All.First(m => m.Name == "Gemma 3 1B");
        var second = ModelCatalog.All.First(m => m.Name == "Qwen3.5 2B");
        var third = ModelCatalog.All.First(m => m.Name == "Phi-4 Mini");
        // Una IA ya instalada (la de las pruebas) que se puede borrar aunque haya descargas en marcha.
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile });
        Ui.Run(() =>
        {
            var main = new MainWindow();
            main.Show();
            var w = new SettingsWindow { Owner = main };
            Ui.Expect<SettingsWindow>(s =>
            {
                Ui.WaitFor(() => Ui.Find<StackPanel>(s, "CatalogList").Children.Count > 0);
                Ui.Click(InstallButton(s, first.Name));
                Ui.Click(InstallButton(s, second.Name));
                Ui.Click(InstallButton(s, third.Name));
                // Las tres estan bajando a la vez: cada una ha empezado su .part y espera a su compuerta.
                Ui.WaitFor(() => ModelDownloads.All.Count == 3 && Directory.Exists(Paths.Models) && Directory.GetFiles(Paths.Models, "*.part").Length == 3);
                Assert.Equal(3, Ui.Find<StackPanel>(s, "DownloadList").Children.Count);
                Assert.Equal(3, AppSettings.Current.PendingDownloads.Count);
                // El boton de una que ya baja queda desactivado (no se pide dos veces)
                Assert.False(InstallButton(s, first.Name).IsEnabled);
                Assert.Equal(Loc.Get("ModelDownloadingNow"), InstallButton(s, first.Name).ToolTip);
                ModelDownloads.Start(first);
                Assert.Equal(3, ModelDownloads.All.Count);
                // La ventana principal dice la primera y cuantas mas
                Assert.EndsWith(Loc.Format("ModelInstallingMore", "", "", 2)[^10..], Ui.Find<TextBlock>(main, "EngineStatus").Text);

                // Borrar una instalada mientras bajan las otras
                var delete = Ui.All<Button>(Ui.Find<StackPanel>(s, "InstalledList")).First(b => Equals(b.ToolTip, Loc.Get("ModelDelete")));
                Assert.True(delete.IsEnabled);
                Ui.Answer("Yes");
                Ui.Click(delete);
                Assert.False(File.Exists(ModelFile));

                // Cancelar la segunda: se va su fila y su .part, las otras siguen
                var rows = Ui.Find<StackPanel>(s, "DownloadList").Children.OfType<StackPanel>().ToList();
                var cancel = Ui.All<Button>(rows[1]).Single();
                Ui.Click(cancel);
                Ui.WaitFor(() => ModelDownloads.All.Count == 2);
                Ui.WaitFor(() => Directory.GetFiles(Paths.Models, "*.part").Length == 2);
                Assert.DoesNotContain(Directory.GetFiles(Paths.Models, "*.part"), p => p.Contains("Qwen3.5-2B"));
                Assert.Equal(2, AppSettings.Current.PendingDownloads.Count);

                // Acaba la tercera (la ultima pedida): queda activa; luego la primera, que no le quita el sitio
                Open(third.Repo);
                Ui.WaitFor(() => !ModelDownloads.IsDownloading(third.Repo));
                Assert.Equal(third.Name, AppSettings.Current.ModelName);
                Assert.Equal(Loc.Format("ModelInstalledName", third.Name), Ui.Find<TextBlock>(s, "DownloadText").Text);
                Open(first.Repo);
                Ui.WaitFor(() => !ModelDownloads.Busy);
                Assert.Equal(third.Name, AppSettings.Current.ModelName);
                Assert.Contains(AppSettings.Current.Installed, i => i.Name == first.Name && File.Exists(i.Path));
                Assert.Empty(AppSettings.Current.PendingDownloads);
                Assert.Empty(Ui.Find<StackPanel>(s, "DownloadList").Children);
                s.Close();
            });
            w.ShowDialog();
            main.Close();
        });
    }

    [Fact]
    public void AlArrancar_SeRetomanTodasLasPendientes_TambienLaDeVersionesViejas()
    {
        Serve();
        var a = ModelCatalog.All.First(m => m.Name == "Gemma 3 1B");
        var b = ModelCatalog.All.First(m => m.Name == "Qwen3.5 2B");
        var s = AppSettings.Current;
        s.Pending = new PendingDownload { Name = a.Name, Repo = a.Repo, ApproxBytes = a.ApproxBytes, License = a.License, Blurb = a.Blurb };
        s.PendingDownloads.Add(new PendingDownload { Name = b.Name, Repo = b.Repo, ApproxBytes = b.ApproxBytes, License = b.License, Blurb = b.Blurb });
        Open(a.Repo);
        Open(b.Repo);
        Ui.Run(() =>
        {
            ModelDownloads.ResumePending();
            Assert.Null(s.Pending);
            Ui.WaitFor(() => !ModelDownloads.Busy);
        });
        Assert.Empty(s.PendingDownloads);
        Assert.Contains(s.Installed, i => i.Name == a.Name);
        Assert.Contains(s.Installed, i => i.Name == b.Name);
    }
}
