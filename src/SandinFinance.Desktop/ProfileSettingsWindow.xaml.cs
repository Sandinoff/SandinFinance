using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class ProfileSettingsWindow : Window
{
    private readonly ProfileStore store;
    public ProfileSettingsWindow(ProfileStore store)
    {
        this.store = store;
        InitializeComponent();
        NameText.Text = store.Load().Name;
        DirectoryText.Text = store.DirectoryPath;
    }
    private void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PinWindow(store, PinMode.Change) { Owner = this };
            if (dialog.ShowDialog() == true) StatusText.Text = "PIN обновлён. Новый код будет действовать при следующем входе.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { StatusText.Text = ex.Message; }
    }
}
