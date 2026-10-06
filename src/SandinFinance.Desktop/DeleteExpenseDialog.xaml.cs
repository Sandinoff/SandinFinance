using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class DeleteExpenseDialog : Window
{
    public DeleteExpenseDialog(BudgetEntry entry)
    {
        InitializeComponent();
        ExpenseText.Text = $"{entry.CategoryName} · {entry.AmountText}";
        DetailText.Text = $"{entry.DetailText}\n{entry.DateText}";
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
