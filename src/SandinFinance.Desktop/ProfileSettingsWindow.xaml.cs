using System.Windows;
using Microsoft.Win32;
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

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = store.DirectoryPath, UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException) { StatusText.Text = ex.Message; }
    }

    public int ExportExpenses(string filename) => ExpenseExcelExporter.Export(filename, new AccountStore(store.DirectoryPath).Load(), new CategoryStore(store.DirectoryPath).Load());

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Title = "Сохранить расходы в Excel", Filter = "Книга Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true,
            FileName = $"Расходы_{DateTime.Today:yyyy-MM-dd}.xlsx", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true) return;
        ExportButton.IsEnabled = false;
        StatusText.Text = "Формируем таблицу…";
        try
        {
            var accounts = new AccountStore(store.DirectoryPath).Load();
            var categories = new CategoryStore(store.DirectoryPath).Load();
            var count = await Task.Run(() => ExpenseExcelExporter.Export(picker.FileName, accounts, categories));
            StatusText.Text = $"Сохранено расходов: {count}.\n{picker.FileName}";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Xml.XmlException) { StatusText.Text = $"Не удалось сохранить Excel. {ex.Message}"; }
        finally { ExportButton.IsEnabled = true; }
    }
}
