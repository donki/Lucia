using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SocLucia.Agent;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Ajustes de verdad (con la red de mentira para Hugging Face), «Acerca de», dialogos, tema y arranque.</summary>
public sealed class SettingsWindowTests : UiTest
{
    private static void Settings(Action<SettingsWindow> act, Window? owner = null)
    {
        var w = new SettingsWindow { Owner = owner };
        Ui.Expect<SettingsWindow>(s =>
        {
            act(s);
            if (s.IsVisible)
                s.Close();
        });
        w.ShowDialog();
    }

    private static ButtonBase Button(DependencyObject root, string name) => Ui.Find<ButtonBase>(root, name);

    [Fact]
    public void Ajustes_EnseñanLaIAElPC_YLasCarpetas()
    {
        Ui.Run(() => Settings(s =>
        {
            Assert.Equal(Loc.Format("ModelCurrent", "Prueba"), Ui.Find<TextBlock>(s, "ModelCurrent").Text);
            Assert.Contains(ModelFile, Ui.Find<TextBlock>(s, "ModelDetail").Text);
            Assert.NotEmpty(Ui.Find<StackPanel>(s, "CatalogList").Children);
            Assert.Equal(Paths.Models, Ui.Find<TextBox>(s, "ModelsFolderBox").Text);
            Assert.Contains(Paths.Root, Ui.Find<TextBlock>(s, "DiagnosticsText").Text);
            Assert.Equal(Loc.Format("DocsCount", 0), Ui.Find<TextBlock>(s, "DocsCount").Text);
            Assert.Equal(Loc.Get("TasksEmpty"), Ui.Find<TextBlock>(s, "TasksEmpty").Text);
            Loc.Use("en");
            Assert.Equal(Loc.Get("SettingsTitle"), s.Title);
            Loc.Use("es");
        }));
    }

    [Fact]
    public void Ajustes_SinModelo_LoDice()
    {
        AppSettings.Current.ModelPath = null;
        Ui.Run(() => Settings(s =>
        {
            Assert.Equal(Loc.Get("ModelNone"), Ui.Find<TextBlock>(s, "ModelCurrent").Text);
            Assert.Equal(string.Empty, Ui.Find<TextBlock>(s, "ModelDetail").Text);
        }));
    }

    [Fact]
    public void Ajustes_Instaladas_Usar_YBorrar()
    {
        var other = Path.Combine(Paths.DefaultModels, "Otra-Q4_K_M.gguf");
        File.WriteAllBytes(other, [1, 2]);
        AppSettings.Current.ModelLicense = "MIT";
        Ui.Run(() =>
        {
            var main = new MainWindow();
            main.Show();
            Settings(s =>
            {
                var list = Ui.Find<StackPanel>(s, "InstalledList");
                Assert.Equal(2, list.Children.Count);
                var use = Ui.All<Button>(list).Where(b => Equals(b.ToolTip, Loc.Get("ModelUse")) && b.IsEnabled).Single();
                Ui.Click(use);
                Assert.Equal(other, AppSettings.Current.ModelPath);
                list = Ui.Find<StackPanel>(s, "InstalledList");
                Ui.Answer("Cancel");
                Ui.Click(Ui.All<Button>(list).First(b => Equals(b.ToolTip, Loc.Get("ModelDelete"))));
                Assert.True(File.Exists(ModelFile) && File.Exists(other));
                Ui.Answer("Yes");
                Ui.Click(Ui.All<Button>(Ui.Find<StackPanel>(s, "InstalledList")).First(b => Equals(b.ToolTip, Loc.Get("ModelDelete"))));
                Assert.Single(Ui.Find<StackPanel>(s, "InstalledList").Children);
            }, main);
        });
    }

    [Fact]
    public void Ajustes_Importar_UnGguf()
    {
        var outside = Path.Combine(Sandbox.Work, "Externo-Q8_0.gguf");
        File.WriteAllBytes(outside, [5]);
        var inside = Path.Combine(Paths.DefaultModels, "Dentro-Q8_0.gguf");
        File.WriteAllBytes(inside, [6]);
        Ui.Run(() => Settings(s =>
        {
            Fake.Files.Enqueue(null);
            Ui.Click(Button(s, "ImportButton"));
            Fake.Files.Enqueue([outside]);
            Ui.Click(Button(s, "ImportButton"));
            Assert.Equal(outside, AppSettings.Current.ModelPath);
            Fake.Files.Enqueue([inside]);
            Ui.Click(Button(s, "ImportButton"));
            Assert.Contains(AppSettings.Current.Installed, i => i.Path == inside);
        }));
    }

