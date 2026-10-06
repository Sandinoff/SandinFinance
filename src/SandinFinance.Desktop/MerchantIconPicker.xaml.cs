using System.Windows;
using Microsoft.Win32;

namespace SandinFinance.Desktop;
public partial class MerchantIconPicker : Window
{
    public string? SelectedPng { get; private set; }
    public MerchantIconPicker() { InitializeComponent(); IconsList.ItemsSource = MerchantIconCatalog.Choices; }
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MerchantIconChoice icon }) return;
        SelectedPng = icon.PngIconBase64; DialogResult = true;
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "PNG-иконка магазина", Filter = "PNG-изображения (*.png)|*.png", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try { SelectedPng = MerchantIconLoader.LoadPng(picker.FileName); DialogResult = true; }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException) { ErrorText.Text = ex.Message; }
    }
}
