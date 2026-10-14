using System.Windows;
using SandinFinance.Core;
namespace SandinFinance.Desktop;
public partial class DeleteRecurringPaymentDialog : Window
{
    public DeleteRecurringPaymentDialog(RecurringPayment payment) { InitializeComponent(); NameText.Text = payment.Name; }
    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
