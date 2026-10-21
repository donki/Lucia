using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace SocLucia.Services;

/// <summary>
/// Vigilante de congelaciones: un hilo aparte manda cada poco un aviso al hilo de la interfaz con
/// prioridad de entrada (la de los clics y las teclas). Si no lo atiende en <see cref="Threshold"/>,
/// la ventana esta congelada: se apunta en el registro de diagnostico que fase iba (lo que dice
/// <see cref="DiagLog.Phase"/>) y que operacion del Dispatcher estaba corriendo (el metodo, y para
/// un await el metodo async que continuaba). La pila administrada de otro hilo no se puede leer
/// desde dentro del proceso en .NET, asi que esa operacion es lo mas cercano. Al volver, se apunta
/// cuanto duro.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();
    private volatile DispatcherOperation? _running;
    private readonly Stopwatch _runningClock = new();

    public TimeSpan Threshold { get; }
    public TimeSpan Interval { get; }

    /// <summary>Cuantas congelaciones ha visto (para las pruebas y el resumen).</summary>
    public int Freezes { get; private set; }

    /// <summary>
    /// La ventana principal (su HWND). Con ella el vigilante ve tambien el otro «congelado»: la principal
    /// visible pero desactivada por un dialogo modal que no se ve (el hilo responde, pero no se puede hacer
    /// nada). Se comprueba con Win32 desde este hilo, sin tocar el de la interfaz.
    /// </summary>
    public IntPtr MainWindow { get; set; }

    /// <summary>Que hacer si pasa (en el hilo de la interfaz): volver a enseñar y traer delante el dialogo.</summary>
    public Action? RecoverHiddenDialog { get; set; }

    /// <summary>Cuanto tiene que durar antes de apuntarlo y arreglarlo.</summary>
    public TimeSpan HiddenDialogAfter { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Veces que se ha visto la principal bloqueada por un dialogo que no se veia.</summary>
    public int HiddenDialogs { get; private set; }

    private readonly Stopwatch _blocked = new();
    private bool _blockedLogged;

    public UiWatchdog(Dispatcher dispatcher, TimeSpan? threshold = null, TimeSpan? interval = null)
    {
        _dispatcher = dispatcher;
        Threshold = threshold ?? TimeSpan.FromSeconds(2);
        Interval = interval ?? TimeSpan.FromMilliseconds(500);
        // Que operacion corre: las ganchos se llaman en el hilo de la interfaz, y solo guardan una referencia.
        _dispatcher.Hooks.OperationStarted += OnStarted;
        _dispatcher.Hooks.OperationCompleted += OnEnded;
        _dispatcher.Hooks.OperationAborted += OnEnded;
        _thread = new Thread(Run) { IsBackground = true, Name = "sOCLucia.UiWatchdog", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        _running = e.Operation;
        _runningClock.Restart();
    }

    private void OnEnded(object? sender, DispatcherHookEventArgs e)
    {
        if (ReferenceEquals(_running, e.Operation)) _running = null;
    }

    private void Run()
    {
        var cancel = _stop.Token;
        while (!cancel.IsCancellationRequested)
        {
            using var answered = new ManualResetEventSlim();
            var clock = Stopwatch.StartNew();
            try { _dispatcher.BeginInvoke(DispatcherPriority.Input, answered.Set); }
            catch (Exception) { return; }   // el Dispatcher se ha cerrado
            if (!Wait(answered, Threshold, cancel))
            {
                if (cancel.IsCancellationRequested || _dispatcher.HasShutdownStarted) return;
                Freezes++;
                DiagLog.Write("ui", $"VENTANA CONGELADA: el hilo de la interfaz no responde desde hace {clock.Elapsed.TotalSeconds:0.0} s. " +
                                    $"Fase: {DiagLog.Phase}. Operacion en curso: {Describe(_running)}" +
                                    (_running is not null ? $" (lleva {_runningClock.Elapsed.TotalSeconds:0.0} s)" : string.Empty) +
                                    ". La pila del hilo no se puede leer desde dentro del proceso.");
                // Un segundo aviso a los 10 s, por si la operacion ha cambiado (un bucle que encadena varias).
                if (!Wait(answered, TimeSpan.FromSeconds(10) - clock.Elapsed, cancel) && !cancel.IsCancellationRequested)
                    DiagLog.Write("ui", $"sigue congelada ({clock.Elapsed.TotalSeconds:0.0} s). Fase: {DiagLog.Phase}. Operacion en curso: {Describe(_running)}");
                Wait(answered, Timeout.InfiniteTimeSpan, cancel);
                if (cancel.IsCancellationRequested) return;
                DiagLog.Write("ui", $"la ventana vuelve a responder tras {clock.Elapsed.TotalSeconds:0.0} s congelada");
                Mlflow.MlflowTelemetry.Current.Freeze(clock.Elapsed.TotalSeconds);
            }
            CheckHiddenDialog();
            if (cancel.WaitHandle.WaitOne(Interval)) return;
        }
    }

    private void CheckHiddenDialog()
    {
        if (MainWindow == IntPtr.Zero) return;
        var stuck = IsWindowVisible(MainWindow) && !IsWindowEnabled(MainWindow) && !AnyUsableWindow();
        if (!stuck)
        {
            if (_blockedLogged)
                DiagLog.Write("ui", $"la ventana principal vuelve a estar disponible tras {_blocked.Elapsed.TotalSeconds:0.0} s");
            _blocked.Reset();
            _blockedLogged = false;
            return;
        }
        if (!_blocked.IsRunning) _blocked.Restart();
        if (_blockedLogged || _blocked.Elapsed < HiddenDialogAfter) return;
        _blockedLogged = true;
        HiddenDialogs++;
        DiagLog.Write("ui", $"VENTANA BLOQUEADA: la principal esta desactivada por un dialogo que no se ve desde hace {_blocked.Elapsed.TotalSeconds:0.0} s (el hilo responde). Fase: {DiagLog.Phase}. Se vuelve a enseñar el dialogo.");
        if (RecoverHiddenDialog is { } recover)
            try { _dispatcher.BeginInvoke(recover); } catch (Exception) { }
    }

    /// <summary>Si alguna ventana de este proceso (que no sea la principal) se ve y se puede usar: el dialogo que la desactiva.</summary>
    private bool AnyUsableWindow()
    {
        var pid = (uint)Environment.ProcessId;
        var found = false;
        EnumWindows((h, _) =>
        {
            if (h == MainWindow) return true;
            GetWindowThreadProcessId(h, out var owner);
            if (owner == pid && IsWindowVisible(h) && IsWindowEnabled(h) && !IsIconic(h) && GetWindowTextLength(h) > 0)
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);

    private static bool Wait(ManualResetEventSlim e, TimeSpan time, CancellationToken cancel)
    {
        try { return e.Wait(time < TimeSpan.Zero && time != Timeout.InfiniteTimeSpan ? TimeSpan.Zero : time, cancel); }
        catch (OperationCanceledException) { return false; }
    }

    private static readonly FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ArgsField = typeof(DispatcherOperation).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Una operacion del Dispatcher en palabras: su prioridad y el metodo que ejecuta. Si es la
    /// continuacion de un await (el contexto de sincronizacion manda el MoveNext como argumento), el
    /// metodo async que sigue, p. ej. «MainWindow.AnswerLoopAsync».
    /// </summary>
    public static string Describe(DispatcherOperation? operation)
    {
        if (operation is null) return "ninguna operacion del Dispatcher (puede ser un mensaje de Windows: un clic, una tecla, pintar, o un dialogo del sistema)";
        try
        {
            var method = MethodField?.GetValue(operation) as Delegate;
            var args = ArgsField?.GetValue(operation);
            var target = args as Delegate ?? (args as object[])?.OfType<Delegate>().FirstOrDefault();
            var text = $"{operation.Priority}: {Name(method)}";
            if (target is not null && !ReferenceEquals(target, method))
                text += " -> " + Name(target);
            return text;
        }
        catch (Exception ex)
        {
            return $"{operation.Priority} ({ex.GetType().Name})";
        }
    }

    private static readonly Regex StateMachine = new(@"([\w.]+)\+<(\w+)>d__\d+", RegexOptions.Compiled);

    internal static string Name(Delegate? d)
    {
        if (d is null) return "?";
        var targetType = d.Target?.GetType().FullName ?? string.Empty;
        var async = StateMachine.Match(targetType);
        if (async.Success)
            return $"{async.Groups[1].Value.Split('.').Last()}.{async.Groups[2].Value} (async)";
        var type = d.Method.DeclaringType;
        var owner = type?.DeclaringType is { } outer && type.Name.StartsWith('<') ? outer : type;
        var name = d.Method.Name;
        var lambda = Regex.Match(name, @"^<(\w+)>");
        return $"{owner?.Name ?? "?"}.{(lambda.Success ? lambda.Groups[1].Value + " (lambda)" : name)}";
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _dispatcher.Hooks.OperationStarted -= OnStarted;
            _dispatcher.Hooks.OperationCompleted -= OnEnded;
            _dispatcher.Hooks.OperationAborted -= OnEnded;
        }
        catch (Exception) { }
    }
}
