using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class DeleteAccountDialog : Window
{
    public DeleteAccountDialog(Account account)
    {
        InitializeComponent();
        AccountNameText.Text = account.Name;
        BalanceText.Text = account.IsCredit ? $"{account.BalanceLabel}: {account.BalanceText}\n{account.DebtText}" : $"{account.BalanceLabel}: {account.BalanceText}";
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
