using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class App : Application
{
    private Mutex? instanceLock;
    private bool ownsLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A host may supply an isolated main window before the startup event is dispatched.
        if (MainWindow is not null) return;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) { MessageBox.Show("Не удалось найти папку «Документы».", "SandinFinance"); Shutdown(1); return; }
        var directory = ProfileStore.DefaultDirectory(documents);
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandinFinance");
        if (e.Args.Length == 2 && e.Args[0] == "--data-dir") { directory = Path.GetFullPath(e.Args[1]); legacy = null; }
        else if (e.Args.Length != 0)
        {
            MessageBox.Show("Допустимый параметр: --data-dir <папка>", "SandinFinance");
            Shutdown(1);
            return;
        }
        // Only one writer may use the same budget directory at a time.
        var identity = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        instanceLock = new Mutex(false, @"Local\SandinFinance." + suffix);
        try { ownsLock = instanceLock.WaitOne(0); }
        catch (AbandonedMutexException) { ownsLock = true; }
        if (!ownsLock)
        {
            MessageBox.Show("SandinFinance уже открыт с этой папкой данных. Используйте открытое окно.", "SandinFinance");
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var profileStore = ProfileStore.Initialize(directory, legacy);
            var mode = profileStore.Load().HasPin ? PinMode.Login : PinMode.Setup;
            var login = new PinWindow(profileStore, mode);
            if (login.ShowDialog() != true) { Shutdown(); return; }
            MainWindow = new MainWindow(directory, profileStore);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            MessageBox.Show($"Не удалось открыть профиль. Данные не сброшены.\n{ex.Message}", "SandinFinance");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsLock) instanceLock!.ReleaseMutex();
        instanceLock?.Dispose();
        base.OnExit(e);
    }
}