    [Fact]
    public void Ajustes_MoverLaCarpetaDeModelos_ConConfirmacion()
    {
        var target = Path.Combine(Sandbox.Root, "otros-modelos");
        Ui.Run(() =>
        {
            var main = new MainWindow();
            main.Show();
            Settings(s =>
            {
                Fake.Folders.Enqueue(null);
                Ui.Click(Button(s, "ModelsFolderButton"));
                Fake.Folders.Enqueue(Paths.Models);   // la misma: nada
                Ui.Click(Button(s, "ModelsFolderButton"));
                Ui.Answer("Cancel");
                Fake.Folders.Enqueue(target);
                Ui.Click(Button(s, "ModelsFolderButton"));
                Assert.False(Directory.Exists(target) && Directory.GetFiles(target).Length > 0);
                Ui.Answer("Yes");
                Ui.Wait(s.MoveModelsAsync(target));
                Assert.Equal(target, Ui.Find<TextBox>(s, "ModelsFolderBox").Text);
                Assert.Equal(Loc.Get("ModelsFolderMoved"), Ui.Find<TextBlock>(s, "MoveStatus").Text);
                Assert.Equal(Path.Combine(target, "Prueba-Q4_K_M.gguf"), AppSettings.Current.ModelPath);
            }, main);
        });
        AppSettings.Current.ModelsFolder = null;
    }

