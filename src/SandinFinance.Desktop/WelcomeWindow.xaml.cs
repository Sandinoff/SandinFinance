using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class WelcomeWindow : Window
{
    public string ProfileName { get; private set; } = "";
    public WelcomeWindow()
    {
        InitializeComponent();
        NameInput.TextChanged += (_, _) => ErrorText.Text = "";
        Loaded += (_, _) => NameInput.Focus();
    }
    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProfileName = ProfileStore.ValidateProfileName(NameInput.Text);
            DialogResult = true;
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; NameInput.Focus(); }
    }
}
