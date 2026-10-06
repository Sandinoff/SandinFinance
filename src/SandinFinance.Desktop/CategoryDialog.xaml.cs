using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class CategoryDialog : Window
{
    private readonly CategoryStore store;
    private readonly List<Category> existing;
    private readonly CategoryKind kind;
    private readonly Category? editing;
    private readonly ObservableCollection<Merchant> merchants = [];
    public Category? SavedCategory { get; private set; }

    public CategoryDialog(CategoryStore store, List<Category> existing, CategoryKind kind, Category? editing = null)
    {
        this.store = store;
        this.existing = existing;
        this.kind = editing?.Kind ?? kind;
        this.editing = editing;
        InitializeComponent();
        IconInput.ItemsSource = CategoryAppearance.Icons;
        var colors = CategoryAppearance.Colors.ToList();
        if (editing is not null && !colors.Any(color => color.Hex == editing.ColorHex)) colors.Add(new(editing.ColorHex, "Текущий цвет"));
        ColorInput.ItemsSource = colors;
        MerchantsList.ItemsSource = merchants;
        KindText.Text = this.kind == CategoryKind.Expense ? "Расходы" : "Доходы";
        MerchantsHeading.Text = this.kind == CategoryKind.Expense ? "Магазины" : "Источники дохода";
        if (editing is not null)
        {
            HeadingText.Text = "Настройка категории";
            NameInput.Text = editing.Name;
            SaveButton.Content = "Сохранить";
            foreach (var merchant in editing.Merchants) merchants.Add(merchant);
        }
        IconInput.SelectedValue = editing?.IconKey ?? (this.kind == CategoryKind.Expense ? "cart" : "work");
        ColorInput.SelectedValue = editing?.ColorHex ?? "#12796B";
        CustomColorInput.Text = editing?.ColorHex ?? "#12796B";
        Loaded += (_, _) => NameInput.Focus();
        UpdatePreview();
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (ErrorText is not null) ErrorText.Text = "";
        UpdatePreview();
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomColorInput is not null && ColorInput.SelectedItem is CategoryColor color) CustomColorInput.Text = color.Hex;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (CustomColorInput is null || PreviewBadge is null || MerchantsHeading is null || MerchantInput is null) return;
        var previewCategory = new Category { Name = NameInput.Text, Kind = kind, IconKey = IconInput.SelectedValue as string ?? "cart" };
        MerchantsHeading.Text = previewCategory.MerchantGroupLabel;
        MerchantInput.ToolTip = previewCategory.MerchantPrompt;
        PreviewName.Text = string.IsNullOrWhiteSpace(NameInput.Text) ? "Новая категория" : NameInput.Text.Trim();
        if (IconInput.SelectedItem is CategoryIcon icon) PreviewIcon.Text = icon.Glyph;
        if (System.Text.RegularExpressions.Regex.IsMatch(CustomColorInput.Text, "^#[0-9a-fA-F]{6}$"))
            PreviewBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CustomColorInput.Text));
    }

    private bool AddMerchant()
    {
        var merchant = new Merchant { Name = MerchantInput.Text.Trim() };
        try
        {
            merchant.Validate();
            if (merchants.Any(m => string.Equals(m.Name, merchant.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Такое название уже добавлено.");
            merchants.Add(merchant);
            MerchantInput.Clear();
            ErrorText.Text = "";
            return true;
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; return false; }
    }

    private void AddMerchant_Click(object sender, RoutedEventArgs e) => AddMerchant();
    private void SetMerchantIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Merchant merchant }) return;
        try
        {
            var png = MerchantIconLoader.SelectPng(this);
            if (png is not null) merchants[merchants.IndexOf(merchant)] = merchant with { PngIconBase64 = png };
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        { ErrorText.Text = $"Не удалось загрузить PNG-иконку. {ex.Message}"; }
    }
    private void RemoveMerchant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Merchant merchant }) merchants.Remove(merchant);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!string.IsNullOrWhiteSpace(MerchantInput.Text) && !AddMerchant()) return;
        var category = new Category
        {
            Id = editing?.Id ?? Guid.NewGuid(), Name = NameInput.Text.Trim(), Kind = kind,
            ColorHex = CustomColorInput.Text.Trim().ToUpperInvariant(), IconKey = IconInput.SelectedValue as string ?? "",
            Merchants = merchants.ToList()
        };
        try
        {
            category.Validate();
            store.Save(editing is null ? [.. existing, category] : existing.Select(c => c.Id == category.Id ? category : c).ToList());
            SavedCategory = category;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = ex is ArgumentException ? ex.Message : $"Не удалось сохранить категорию. {ex.Message}";
        }
    }
}
