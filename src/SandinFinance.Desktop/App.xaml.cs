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
    private ProfileLocationStore? profileLocations;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A host may supply an isolated main window before the startup event is dispatched.
        if (MainWindow is not null) return;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) { MessageBox.Show("Не удалось найти папку «Документы».", "SandinFinance"); Shutdown(1); return; }
        string? directory = null;
        string profileName = "Профиль";
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandinFinance");
        if (e.Args.Length == 2 && e.Args[0] == "--data-dir") { directory = Path.GetFullPath(e.Args[1]); legacy = null; }
        else if (e.Args.Length != 0)
        {
            MessageBox.Show("Допустимый параметр: --data-dir <папка>", "SandinFinance");
            Shutdown(1);
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (e.Args.Length == 0)
            {
                profileLocations = new ProfileLocationStore(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                directory = profileLocations.LoadDirectory();
            }
            directory ??= ProfileStore.FindExistingDirectory(documents);
            var importLegacy = directory is null && legacy is not null &&
                (File.Exists(ProfileLayout.ReadFile(legacy, "accounts.json")) || File.Exists(ProfileLayout.ReadFile(legacy, "categories.json")));
            if (directory is null || !File.Exists(Path.Combine(directory, "profile.json")))
            {
                var welcome = new WelcomeWindow();
                if (welcome.ShowDialog() != true) { Shutdown(); return; }
                profileName = welcome.ProfileName;
                if (directory is null)
                {
                    directory = ProfileStore.DirectoryForName(documents, profileName);
                    if (!importLegacy) legacy = null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show($"Не удалось подготовить профиль.\n{ex.Message}", "SandinFinance");
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
            var profileStore = ProfileStore.Initialize(directory, legacy, profileName);
            var mode = profileStore.Load().HasPin ? PinMode.Login : PinMode.Setup;
            var login = new PinWindow(profileStore, mode);
            if (login.ShowDialog() != true) { Shutdown(); return; }
            profileLocations?.SaveDirectory(directory);
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

    public void ChangeProfileDirectory(MainWindow current, string source, string destination)
    {
        if (MainWindow != current) throw new InvalidOperationException("Нельзя перенести профиль неактивного окна.");
        destination = Path.GetFullPath(destination);
        var identity = destination.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var nextLock = new Mutex(false, @"Local\SandinFinance." + suffix);
        var acquired = false;
        try
        {
            try { acquired = nextLock.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Выбранный профиль открыт в другом окне.");
            ProfileRelocation.Copy(source, destination);
            var replacement = new MainWindow(destination, new ProfileStore(destination));
            try { profileLocations?.SaveDirectory(destination); }
            catch { replacement.Close(); throw; }
            var oldLock = instanceLock;
            var ownedOldLock = ownsLock;
            var previousMode = ShutdownMode;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MainWindow = replacement;
            instanceLock = nextLock;
            ownsLock = true;
            acquired = false;
            current.Close();
            if (ownedOldLock) oldLock!.ReleaseMutex();
            oldLock?.Dispose();
            replacement.Show();
            ShutdownMode = previousMode;
        }
        finally { if (acquired) { nextLock.ReleaseMutex(); nextLock.Dispose(); } else if (instanceLock != nextLock) nextLock.Dispose(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsLock) instanceLock!.ReleaseMutex();
        instanceLock?.Dispose();
        base.OnExit(e);
    }
}
