using System.Threading;
using System.Windows;

namespace DesktopTodo;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\DesktopTodo.SingleInstance.9C5D2F31";
    private const string ShowEventName = @"Local\DesktopTodo.ShowExisting.9C5D2F31";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private Thread? _signalThread;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _signalThread = new Thread(WaitForShowSignal)
        {
            IsBackground = true,
            Name = "DesktopTodo single-instance listener"
        };
        _signalThread.Start();

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
                showEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void WaitForShowSignal()
    {
        while (!_exiting)
        {
            _showEvent?.WaitOne();
            if (_exiting) break;
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                    window.ShowFromExternalLaunch();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _showEvent?.Set();
        _signalThread?.Join(500);
        _showEvent?.Dispose();
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
