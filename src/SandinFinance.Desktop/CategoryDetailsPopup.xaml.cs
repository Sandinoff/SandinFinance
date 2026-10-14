using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class CategoryDetailsPopup : UserControl
{
    public event EventHandler? CloseRequested;
    public CategoryDetailsPopup(CategoryTotal category, IReadOnlyList<BudgetEntry> entries, CategoryKind kind, DateRange range, string breakdownHeading)
    {
        InitializeComponent();
        CategoryNameText.Text = category.Name;
        CategoryIcon.Text = category.IconGlyph;
        if (category.IconGlyph == CategoryAppearance.Icons.Single(icon => icon.Key == "interest").Glyph)
        {
            CategoryIcon.Text = "%";
            CategoryIcon.FontFamily = (FontFamily)FindResource("AppFont");
        }
        CategoryBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(category.ColorHex));
        PeriodText.Text = $"{(kind == CategoryKind.Expense ? "Расходы" : "Доходы")} · {range.Start:dd.MM} — {range.End:dd.MM.yyyy}";
        AmountText.Text = category.AmountText;
        ShareText.Text = category.ShareText;
        CountText.Text = $"· операций: {category.Count}";
        BreakdownHeading.Text = breakdownHeading;
        BreakdownList.ItemsSource = category.Merchants.Select(merchant => new BreakdownRow(merchant, category.Amount == 0 ? 0 : (double)(merchant.Amount / category.Amount) * 260, category.ColorHex)).ToList();
        CategoryEntriesList.ItemsSource = entries.Take(3).ToList();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private sealed record BreakdownRow(MerchantTotal Merchant, double BarWidth, string ColorHex);
}