    [Fact]
    public void Ajustes_BuscarEnHuggingFace_ResultadosYErrores()
    {
        Net.Other = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://huggingface.co/api/models?", StringComparison.Ordinal))
                return url.Contains("roto", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                        [{"id":"org/Bueno-GGUF","downloads":1500000,"likes":20,"tags":["gguf","license:apache-2.0"],"pipeline_tag":"text-generation"},
                         {"id":"org/Partido-GGUF","downloads":1500,"likes":2,"tags":["gguf"],"pipeline_tag":"text-generation","gated":"manual"},
                         {"id":"org/imagen","downloads":5,"tags":["diffusers"],"pipeline_tag":"text-to-image"},
                         {"id":"org/sin-nada","downloads":3,"tags":[]}]
                        """) };
            if (url == "https://huggingface.co/api/models/org/Bueno-GGUF/tree/main")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"type":"file","path":"Bueno-Q4_K_M.gguf","size":104857600}]""") };
            return null;
        };
        Ui.Run(() => Settings(s =>
        {
            Ui.Click(Button(s, "SearchButton"));   // sin texto: nada
            var box = Ui.Find<TextBox>(s, "SearchBox");
            box.Text = "bueno";
            Ui.Call(s, "OnSearchKey", box, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(s)!, 0, Key.A) { RoutedEvent = Keyboard.KeyDownEvent });
            Ui.Call(s, "OnSearchKey", box, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(s)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Ui.WaitFor(() => Button(s, "SearchButton").IsEnabled && Ui.Find<StackPanel>(s, "SearchList").Children.Count == 4);
            Assert.StartsWith(Loc.Format("SearchHfFound", 4, 0, 0)[..5], Ui.Find<TextBlock>(s, "SearchHint").Text);
            var hf = Ui.All<Button>(Ui.Find<StackPanel>(s, "SearchList")).First(b => Equals(b.ToolTip, Loc.Get("SearchOpenHf")));
            Ui.Click(hf);   // la abre en el navegador (si existiera como fichero: aqui no, no se abre nada)
            Loc.Use("en");   // repinta con los resultados
            Loc.Use("es");
            box.Text = "roto";
            Ui.Click(Button(s, "SearchButton"));
            Ui.WaitFor(() => Ui.Find<TextBlock>(s, "SearchHint").Text.StartsWith(Loc.Format("SearchHfError", "")[..5], StringComparison.Ordinal));
            s.SearchForTest("*bueno");
            Ui.WaitFor(() => Button(s, "SearchButton").IsEnabled && Ui.Find<StackPanel>(s, "SearchList").Children.Count == 4);
        }));
    }

    [Fact]
    public void Ajustes_InstalarUnaDelCatalogo_YSiFallaLoDice()
    {
        Net.Other = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/tree/main", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"type":"file","path":"Nueva-Q4_K_M.gguf","size":5}]""") };
            if (url.Contains("/resolve/main/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4, 5]) };
            return null;
        };
        Ui.Run(() =>
        {
            var main = new MainWindow();
            main.Show();
            Settings(s =>
            {
                var install = Ui.All<Button>(Ui.Find<StackPanel>(s, "CatalogList")).First(b => Equals(b.ToolTip, Loc.Get("ModelInstall")) && b.IsEnabled);
                Ui.Click(install);
                Ui.WaitFor(() => Ui.Find<TextBlock>(s, "DownloadText").Text == Loc.Get("ModelInstalled"));
                Assert.EndsWith("Nueva-Q4_K_M.gguf", AppSettings.Current.ModelPath);
                Net.Other = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
                var again = Ui.All<Button>(Ui.Find<StackPanel>(s, "CatalogList")).First(b => Equals(b.ToolTip, Loc.Get("ModelInstall")) && b.IsEnabled);
                Ui.Answer("Ok");
                Ui.Click(again);
                Ui.WaitFor(() => Ui.Pending == 0 && !ModelDownloads.Busy);
            }, main);
        });
        AppSettings.Current.Pending = null;
    }

    [Fact]
    public void Ajustes_Instrucciones_Internet_Letra_Memoria_YCarpetas()
    {
        UserMemory.Add("Me llamo Josep");
        UserMemory.Add("Vivo en Montcada");
        Scheduler.Add("Copia", "haz la copia", "daily 08:30");
        try
        {
            Ui.Run(() =>
            {
                var main = new MainWindow();
                main.Show();
                Settings(s =>
                {
                    Ui.Find<TextBox>(s, "InstructionsBox").Text = "Responde corto";
                    Ui.Call(s, "OnInstructionsChanged", s, new RoutedEventArgs());
                    Ui.Call(s, "OnInstructionsChanged", s, new RoutedEventArgs());   // igual: nada
                    Assert.Equal("Responde corto", AppSettings.Current.Instructions);
                    Ui.Click(Ui.Find<CheckBox>(s, "InternetCheck"));
                    Assert.False(AppSettings.Current.InternetAccess);
                    Ui.Find<Slider>(s, "FontSizeSlider").Value = 18;
                    Assert.Equal(18, AppSettings.Current.FontSize);
                    Assert.Equal("18 px", Ui.Find<TextBlock>(s, "FontSizeValue").Text);
                    Ui.Click(Ui.Find<CheckBox>(s, "MemoryCheck"));
                    Assert.False(AppSettings.Current.MemoryEnabled);
                    var memory = Ui.Find<StackPanel>(s, "MemoryList");
                    Assert.Equal(2, memory.Children.Count);
                    Ui.Click(Ui.All<Button>(memory).First());
                    Assert.Single(UserMemory.All);
                    Ui.Answer("Cancel");
                    Ui.Click(Button(s, "MemoryClearButton"));
                    Ui.Answer("Yes");
                    Ui.Click(Button(s, "MemoryClearButton"));
                    Assert.Empty(UserMemory.All);
                    var tasks = Ui.Find<StackPanel>(s, "TasksList");
                    Ui.Click(Ui.All<CheckBox>(tasks).Single());   // apagar la tarea
                    Assert.False(Scheduler.All.Single().Enabled);
                    Ui.Click(Ui.All<Button>(Ui.Find<StackPanel>(s, "TasksList")).Single());
                    Assert.Empty(Scheduler.All);
                    // carpeta de trabajo: una que no existe no se acepta
                    var work = Ui.Find<TextBox>(s, "WorkFolderBox");
                    work.Text = Path.Combine(Sandbox.Root, "no-esta");
                    Ui.Call(s, "OnWorkFolderChanged", s, new RoutedEventArgs());
                    Assert.Equal(Sandbox.Work, work.Text);
                    Fake.Folders.Enqueue(Sandbox.Documents);
                    Ui.Click(Button(s, "WorkFolderButton"));
                    Assert.Equal(Sandbox.Documents, AppSettings.Current.WorkFolder);
                    Fake.Folders.Enqueue(null);
                    Ui.Click(Button(s, "WorkFolderButton"));
                    Fake.Folders.Enqueue(Sandbox.Work);
                    Ui.Click(Button(s, "DocsFolderButton"));
                    Assert.Equal(Sandbox.Work, AppSettings.Current.DocumentsFolder);
                    Fake.Folders.Enqueue(null);
                    Ui.Click(Button(s, "DocsFolderButton"));
                    Ui.Click(Button(s, "DocsOpenButton"));
                    Ui.Click(Button(s, "OpenDataButton"));
                    Ui.Click(Button(s, "OpenLogButton"));
                    Ui.Click(Button(s, "OpenCommandLogButton"));   // no existe: no se abre
                    Assert.Contains(Sandbox.Work, Fake.Opened);
                    Assert.Contains(Paths.Root, Fake.Opened);
                    Fake.OpenError = new InvalidOperationException("sin explorador");
                    Ui.Answer("Ok");
                    Ui.Click(Button(s, "OpenDataButton"));
                    Ui.Click(Button(s, "CloseButton"));
                }, main);
            });
        }
        finally
        {
            foreach (var t in Scheduler.All.ToList())
                Scheduler.Remove(t.Id);
            UserMemory.Clear();
        }
    }

    [Fact]
    public void Ajustes_Permisos_Bandeja_Arranque_EIdioma()
    {
        Ui.Run(() =>
        {
            var main = new MainWindow();
            main.Show();
            Settings(s =>
            {
                var perms = Ui.Find<StackPanel>(s, "PermList");
                var deny = Ui.All<RadioButton>(perms).First(r => Equals(r.Content, Loc.Get("PermDeny")));
                deny.IsChecked = true;
                Assert.Equal(Permission.Deny, AppSettings.Current.PermissionFor(Resource.Commands));
                Ui.Click(Ui.Find<CheckBox>(s, "TrayCheck"));
                Assert.False(AppSettings.Current.TrayOnMinimize);
                Assert.False(main.Tray!.MinimizeToTray);
                var startup = Ui.Find<CheckBox>(s, "StartupCheck");
                Assert.False(startup.IsChecked);
                WindowsStartup.Executable = () => @"C:\Programas\sOCLucia.exe";
                Ui.Click(startup);
                Assert.True(WindowsStartup.IsEnabled());
                Ui.Click(startup);
                Assert.False(WindowsStartup.IsEnabled());
                Ui.Click(Button(s, "EnglishButton"));
                Assert.Equal("en", Loc.Language);
                Assert.Equal("en", AppSettings.Current.Language);
                Ui.Click(Button(s, "SpanishButton"));
                Assert.Equal("es", Loc.Language);
            }, main);
        });
        WindowsStartup.Executable = () => Environment.ProcessPath;
    }

    [Fact]
    public void ArranqueConWindows_LaEntradaVieja_SePasaALaNueva()
    {
        using (var run = Fake.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            run.SetValue("sOCAIChat", "viejo.exe");
        WindowsStartup.Executable = () => @"C:\Programas\sOCLucia.exe";
        try
        {
            Assert.True(WindowsStartup.IsEnabled());
            using var run = Fake.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!;
            Assert.Null(run.GetValue("sOCAIChat"));
            Assert.Equal("\"C:\\Programas\\sOCLucia.exe\" --tray", run.GetValue("sOCLucia"));
            WindowsStartup.Refresh();   // en Debug no toca nada
        }
        finally
        {
            WindowsStartup.Executable = () => Environment.ProcessPath;
        }
    }

    [Fact]
    public void Ajustes_PuertaDeEditores_Puerto_Token_YCopiar()
    {
        Ui.Run(() => Settings(s =>
        {
            var d = AppSettings.Current.EditorDoor;
            var port = Ui.Find<TextBox>(s, "DoorPort");
            port.Text = "80";   // no vale: vuelve el de antes
            Ui.Call(s, "OnPortChanged", s, new RoutedEventArgs());
            Assert.Equal(d.Port.ToString(), port.Text);
            var free = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            free.Start();
            var p = ((IPEndPoint)free.LocalEndpoint).Port;
            free.Stop();
            port.Text = p.ToString();
            Ui.Call(s, "OnPortChanged", s, new RoutedEventArgs());
            Assert.Equal(p, d.Port);
            Ui.Click(Ui.Find<CheckBox>(s, "DoorCheck"));
            Assert.True(d.Enabled);
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(s, "DoorDetails").Visibility);
            var token = d.Token;
            Ui.Click(Button(s, "NewTokenButton"));
            Assert.NotEqual(token, d.Token);
            Ui.Click(Button(s, "CopyTokenButton"));
            Assert.Equal(d.Token, Fake.Clipboard);
            Ui.Click(Button(s, "CopyUrlButton"));
            Assert.StartsWith("http://", Fake.Clipboard);
            Ui.Click(Ui.Find<CheckBox>(s, "DoorCheck"));
            Assert.False(d.Enabled);
        }));
        App.Door.Apply();
    }

    [Fact]
    public void Ajustes_Imagenes_InstalarFallaYSeDice_YQuitarlas()
    {
        Ui.Run(() => Settings(s =>
        {
            Ui.Answer("Ok");
            Ui.Click(Button(s, "ImagesInstallButton"));
            Ui.WaitFor(() => Ui.Pending == 0 && Ui.Find<ButtonBase>(s, "ImagesInstallButton").Content as string == Loc.Get("ImagesInstall"));
            Directory.CreateDirectory(ImageEngine.ImagesFolder);
            Ui.Call(s, "PaintImages");
            Ui.Click(Button(s, "ImagesFolderButton"));
            Assert.Equal(ImageEngine.ImagesFolder, Fake.Opened.Last());
            Ui.Answer("Cancel");
            Ui.Call(s, "OnImagesRemove", s, new RoutedEventArgs());
            Ui.Answer("Yes");
            Ui.Call(s, "OnImagesRemove", s, new RoutedEventArgs());
        }));
    }

    // ------------------------------------------------------------------ acerca de, dialogos, arranque

    [Fact]
    public void AcercaDe_Version_Idioma_YContacto()
    {
        Ui.Run(() =>
        {
            var owner = new Window();
            owner.Show();
            Ui.Expect<AboutWindow>(a =>
            {
                Assert.StartsWith("v", Ui.Find<TextBlock>(a, "VersionLabel").Text);
                Ui.Click(Button(a, "ContactButton"));
                Assert.StartsWith("mailto:", Fake.Opened.Single());
                Ui.Click(Button(a, "SpanishButton"));   // ya en castellano
                Ui.Expect<AboutWindow>(b => Ui.Click(Button(b, "CloseButton")));
                Ui.Click(Button(a, "EnglishButton"));
            });
            new AboutWindow { Owner = owner }.ShowDialog();
            Ui.WaitFor(() => Ui.Pending == 0);
            Loc.Use("es");
        });
    }

    [Fact]
    public void Dialogo_PreguntarConIntroYEscape()
    {
        Ui.Run(() =>
        {
            var owner = new Window();
            owner.Show();
            Ui.Expect<PromptWindow>(p =>
            {
                Ui.All<TextBox>(p).Single().Text = "nuevo";
                p.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(p)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            });
            Assert.Equal("nuevo", PromptWindow.Ask(owner, "t", "m", "viejo"));
            Ui.Expect<PromptWindow>(p => p.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(p)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }));
            Assert.False(PromptWindow.Confirm(owner, "t", "m"));
        });
    }

    [Fact]
    public void Tema_PintaLasSuperficies()
    {
        Ui.Run(() =>
        {
            ThemeManager.Apply();
            var w = new Window();
            ThemeManager.ApplyToWindow(w);
            new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle();
            ThemeManager.ApplyToWindow(w);
            typeof(ThemeManager).GetProperty("IsDark")?.SetValue(null, !ThemeManager.IsDark);
            ThemeManager.ApplyToWindow(w);
            ThemeManager.Apply();
        });
    }

    [Fact]
    public void Arranque_AbreLaVentana_ConLasOpcionesDePrueba()
    {
        var app = (App)Application.Current;
        var file = Path.Combine(Sandbox.Work, "leeme.txt");
        File.WriteAllText(file, "hola");
        AppSettings.Current.Language = "es";
        AppSettings.Current.Save();
        Net.Answers.Enqueue(new FakeHttp.Answer("Respuesta al --ask"));
        Ui.Run(() =>
        {
            Ui.Expect<AboutWindow>(a => a.Close());
            var w = app.Launch(["--work", "--auto", "--ask", "hola", "--tool", ToolBox.ReadFile, "{\"path\":\"" + file.Replace("\\", "\\\\") + "\"}", "--about", "--tray"]);
            try
            {
                Assert.Same(w, app.MainWindow);
                Ui.WaitFor(() => Net.ChatRequests.Count == 1);
                Ui.WaitFor(() => !w.IsVisible);   // --tray
                Ui.WaitFor(() => Ui.Pending == 0);
                Ui.Answer("Ok");
                app.OnUnexpected(new InvalidOperationException("prueba"));
            }
            finally
            {
                w.Tray?.Dispose();
            }
        });
        Assert.Contains("error no controlado: System.InvalidOperationException: prueba", File.ReadAllText(Paths.EngineLog));
    }

    [Fact]
    public void ErrorInesperado_ConLaVentanaAbierta_Avisa()
    {
        var app = (App)Application.Current;
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            var previous = app.MainWindow;
            app.MainWindow = w;
            try
            {
                string? message = null;
                Ui.Answer("Ok", m => message = m);
                app.OnUnexpected(new InvalidOperationException("prueba"));
                Assert.Equal(Loc.Get("UnexpectedError"), message);
            }
            finally
            {
                app.MainWindow = previous;
            }
        });
    }
}
