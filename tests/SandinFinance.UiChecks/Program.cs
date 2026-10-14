using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandinFinance.Desktop;
using SandinFinance.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), ".local", "ui-checks", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            CheckWelcome(root);
            var window = new MainWindow(root);
            app.MainWindow = window;
            window.Show();
            Assert(window.WindowStyle == WindowStyle.None && ((Button)window.Template.FindName("ChromeMinimize", window)).IsVisible && ((Button)window.Template.FindName("ChromeClose", window)).IsVisible, "Main window has custom minimize and close controls without native chrome");
            SystemCommands.MinimizeWindowCommand.Execute(null, window);
            SettleAnimations();
            Assert(window.WindowState == WindowState.Minimized, "Custom minimize command works");
            window.WindowState = WindowState.Normal;
            ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Add(window, "Наличные", 0, "5000,50", "0", root, checkErrors: true);
            Add(window, "Дебетовая карта", 1, "2499.50", "0", root);
            Add(window, "Накопительный счёт", 3, "12500", "0", root);
            Add(window, "Кредитка", 2, "1500,50", "100000", root);
            Assert(((ItemsControl)window.FindName("AccountsList")).Items.Count == 4, "All four account types appear");
            Assert(((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(20000m), "Own funds total");
            Assert(((TextBlock)window.FindName("NetText")).Text == Money.Format(18499.50m), "Net total");
            Edit(window, "Кредитка", "Карта 55 дней", 55, GracePeriodRule.EndOfNextMonth);
            AddPurchase(window, "Карта 55 дней", 100.01m);
            Edit(window, "Карта 55 дней", "Карта 120 дней", 120, GracePeriodRule.MonthEndPlusDays);
            AddPurchase(window, "Карта 120 дней", 200m);
            AddPurchase(window, "Карта 120 дней", 300m);
            var card = new AccountStore(root).Load().Single(a => a.Name == "Карта 120 дней");
            Assert(card.Purchases[0].Rule == GracePeriodRule.EndOfNextMonth && card.Purchases[1].Rule == GracePeriodRule.MonthEndPlusDays,
                "Existing purchase keeps its terms after card editing");
            Assert(card.Debt == 2100.51m, "Purchases increase credit debt");
            Assert(card.PurchaseSummaries.Count() == 2 && card.PurchaseSummaries.Any(s => s.Payments.Contains(Money.Format(500m))), "Monthly purchases are grouped into one payment");
            var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
            Add(window, "С ежедневными процентами", 3, "10000", "0", root,
                rate: DateTime.IsLeapYear(yesterday.Year) ? "36,6" : "36,5", opening: yesterday);
            var accrued = new AccountStore(root).Load().Single(a => a.Name == "С ежедневными процентами");
            Assert(accrued.CreditedInterest == 10m, "Backdated savings accrue a completed day through UI");
            Assert(((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(30010m), "Interest appears in totals");
            RunDialog<AccountDialog>(window, () => FindAction(window, "Настроить", "С ежедневными процентами").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
            {
                ((TextBox)dialog.FindName("RateInput")).Text = "15";
                ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dialog.CreatedAccount?.CreditedInterest == 10m && dialog.CreatedAccount.AnnualInterestRate == 15m, "Rate edit keeps credited interest");
            });
            window.UpdateLayout();
            FindVisibilityButton(window, "Наличные").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(((ItemsControl)window.FindName("AccountsList")).Items.Count == 4, "Hidden account is filtered out");
            Assert(new AccountStore(root).Load().Single(a => a.Name == "Наличные").IsHidden, "Eye button persists hidden state");
            Assert(((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(30010m), "Hidden account remains in totals");
            ((CheckBox)window.FindName("ShowHiddenInput")).IsChecked = true;
            Assert(((ItemsControl)window.FindName("AccountsList")).Items.Count == 5, "Show hidden restores account for interaction");
            window.UpdateLayout();
            var target = FindDropCard(window, "Наличные");
            var moving = new AccountStore(root).Load().Single(a => a.Name == "Карта 120 дней");
            var drop = CreateDropEvent(target, moving.Id);
            target.RaiseEvent(drop);
            Assert(new AccountStore(root).Load()[0].Id == moving.Id, "Drop moves and persists card before target");
            Capture(window, Path.Combine(root, "accounts.png"));
            CheckCategories(window, root);
            window.Close();
            var reopened = new MainWindow(root);
            reopened.Show();
            ((Button)reopened.FindName("CategoriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reopenedCategories = (CategoriesView)reopened.FindName("CategoriesPage");
            Assert(((ItemsControl)reopenedCategories.FindName("CategoriesList")).Items.Count == 1, "Expense category survives restart");
            ((Button)reopened.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(((ItemsControl)reopened.FindName("AccountsList")).Items[0] is Account { Name: "Карта 120 дней" }, "Reordered card stays first after restart");
            Assert(((ItemsControl)reopened.FindName("AccountsList")).Items.Count == 4, "Hidden account stays hidden after restart");
            ((CheckBox)reopened.FindName("ShowHiddenInput")).IsChecked = true;
            reopened.UpdateLayout();
            FindVisibilityButton(reopened, "Наличные").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((CheckBox)reopened.FindName("ShowHiddenInput")).IsChecked = false;
            Assert(((ItemsControl)reopened.FindName("AccountsList")).Items.Count == 5 && !new AccountStore(root).Load().Single(a => a.Name == "Наличные").IsHidden, "Eye restores hidden account persistently");
            Assert(new AccountStore(root).Load().Single(a => a.Name == "С ежедневными процентами").CreditedInterest == 10m, "Restart does not double interest");
            reopened.Close();
            CheckHome(root, app);
            CheckTransfers(root, app);
            CheckProfileAndPin(root, app);
            CheckAccountDeletion(root, app);
            CheckExpenseDeletion(root, app);
            CheckExpenseEdit(root, app);
            CheckFinanceUpdates(root, app);
            CheckBudgetCardDisplay(root, app);
            CheckMerchantLibrary(root, app);
            CheckRecurringPayments(root, app);
            CheckCreditReminders(root, app);
            CheckProfileFolderChange(root, app);
            var badDirectory = Path.Combine(root, "corrupt");
            Directory.CreateDirectory(badDirectory);
            File.WriteAllText(Path.Combine(badDirectory, "accounts.json"), "broken json");
            var corruptWindow = new MainWindow(badDirectory);
            corruptWindow.Show();
            Assert(!((Button)corruptWindow.FindName("AddButton")).IsEnabled, "Corrupt data disables writes");
            File.WriteAllText(Path.Combine(badDirectory, "categories.json"), "broken json");
            ((Button)corruptWindow.FindName("CategoriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!((Button)((CategoriesView)corruptWindow.FindName("CategoriesPage")).FindName("CreateButton")).IsEnabled, "Corrupt categories disable writes");
            corruptWindow.Close();
            app.Shutdown();
            Console.WriteLine($"PASS UI creation, validation, totals, reopening and corrupt-file protection.\nScreenshots: {root}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckProfileAndPin(string root, App app)
    {
        var directory = Path.Combine(root, "profile-ui");
        var store = ProfileStore.Initialize(directory);
        var setup = new PinWindow(store, PinMode.Setup);
        RunPin(setup, dialog =>
        {
            EnterPin(dialog, "0012");
            EnterPin(dialog, "0013");
            Assert(!store.Load().HasPin, "Mismatched confirmation does not set PIN");
            EnterPin(dialog, "0012");
            EnterPin(dialog, "0012");
        });
        Assert(store.VerifyPin("0012"), "Virtual setup keys establish PIN");
        var login = new PinWindow(store, PinMode.Login);
        RunPin(login, dialog =>
        {
            Capture(dialog, Path.Combine(root, "pin-login.png"));
            Assert(dialog.WindowStyle == WindowStyle.None && !((Button)dialog.Template.FindName("ChromeMinimize", dialog)).IsVisible && ((Button)dialog.Template.FindName("ChromeClose", dialog)).IsVisible, "PIN screen has only a custom close control");
            Assert(dialog.Icon is not null, "Login window uses brand icon");
            EnterPin(dialog, "9999");
            Assert(!string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Wrong PIN keeps login locked");
            EnterPin(dialog, "0012");
        });
        var closeProbe = new PinWindow(store, PinMode.Login);
        closeProbe.Show();
        Assert(!((Button)closeProbe.Template.FindName("ChromeMinimize", closeProbe)).IsVisible, "PIN minimize control stays hidden");
        SystemCommands.CloseWindowCommand.Execute(null, closeProbe);
        Assert(!closeProbe.IsVisible, "Custom close command closes the PIN screen");
        var window = new MainWindow(directory, store);
        app.MainWindow = window;
        window.Show();
        Assert(((TextBlock)window.FindName("ProfileNameText")).Text == "Sandin" && window.Icon is not null, "Profile name replaces footer quote and icon is applied");
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
        {
            var typeface = new Typeface(window.FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
            Assert(typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.Weight == weight && glyphs.CharacterToGlyphMap.ContainsKey(0x0416) && glyphs.CharacterToGlyphMap.ContainsKey(0x0451), "Embedded font resolves Cyrillic in every UI weight");
        }
        RunDialog<ProfileSettingsWindow>(window, () =>
        {
            var profileButton = (Button)window.FindName("ProfileButton");
            profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(profileButton.ContextMenu.IsOpen, "Clicking the profile opens the dropdown");
            profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!profileButton.ContextMenu.IsOpen, "Clicking the profile again closes the dropdown");
            profileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            profileButton.ContextMenu.UpdateLayout();
            CaptureElement(profileButton.ContextMenu, Path.Combine(root, "profile-menu.png"));
            ((MenuItem)profileButton.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }, settings =>
        {
            Assert(!((Button)window.FindName("ProfileButton")).ContextMenu.IsOpen, "Selecting settings closes the dropdown");
            Assert(((TextBox)settings.FindName("DirectoryText")).Text == store.DirectoryPath, "Settings show actual profile folder");
            Assert(((Button)settings.FindName("ExportButton")).Content as string == "Экспортировать в Excel", "Settings offer Excel export");
            Assert(settings.ExportExpenses(Path.Combine(root, "settings-expenses.xlsx")) == 0, "Settings export can create an empty expense workbook without PIN data");
            Capture(settings, Path.Combine(root, "profile-settings.png"));
            Exception? failure = null;
            settings.Dispatcher.BeginInvoke(new Action(() =>
            {
                var change = Application.Current.Windows.OfType<PinWindow>().Single();
                try { EnterPin(change, "0012"); EnterPin(change, "4321"); EnterPin(change, "4321"); }
                catch (Exception ex) { failure = ex; change.Close(); }
            }));
            ((Button)settings.FindName("ChangePinButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (failure is not null) throw failure;
            Assert(store.VerifyPin("4321") && !store.VerifyPin("0012"), "Settings change PIN using current code and confirmation");
            settings.Close();
        });
        Capture(window, Path.Combine(root, "profile-main.png"));
        var logo = (Grid)window.FindName("LogoHeader");
        logo.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        logo.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent });
        var frame = new System.Windows.Threading.DispatcherFrame();
        var animationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        animationTimer.Tick += (_, _) => { animationTimer.Stop(); frame.Continue = false; };
        animationTimer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Capture(window, Path.Combine(root, "logo-hover.png"));
        logo.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        window.Close();
    }

    private static void RunPin(PinWindow window, Action<PinWindow> interact)
    {
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { interact(window); }
            catch (Exception ex) { failure = ex; window.Close(); }
        }));
        var result = window.ShowDialog();
        if (failure is not null) throw failure;
        Assert(result == true, "PIN gate accepts only a completed successful flow");
    }

    private static void EnterPin(PinWindow window, string pin)
    {
        var keypad = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("Keypad");
        foreach (var digit in pin)
        {
            var button = keypad.Children.OfType<Button>().Single(b => b.Tag as string == digit.ToString());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private static void CheckHome(string root, App app)
    {
        var catalog = new CategoryStore(root);
        var categories = catalog.Load();
        var transport = new Category { Name = "Транспорт", IconKey = "car", ColorHex = "#B76A22", Merchants = [new() { Name = "Такси" }, new() { Name = "Бензин" }] };
        catalog.Save([.. categories, transport]);
        var window = new MainWindow(root);
        app.MainWindow = window;
        window.Show();
        var home = (HomeView)window.FindName("HomePage");
        Assert(home.Visibility == Visibility.Visible, "Home is the starting page");
        AddOperation(window, home, CategoryKind.Expense, "Наличные", "Питание", "800,25", "Магнит", root, checkErrors: true);
        AddOperation(window, home, CategoryKind.Expense, "Наличные", "Транспорт", "200", "Такси", root);
        AddOperation(window, home, CategoryKind.Expense, "Карта 120 дней", "Питание", "1000", "Пятёрочка", root);
        var cash = new AccountStore(root).Load().Single(a => a.Name == "Наличные");
        Assert(cash.CurrentBalance == 4000.25m, "Home expenses debit cash");
        Assert(new AccountStore(root).Load().Single(a => a.Name == "Карта 120 дней").Debt == 3100.51m, "Home credit expense increases debt");
        Assert(((TextBlock)home.FindName("TotalText")).Text == Money.Format(2600.26m), "Expense chart total includes legacy purchases once");
        var totals = ((ItemsControl)home.FindName("CategoryTotalsList")).Items.Cast<CategoryTotal>().ToList();
        Assert(totals[0].Name == "Питание" && totals[0].Amount == 1800.25m && totals.Count == 3, "Chart sorts categories descending");
        var canvas = (Canvas)home.FindName("PieCanvas");
        Assert(canvas.Children.Count == 3 && canvas.Children.OfType<System.Windows.Shapes.Shape>().All(s => s.ToolTip is string text && text.Length > 0), "Each chart sector has detail tooltip");
        Capture(window, Path.Combine(root, "home-expenses.png"));
        var transportSector = canvas.Children.OfType<System.Windows.Shapes.Shape>().Single(s => s.DataContext is CategoryTotal { Name: "Транспорт" });
        CheckCategoryPopup(home, () => transportSector.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }), dialog =>
        {
            Assert(((TextBlock)dialog.FindName("CategoryNameText")).Text == "Транспорт" && ((TextBlock)dialog.FindName("AmountText")).Text == Money.Format(200), "Clicked sector opens matching category totals");
            Assert(((TextBlock)dialog.FindName("BreakdownHeading")).Text == "Товары и услуги", "Transport detail uses goods and services");
            Assert(((ItemsControl)dialog.FindName("CategoryEntriesList")).Items.Cast<BudgetEntry>().Single().MerchantName == "Такси", "Category detail filters operations");
            CaptureElement(dialog, Path.Combine(root, "category-details.png"));
        });
        AddOperation(window, home, CategoryKind.Income, "Наличные", "Зарплата", "1200", "Работодатель", root);
        Assert(new AccountStore(root).Load().Single(a => a.Name == "Наличные").CurrentBalance == 5200.25m, "Home income credits cash");
        Assert(((TextBlock)home.FindName("TotalText")).Text == Money.Format(1210m), "Income total includes salary and accrued interest");
        ((RadioButton)home.FindName("CustomPeriodInput")).IsChecked = true;
        ((DatePicker)home.FindName("StartInput")).SelectedDate = DateTime.Today.AddDays(-7);
        ((DatePicker)home.FindName("EndInput")).SelectedDate = DateTime.Today.AddDays(-1);
        Assert(((ItemsControl)home.FindName("EntriesList")).Items.Count == 0, "Custom period excludes today's income");
        ((DatePicker)home.FindName("EndInput")).SelectedDate = DateTime.Today;
        Assert(((ItemsControl)home.FindName("EntriesList")).Items.Cast<BudgetEntry>().Count(e => !e.IsTransfer && !e.IsInterest) == 1, "Custom period includes last day's income");
        window.Close();
        var reopened = new MainWindow(root);
        reopened.Show();
        var reopenedHome = (HomeView)reopened.FindName("HomePage");
        Assert(((TextBlock)reopenedHome.FindName("TotalText")).Text == Money.Format(2600.26m), "Journal and chart survive restart");
        reopened.Close();
    }

    private static void AddOperation(MainWindow window, HomeView home, CategoryKind kind, string accountName, string categoryName,
        string amount, string merchant, string root, bool checkErrors = false)
    {
        var button = (Button)home.FindName("AddOperationButton");
        RunDialog<TransactionDialog>(window, () =>
        {
            ((TabControl)home.FindName("KindTabs")).SelectedIndex = kind == CategoryKind.Income ? 1 : 0;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, dialog =>
        {
            var accounts = (ComboBox)dialog.FindName("AccountInput");
            if (checkErrors) CheckTimeFormatting((TextBox)dialog.FindName("TimeInput"));
            var categories = (ComboBox)dialog.FindName("CategoryInput");
            accounts.SelectedItem = accounts.Items.Cast<Account>().Single(a => a.Name == accountName);
            categories.SelectedItem = categories.Items.Cast<Category>().Single(c => c.Name == categoryName);
            var merchantInput = (ComboBox)dialog.FindName("MerchantInput");
            merchantInput.ApplyTemplate();
            var editor = (TextBox)merchantInput.Template.FindName("PART_EditableTextBox", merchantInput);
            Assert(editor.IsVisible, "Merchant selector has a visible editable field");
            var savedMerchant = merchantInput.Items.Cast<Merchant>().FirstOrDefault(m => m.Name == merchant);
            if (savedMerchant is not null) merchantInput.SelectedItem = savedMerchant;
            else editor.Text = merchant;
            Assert(editor.Text == merchant && merchantInput.Text == merchant, "Selection and typed merchant names stay visible and synchronized");
            if (categoryName == "Транспорт")
            {
                Assert(((Label)dialog.FindName("MerchantLabel")).Content as string == "Товары и услуги (необязательно)", "Transport operation label is consistent");
                Capture(dialog, Path.Combine(root, "transport-transaction.png"));
            }
            if (checkErrors)
            {
                ((TextBox)dialog.FindName("AmountInput")).Text = "999999999";
                ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dialog.SavedTransaction is null && !string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Overspending rejected in form");
            }
            ((TextBox)dialog.FindName("AmountInput")).Text = amount;
            if (checkErrors) Capture(dialog, Path.Combine(root, "transaction-dialog.png"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedTransaction?.MerchantName == merchant && dialog.SavedTransaction.Kind == kind, "Operation form saves category, merchant and kind");
        });
    }

    private static void CheckCategories(MainWindow window, string root)
    {
        ((Button)window.FindName("CategoriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var page = (CategoriesView)window.FindName("CategoriesPage");
        Assert(page.Visibility == Visibility.Visible && ((ScrollViewer)window.FindName("AccountsScroll")).Visibility == Visibility.Collapsed, "Category navigation switches page");
        RunDialog<CategoryDialog>(window, () => ((Button)page.FindName("CreateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedCategory is null && !string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Blank category rejected");
            ((TextBox)dialog.FindName("NameInput")).Text = "Продукты";
            ((ComboBox)dialog.FindName("IconInput")).SelectedValue = "food";
            ((ComboBox)dialog.FindName("ColorInput")).SelectedValue = "#2478A8";
            ((TextBox)dialog.FindName("MerchantInput")).Text = "Пятёрочка";
            Capture(dialog, Path.Combine(root, "category-dialog.png"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedCategory?.Merchants.Single().Name == "Пятёрочка", "Merchant text is saved with category");
        });
        window.UpdateLayout();
        var initiallyCollapsed = FindExpanders(page).Single();
        Assert(!initiallyCollapsed.IsExpanded, "Subcategories start collapsed");
        initiallyCollapsed.IsExpanded = true;
        window.UpdateLayout();
        var add = FindCategoryAction(page, "Добавить", "Продукты");
        ((TextBox)add.Tag).Text = "Магнит";
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var expense = new CategoryStore(root).Load().Single();
        Assert(expense.ColorHex == "#2478A8" && expense.IconKey == "food" && expense.Merchants.Count == 2, "Color, icon and inline merchant saved");
        var iconPath = Path.Combine(root, "merchant-logo.png");
        CreatePngFixture(iconPath);
        var png = MerchantIconLoader.LoadPng(iconPath);
        var merchantId = expense.Merchants.Single(m => m.Name == "Магнит").Id;
        page.SetMerchantIcon(merchantId, png);
        File.Delete(iconPath);
        var restoredIcon = new CategoryStore(root).Load().Single().Merchants.Single(m => m.Id == merchantId);
        Assert(restoredIcon.PngIconBase64 == png && restoredIcon.HasPngIcon, "PNG copy survives removal of original file");
        var bitmap = new PngIconConverter().Convert(png, typeof(System.Windows.Media.ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert(bitmap is System.Windows.Media.Imaging.BitmapImage { IsFrozen: true }, "PNG renders as a frozen thumbnail");
        page.SetMerchantIcon(merchantId, null);
        Assert(!new CategoryStore(root).Load().Single().Merchants.Single(m => m.Id == merchantId).HasPngIcon, "Icon can be removed");
        page.SetMerchantIcon(merchantId, png);
        window.UpdateLayout();
        RunDialog<CategoryDialog>(window, () => FindCategoryAction(page, "Настроить", "Продукты").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((TextBox)dialog.FindName("NameInput")).Text = "Питание";
            ((TextBox)dialog.FindName("CustomColorInput")).Text = "#8040AA";
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedCategory?.Id == expense.Id && dialog.SavedCategory.Merchants.Count == 2, "Editing preserves category and merchants");
        });
        Capture(window, Path.Combine(root, "expense-categories.png"));
        ((TabControl)page.FindName("KindTabs")).SelectedIndex = 1;
        Assert(((ItemsControl)page.FindName("CategoriesList")).Items.Count == 0, "Income section is separate");
        RunDialog<CategoryDialog>(window, () => ((Button)page.FindName("CreateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((TextBox)dialog.FindName("NameInput")).Text = "Зарплата";
            ((TextBox)dialog.FindName("MerchantInput")).Text = "Работодатель";
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedCategory?.Kind == CategoryKind.Income, "Income category created in correct section");
        });
        Assert(new CategoryStore(root).Load().Count == 2, "Both category types persisted");
        ((TabControl)page.FindName("KindTabs")).SelectedIndex = 0;
        window.UpdateLayout();
        Assert(FindExpanders(page).All(e => !e.IsExpanded), "Switching kind collapses subcategories");
        FindExpanders(page).Single().IsExpanded = true;
        ((Button)window.FindName("HomeNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)window.FindName("CategoriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(FindExpanders(page).All(e => !e.IsExpanded), "Returning to categories collapses subcategories");
        var categoryStore = new CategoryStore(root);
        var original = categoryStore.Load();
        var transport = new Category { Name = "Транспорт", IconKey = "car", Merchants = [new Merchant { Name = "Бензин" }] };
        var empty = new Category { Name = "Без подкатегорий" };
        categoryStore.Save([.. original, transport, empty]);
        page.Refresh();
        window.UpdateLayout();
        Assert(FindExpanders(page).Single(e => e.DataContext is Category c && c.Id == empty.Id).Visibility == Visibility.Collapsed, "Empty subcategory section is hidden");
        Assert(FindExpanders(page).Single(e => e.DataContext is Category c && c.Id == transport.Id).Header as string == "Товары и услуги · 1", "Transport uses relevant heading");
        var targetCategory = FindCategoryDropCard(page, "Питание");
        targetCategory.RaiseEvent(CreateDropEvent(targetCategory, transport.Id, "SandinFinance.Category"));
        Assert(categoryStore.Load().First(c => c.Kind == CategoryKind.Expense).Id == transport.Id, "Category drop saves order");
        page.Refresh();
        Assert(((ItemsControl)page.FindName("CategoriesList")).Items[0] is Category { Name: "Транспорт" }, "Category order survives reload");
        categoryStore.Save(original);
        page.Refresh();
        ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        var transferButton = (Button)window.FindName("TransferButton");
        var addButton = (Button)window.FindName("AddButton");
        Assert(transferButton.ActualWidth == addButton.ActualWidth && transferButton.ActualHeight == addButton.ActualHeight, "Account action buttons have equal dimensions");
        var transferTop = transferButton.TranslatePoint(new Point(0, 0), window).Y;
        var addTop = addButton.TranslatePoint(new Point(0, 0), window).Y;
        Assert(Math.Abs(transferTop - addTop) < 0.5, "Account action buttons share the same top edge");
        Assert(transferButton.FontSize == 14 && addButton.FontSize == 14 && transferButton.VerticalContentAlignment == VerticalAlignment.Center && addButton.VerticalContentAlignment == VerticalAlignment.Center, "Account action text uses consistent size and alignment");
        Capture(window, Path.Combine(root, "account-button-alignment.png"));
        Assert(((ScrollViewer)window.FindName("AccountsScroll")).Visibility == Visibility.Visible, "Accounts navigation returns to accounts");
    }

    private static void CreatePngFixture(string path)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var index = (y * 32 + x) * 4;
            var white = x >= 8 && x <= 23 && y >= 8 && y <= 23;
            pixels[index] = white ? (byte)255 : (byte)75;
            pixels[index + 1] = white ? (byte)255 : (byte)130;
            pixels[index + 2] = white ? (byte)255 : (byte)30;
            pixels[index + 3] = 255;
        }
        var source = System.Windows.Media.Imaging.BitmapSource.Create(32, 32, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 128);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void CheckMerchantLibrary(string root, App app)
    {
        Assert(MerchantIconCatalog.Choices.Count == 13, "All supplied merchant PNG icons are embedded");
        foreach (var icon in MerchantIconCatalog.Choices) PngIcon.Validate(Convert.FromBase64String(icon.PngIconBase64));
        Assert(MerchantIconCatalog.ForName("Пятерочка") == MerchantIconCatalog.ForName("Пятёрочка") && MerchantIconCatalog.ForName("Красное&Белое") is not null, "Brand aliases resolve automatic merchant icons");
        var converted = new PngIconConverter().Convert(new Merchant { Name = "SPAR" }, typeof(System.Windows.Media.ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert(converted is BitmapImage, "Merchant badge uses bundled icon by name");
        var owner = new MainWindow(root); app.MainWindow = owner; owner.Show();
        RunDialog<MerchantIconPicker>(owner, () => new MerchantIconPicker { Owner = owner }.ShowDialog(), picker =>
        {
            Assert(((ItemsControl)picker.FindName("IconsList")).Items.Count == 13, "Gallery lists bundled icons");
            Capture(picker, Path.Combine(root, "merchant-icon-gallery.png"));
            picker.Close();
        });
        owner.Close();
    }

    private static void CheckWelcome(string root)
    {
        var welcome = new WelcomeWindow();
        Exception? failure = null;
        welcome.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Assert(welcome.WindowStyle == WindowStyle.None && !((Button)welcome.Template.FindName("ChromeMinimize", welcome)).IsVisible, "Welcome uses styled frameless chrome with only close");
                var input = (TextBox)welcome.FindName("NameInput");
                var button = (Button)welcome.FindName("ContinueButton");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(!string.IsNullOrEmpty(((TextBlock)welcome.FindName("ErrorText")).Text), "Empty name remains on welcome screen");
                input.Text = "../other";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(welcome.ProfileName == "", "Unsafe profile name is rejected");
                input.Text = "  Анна  ";
                Capture(welcome, Path.Combine(root, "welcome.png"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; welcome.Close(); }
        }));
        var result = welcome.ShowDialog();
        if (failure is not null) throw failure;
        Assert(result == true && welcome.ProfileName == "Анна", "Welcome accepts a trimmed user name");
    }

    private static void CheckProfileFolderChange(string root, App app)
    {
        var source = Path.Combine(root, "folder-change-source");
        var destination = Path.Combine(root, "folder-change-destination");
        var profile = ProfileStore.Initialize(source, name: "Анна");
        profile.SetInitialPin("2468");
        new AccountStore(source).Save([new Account { Name = "Тест", Type = AccountType.Cash, OpeningBalance = 1234.56m, OpeningDate = DateOnly.FromDateTime(DateTime.Today) }]);
        var before = File.ReadAllBytes(new AccountStore(source).FilePath);
        var owner = new MainWindow(source, profile);
        app.MainWindow = owner;
        owner.Show();
        RunDialog<ProfileSettingsWindow>(owner, () =>
        {
            var button = (Button)owner.FindName("ProfileButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((MenuItem)button.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }, settings =>
        {
            Assert(((Button)settings.FindName("ChangeFolderButton")).IsVisible, "Settings offer profile directory selection");
            Capture(settings, Path.Combine(root, "settings-folder-change.png"));
            settings.SelectProfileDirectory(destination);
        });
        Assert(!owner.IsVisible && app.MainWindow is MainWindow, "Folder change replaces the window that writes the old directory");
        Assert(new ProfileStore(destination).VerifyPin("2468") && new AccountStore(destination).Load().Single().CurrentBalance == 1234.56m, "Moved profile retains PIN and balance");
        Assert(File.ReadAllBytes(new AccountStore(source).FilePath).SequenceEqual(before), "Original profile remains unchanged as a recovery copy");
        Assert(((TextBlock)app.MainWindow.FindName("ProfileNameText")).Text == "Анна", "New window uses the relocated profile");
        Add((MainWindow)app.MainWindow, "После переноса", 0, "50", "0", destination);
        Assert(new AccountStore(destination).Load().Count == 2 && new AccountStore(source).Load().Count == 1, "Subsequent operations write only to the selected folder");
        app.MainWindow.Close();
    }

    private static void CheckAccountDeletion(string root, App app)
    {
        var directory = Path.Combine(root, "account-deletion");
        var store = new AccountStore(directory);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = new Account { Name = "Удаляемый счёт", OpeningBalance = 1000, OpeningDate = today };
        var second = new Account { Name = "Оставшийся счёт", OpeningBalance = 200, OpeningDate = today };
        store.Save([first, second]);
        AccountTransfers.Add(store, first.Id, second.Id, 100, DateTime.Today);
        var window = new MainWindow(directory);
        app.MainWindow = window;
        window.Show();
        ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var bytes = File.ReadAllText(store.FilePath);
        RunDialog<DeleteAccountDialog>(window, () => FindAction(window, "Удалить счёт", first.Name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            Assert(((TextBlock)dialog.FindName("AccountNameText")).Text == first.Name, "Delete confirmation identifies selected account");
            Capture(dialog, Path.Combine(root, "delete-account-dialog.png"));
            dialog.Close();
        });
        Assert(File.ReadAllText(store.FilePath) == bytes, "Cancelling deletion leaves data untouched");
        RunDialog<DeleteAccountDialog>(window, () => FindAction(window, "Удалить счёт", first.Name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
            ((Button)dialog.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert(((ItemsControl)window.FindName("AccountsList")).Items.Count == 1 && store.Load().Single().CurrentBalance == 300, "Confirmed deletion removes card and preserves other account balance");
        Assert(((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(300), "Deleted account excluded from totals");
        window.Close();
        var reopened = new MainWindow(directory);
        app.MainWindow = reopened;
        reopened.Show();
        Assert(store.Load().Count == 1, "Deletion persists after reopening");
        reopened.Close();
    }

    private static void CheckExpenseDeletion(string root, App app)
    {
        var directory = Path.Combine(root, "expense-deletion");
        var store = new AccountStore(directory);
        var categories = new CategoryStore(directory);
        var account = new Account { Name = "Наличные", OpeningBalance = 1000, OpeningDate = DateOnly.FromDateTime(DateTime.Today) };
        var category = new Category { Name = "Продукты" };
        store.Save([account]);
        categories.Save([category]);
        var expense = BudgetOperations.Add(store, categories, account.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today, "Ошибка");
        var window = new MainWindow(directory);
        app.MainWindow = window;
        window.Show();
        var home = (HomeView)window.FindName("HomePage");
        var bytes = File.ReadAllText(store.FilePath);
        RunDialog<DeleteExpenseDialog>(window, () => FindExpenseDelete(home, expense.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            Assert(((TextBlock)dialog.FindName("ExpenseText")).Text.Contains(Money.Format(100)), "Expense confirmation identifies amount");
            Capture(dialog, Path.Combine(root, "delete-expense-dialog.png"));
            dialog.Close();
        });
        Assert(File.ReadAllText(store.FilePath) == bytes, "Cancelling expense deletion leaves data untouched");
        Capture(window, Path.Combine(root, "expense-delete-button.png"));
        RunDialog<DeleteExpenseDialog>(window, () => FindExpenseDelete(home, expense.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
            ((Button)dialog.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert(store.Load().Single().CurrentBalance == 1000 && store.Load().Single().Transactions.Count == 0, "Deletion restores account balance and removes expense");
        Assert(((ItemsControl)home.FindName("EntriesList")).Items.Count == 0 && ((TextBlock)home.FindName("TotalText")).Text == Money.Format(0), "Home list and chart update immediately");
        Assert(((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(1000), "Account totals update after deleting expense");
        window.Close();
        var reopened = new MainWindow(directory);
        app.MainWindow = reopened;
        reopened.Show();
        Assert(((ItemsControl)((HomeView)reopened.FindName("HomePage")).FindName("EntriesList")).Items.Count == 0, "Deleted expense stays deleted after restart");
        reopened.Close();
    }

    private static Button FindExpenseDelete(DependencyObject node, Guid id, string action = "DeleteExpense")
    {
        if (node is Button { DataContext: BudgetEntry entry } button && entry.Id == id && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == action) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindExpenseDelete(VisualTreeHelper.GetChild(node, i), id, action); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Expense delete button not found.");
    }

    private static void CheckExpenseEdit(string root, App app)
    {
        var directory = Path.Combine(root, "expense-edit");
        var store = new AccountStore(directory);
        var categories = new CategoryStore(directory);
        var account = new Account { Name = "Наличные", OpeningBalance = 1000, OpeningDate = DateOnly.FromDateTime(DateTime.Today) };
        var category = new Category { Name = "Продукты" };
        store.Save([account]);
        categories.Save([category]);
        var expense = BudgetOperations.Add(store, categories, account.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today, "Магазин");
        var window = new MainWindow(directory);
        app.MainWindow = window;
        window.Show();
        var home = (HomeView)window.FindName("HomePage");
        var bytes = File.ReadAllText(store.FilePath);
        RunDialog<EditExpenseDialog>(window, () => FindExpenseDelete(home, expense.Id, "EditExpense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((TextBox)dialog.FindName("AmountInput")).Text = "75";
            dialog.Close();
        });
        Assert(File.ReadAllText(store.FilePath) == bytes, "Cancelling edit preserves expense");
        RunDialog<EditExpenseDialog>(window, () => FindExpenseDelete(home, expense.Id, "EditExpense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            Assert(dialog.FindName("CategoryInput") is null && dialog.FindName("AccountInput") is null && dialog.FindName("DateInput") is null, "Only amount and time can be edited");
            var amount = (TextBox)dialog.FindName("AmountInput");
            var time = (TextBox)dialog.FindName("TimeInput");
            var save = (Button)dialog.FindName("SaveButton");
            amount.Text = "2000";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Overspending edit rejected");
            amount.Text = "75";
            time.Text = "9999";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(File.ReadAllText(store.FilePath) == bytes, "Invalid time and amount leave file unchanged");
            time.Text = "0001";
            Assert(time.Text == "00:01", "Edit form automatically formats time");
            Capture(dialog, Path.Combine(root, "edit-expense-dialog.png"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var edited = store.Load().Single().Transactions.Single();
        Assert(edited == expense with { Amount = 75, OccurredAt = DateTime.Today.AddMinutes(1) }, "UI edit changes only amount and time");
        Assert(((TextBlock)home.FindName("TotalText")).Text == Money.Format(75) && ((TextBlock)window.FindName("OwnMoneyText")).Text == Money.Format(925), "Edit refreshes chart and account totals");
        window.UpdateLayout();
        var editButton = FindExpenseDelete(home, expense.Id, "EditExpense");
        var deleteButton = FindExpenseDelete(home, expense.Id);
        Assert(editButton.TranslatePoint(new Point(), window).X < deleteButton.TranslatePoint(new Point(), window).X, "Edit button is on the left of expense row");
        window.Close();
        var reopened = new MainWindow(directory);
        app.MainWindow = reopened;
        reopened.Show();
        Assert(store.Load().Single().Transactions.Single().Amount == 75, "Expense edit persists after restart");
        reopened.Close();
    }

    private static void CheckTransfers(string root, App app)
    {
        var window = new MainWindow(root);
        app.MainWindow = window;
        window.Show();
        ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var store = new AccountStore(root);
        var before = AccountTotals.From(store.Load());
        Transfer(window, "Наличные", "Дебетовая карта", "100", root, checkErrors: true);
        Transfer(window, "Карта 120 дней", "Наличные", "200", root);
        Transfer(window, "Наличные", "Карта 120 дней", "50", root);
        var accounts = store.Load();
        Assert(AccountTotals.From(accounts).Net == before.Net, "Transfers do not create money");
        Assert(accounts.Single(a => a.Name == "Наличные").CurrentBalance == 5250.25m && accounts.Single(a => a.Name == "Карта 120 дней").Debt == 3250.51m, "UI transfers update sender, recipient and credit repayment");
        Assert(accounts.SelectMany(a => a.Transactions).Count(t => t.IsTransfer) == 6, "Three transfers have six history legs");
        window.UpdateLayout();
        var card = FindDropCard(window, "Карта 120 дней");
        var expanders = FindExpanders(card).Where(e => e.Header as string == "История операций").ToList();
        Assert(expanders.Count == 0, "Account cards do not display transaction history");
        Assert(accounts.Single(a => a.Name == "Карта 120 дней").PaymentSummaries.All(s => !s.Heading.Contains("Расходы") && !s.Payments.Contains("Тестовая покупка")), "Payment summaries show only due dates and totals");
        Capture(window, Path.Combine(root, "compact-accounts.png"));
        window.Close();
        var reopened = new MainWindow(root);
        reopened.Show();
        var home = (HomeView)reopened.FindName("HomePage");
        Assert(((TextBlock)home.FindName("TotalText")).Text == Money.Format(2600.26m), "Transfers stay out of expense chart after restart");
        reopened.Close();
    }

    private static IEnumerable<Expander> FindExpanders(DependencyObject node)
    {
        if (node is Expander expander) yield return expander;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in FindExpanders(VisualTreeHelper.GetChild(node, i))) yield return child;
    }

    private static void Transfer(MainWindow window, string from, string to, string amount, string root, bool checkErrors = false)
    {
        RunDialog<TransferDialog>(window, () => ((Button)window.FindName("TransferButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            var fromInput = (ComboBox)dialog.FindName("FromInput");
            if (checkErrors) CheckTimeFormatting((TextBox)dialog.FindName("TimeInput"));
            var toInput = (ComboBox)dialog.FindName("ToInput");
            fromInput.SelectedItem = fromInput.Items.Cast<Account>().Single(a => a.Name == from);
            toInput.SelectedItem = toInput.Items.Cast<Account>().Single(a => a.Name == to);
            if (checkErrors)
            {
                ((TextBox)dialog.FindName("AmountInput")).Text = "99999999";
                ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dialog.SavedTransferId is null && !string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Invalid transfer stays unsaved");
            }
            ((TextBox)dialog.FindName("AmountInput")).Text = amount;
            if (checkErrors) Capture(dialog, Path.Combine(root, "transfer-dialog.png"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedTransferId is not null, "Transfer saved through form");
        });
    }

    private static Button FindCategoryAction(DependencyObject node, string content, string categoryName)
    {
        if (node is Button button && button.Content as string == content && button.DataContext is Category { Name: var name } && name == categoryName) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindCategoryAction(VisualTreeHelper.GetChild(node, i), content, categoryName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Category action not found.");
    }

    private static void Add(MainWindow window, string name, int type, string balance, string limit, string root, bool checkErrors = false, string rate = "0", DateOnly? opening = null)
    {
        Exception? failure = null;
        var interacted = false;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            interacted = true;
            var dialog = Application.Current.Windows.OfType<AccountDialog>().Single();
            try
            {
                var nameInput = (TextBox)dialog.FindName("NameInput");
                var balanceInput = (TextBox)dialog.FindName("BalanceInput");
                var save = FindButton(dialog);
                if (checkErrors)
                {
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(!string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Blank name rejected");
                    nameInput.Text = name;
                    balanceInput.Text = "-1";
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(dialog.CreatedAccount is null, "Negative amount rejected");
                }
                nameInput.Text = name;
                ((ComboBox)dialog.FindName("TypeInput")).SelectedIndex = type;
                balanceInput.Text = balance;
                ((TextBox)dialog.FindName("LimitInput")).Text = limit;
                ((TextBox)dialog.FindName("RateInput")).Text = rate;
                if (opening is { } start) ((DatePicker)dialog.FindName("DateInput")).SelectedDate = start.ToDateTime(TimeOnly.MinValue);
                if (type == 2)
                {
                    Assert(((StackPanel)dialog.FindName("CreditFields")).Visibility == Visibility.Visible, "Credit fields visible");
                    Capture(dialog, Path.Combine(root, "credit-dialog.png"));
                }
                if (type == 3) Capture(dialog, Path.Combine(root, "savings-dialog.png"));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dialog.CreatedAccount is not null, "Account saved through dialog");
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        }));
        ((Button)window.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw failure;
        Assert(interacted, $"Dialog did not open for {name}; shutdown: {window.Dispatcher.HasShutdownStarted}; windows: {Application.Current.Windows.Count}");
        Assert(((Button)window.FindName("AddButton")).IsEnabled, ((TextBlock)window.FindName("StatusText")).Text);
    }

    private static void Edit(MainWindow window, string oldName, string newName, int days, GracePeriodRule rule)
    {
        RunDialog<AccountDialog>(window, () =>
        {
            var button = FindAction(window, "Настроить", oldName);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, dialog =>
        {
            ((TextBox)dialog.FindName("NameInput")).Text = newName;
            ((TextBox)dialog.FindName("GraceDaysInput")).Text = days.ToString();
            ((ComboBox)dialog.FindName("GraceRuleInput")).SelectedIndex = rule == GracePeriodRule.MonthEndPlusDays ? 2 : (int)rule;
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.CreatedAccount?.Name == newName, "Card rename saved");
        });
    }

    private static void AddPurchase(MainWindow window, string accountName, decimal amount)
    {
        RunDialog<PurchaseDialog>(window, () => FindAction(window, "Добавить покупку", accountName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((TextBox)dialog.FindName("NameInput")).Text = "Тестовая покупка";
            ((TextBox)dialog.FindName("AmountInput")).Text = amount.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.DialogResult == true, "Purchase saved");
        });
    }

    private static void RunDialog<T>(MainWindow window, Action open, Action<T> interact) where T : Window
    {
        window.UpdateLayout();
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<T>().Single();
            try { interact(dialog); }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        }));
        open();
        if (failure is not null) throw failure;
    }

    private static void CheckTimeFormatting(TextBox input)
    {
        var original = input.Text;
        input.Text = "1";
        Assert(input.Text == "1", "First time digit stays editable");
        input.Text = "12";
        Assert(input.Text == "12:" && input.CaretIndex == 3, "Colon appears after hours and caret moves to minutes");
        input.Text = "123";
        Assert(input.Text == "12:3", "Partial minute input formats correctly");
        input.Text = "1234";
        Assert(input.Text == "12:34", "Four digits become hours and minutes");
        input.Text = "09:05";
        Assert(input.Text == "09:05", "Already formatted time keeps its colon and leading zero");
        input.Text = "";
        Assert(input.Text == "", "Time can be cleared");
        input.Text = original;
    }

    private static void CheckFinanceUpdates(string root, App app)
    {
        var directory = Path.Combine(root, "finance-updates");
        var store = new AccountStore(directory);
        var categories = new CategoryStore(directory);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = today.AddDays(-2);
        var cash = new Account { Name = "Наличные", OpeningBalance = 2000, OpeningDate = start };
        var savings = SavingsInterest.EnsureHistory(new Account { Name = "Накопительный", Type = AccountType.Savings, OpeningBalance = 1000, OpeningDate = start, AnnualInterestRate = 36.5m, InterestAsOf = start });
        store.Save([cash, SavingsInterest.Accrue(savings, today)]);
        var food = new Category { Name = "Еда" };
        categories.Save([food]);
        BudgetOperations.Add(store, categories, cash.Id, food.Id, CategoryKind.Expense, 100, DateTime.Today, "");
        AccountTransfers.Add(store, cash.Id, savings.Id, 500, DateTime.Today.AddMinutes(1));
        var profile = ProfileStore.Initialize(directory);
        profile.SetBudget(60000, null);
        var window = new MainWindow(directory, profile);
        app.MainWindow = window;
        window.Show();
        window.UpdateLayout();
        var home = (HomeView)window.FindName("HomePage");
        Assert(((ItemsControl)home.FindName("EntriesList")).Items.Cast<BudgetEntry>().Any(e => e.IsTransfer), "Outgoing transfer appears in operations");
        Capture(window, Path.Combine(root, "finance-operations.png"));
        ((TabControl)home.FindName("KindTabs")).SelectedIndex = 1;
        window.UpdateLayout();
        var incomeEntries = ((ItemsControl)home.FindName("EntriesList")).Items.Cast<BudgetEntry>().ToList();
        Assert(incomeEntries.Count(e => e.IsInterest) == 2 && incomeEntries.Count(e => e.IsTransfer) == 1, "Incoming history shows transfer and actual interest entries");
        Assert(((TextBlock)home.FindName("TotalText")).Text == Money.Format(2), "Accrued interest appears in income statistics");
        var interestSector = ((Canvas)home.FindName("PieCanvas")).Children.OfType<System.Windows.Shapes.Shape>().Single();
        CheckCategoryPopup(home, () => interestSector.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(interestSector)!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent }), dialog =>
        {
            Assert(((TextBlock)dialog.FindName("BreakdownHeading")).Text == "По счетам" && ((ItemsControl)dialog.FindName("CategoryEntriesList")).Items.Count == 2, "Interest details show accounts and postings without transfers");
            Assert(((TextBlock)dialog.FindName("CategoryIcon")).Text == "%", "Interest detail has a readable percent badge");
            CaptureElement(dialog, Path.Combine(root, "interest-details.png"));
        });
        RunDialog<EditExpenseDialog>(window, () => FindExpenseDelete(home, Guid.Empty, "EditExpense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            Assert(((TextBox)dialog.FindName("TimeInput")).Visibility == Visibility.Collapsed, "Interest correction edits amount only");
            ((TextBox)dialog.FindName("AmountInput")).Text = "0,50";
            Capture(dialog, Path.Combine(root, "correct-interest.png"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert(store.Load().Single(a => a.Id == savings.Id).CurrentBalance == 1501.50m, "Actual interest correction updates savings balance");
        window.UpdateLayout();
        var transfer = store.Load().Single(a => a.Id == savings.Id).Transactions.Single();
        RunDialog<EditExpenseDialog>(window, () => FindExpenseDelete(home, transfer.Id, "EditExpense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((TextBox)dialog.FindName("AmountInput")).Text = "400";
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert(store.Load().Single(a => a.Id == savings.Id).CurrentBalance == 1401.50m && store.Load().Single(a => a.Id == cash.Id).CurrentBalance == 1500, "Editing incoming transfer updates both account balances");
        window.Close();
    }

    private static void CheckBudgetCardDisplay(string root, App app)
    {
        var directory = Path.Combine(root, "budget-card-display");
        var store = new AccountStore(directory);
        var categories = new CategoryStore(directory);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var card = new Account { Name = "Кредитка", Type = AccountType.CreditCard, CreditLimit = 10000, OpeningBalance = 1000, OpeningDate = today, MonthlySpendingLimit = 60000 };
        var food = new Category { Name = "Продукты" };
        var other = new Account { Name = "Other account", OpeningBalance = 1000, OpeningDate = today };
        categories.Save([food]); store.Save([card, other]);
        BudgetOperations.Add(store, categories, other.Id, food.Id, CategoryKind.Expense, 250, DateTime.Today, "");
        BudgetOperations.Add(store, categories, card.Id, food.Id, CategoryKind.Expense, 100, DateTime.Today, "");
        var profile = ProfileStore.Initialize(directory);

        var bytes = File.ReadAllText(store.FilePath);
        var window = new MainWindow(directory, profile);
        app.MainWindow = window; window.Show();
        ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        var displayed = (Account)((ItemsControl)window.FindName("AccountsList")).Items[0];
        Assert(displayed.CardBalanceText == Money.Format(59900) && displayed.FullCardBalanceText.Contains(Money.Format(8900)), "Card displays remaining monthly budget and full bank availability");
        Assert(store.Load().Single(a => a.Id == card.Id).AvailableBalance == 8900 && File.ReadAllText(store.FilePath) == bytes, "Budget presentation does not change bank balances or financial file");
        var dropCard = FindDropCard(window, card.Name);
        Assert(FindExpanders(dropCard).Count() == 0, "No payment or history expanders remain on account card");
        Capture(window, Path.Combine(root, "budget-card-display.png"));
        RunDialog<AccountDialog>(window, () => FindAction(window, "Настроить", card.Name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            var input = (TextBox)dialog.FindName("SpendingLimitInput");
            Assert(Money.TryParse(input.Text, out var limit) && limit == 60000, "Credit card settings show saved monthly spending limit");
            input.Text = "50000";
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert(store.Load().Single(a => a.Id == card.Id).MonthlySpendingLimit == 50000 && store.Load().Single(a => a.Id == card.Id).AvailableBalance == 8900, "Editing monthly spending limit preserves actual bank availability");
        Assert(((Account)((ItemsControl)window.FindName("AccountsList")).Items[0]).CardBalanceText == Money.Format(49900), "Changed card limit updates remaining spending immediately");
        window.Close();
    }

    private static Button FindAction(DependencyObject node, string content, string accountName)
    {
        if (node is Button button && button.Content as string == content && button.DataContext is Account { Name: var name } && name == accountName)
            return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindAction(VisualTreeHelper.GetChild(node, i), content, accountName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Account action not found.");
    }

    private static DragEventArgs CreateDropEvent(Border target, Guid id, string format = "SandinFinance.Account")
    {
        // WPF creates these args in native drag/drop. Simulate the routed drop at the same UI boundary.
        var constructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var data = new DataObject(format, id);
        var values = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(IDataObject) ? (object)data
            : parameter.ParameterType == typeof(int) ? 0
            : parameter.ParameterType == typeof(DragDropKeyStates) ? DragDropKeyStates.LeftMouseButton
            : parameter.ParameterType == typeof(DragDropEffects) ? DragDropEffects.Move
            : parameter.ParameterType == typeof(DependencyObject) ? target
            : parameter.ParameterType == typeof(Point) ? new Point(10, 10)
            : throw new InvalidOperationException($"Unknown WPF drop argument: {parameter.ParameterType.FullName}")).ToArray();
        var result = (DragEventArgs)constructor.Invoke(values);
        result.RoutedEvent = DragDrop.DropEvent;
        return result;
    }

    private static Border FindDropCard(DependencyObject node, string accountName)
    {
        if (node is Border { AllowDrop: true, DataContext: Account { Name: var name } } card && name == accountName) return card;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindDropCard(VisualTreeHelper.GetChild(node, i), accountName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Drop target not found.");
    }

    private static Border FindCategoryDropCard(DependencyObject node, string categoryName)
    {
        if (node is Border { AllowDrop: true, DataContext: Category { Name: var name } } card && name == categoryName) return card;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindCategoryDropCard(VisualTreeHelper.GetChild(node, i), categoryName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Category drop target not found.");
    }

    private static Button FindVisibilityButton(DependencyObject node, string accountName)
    {
        if (node is Button button && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "ToggleAccountVisibility" && button.DataContext is Account { Name: var name } && name == accountName)
            return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindVisibilityButton(VisualTreeHelper.GetChild(node, i), accountName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Eye button not found.");
    }

    private static Button FindButton(DependencyObject node)
    {
        if (node is Button button && button.IsDefault) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindButton(VisualTreeHelper.GetChild(node, i)); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Save button not found.");
    }

    private static void Capture(Window window, string path) => CaptureElement(window, path);

    private static void SettleAnimations()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void CheckCreditReminders(string root, App app)
    {
        var directory = Path.Combine(root, "credit-reminders");
        var store = new AccountStore(directory);
        var cash = new Account { Name = "Наличные", OpeningBalance = 1000, OpeningDate = DateOnly.FromDateTime(DateTime.Today) };
        var card = new Account { Name = "Кредитка", Type = AccountType.CreditCard, CreditLimit = 10000, OpeningBalance = 500, OpeningDate = cash.OpeningDate, OpeningDebtPayments = [new(cash.OpeningDate.AddDays(3), 500)] };
        store.Save([cash, card]);
        var window = new MainWindow(directory);
        app.MainWindow = window;
        window.Show();
        SettleAnimations();
        Assert(((Border)window.FindName("ReminderBanner")).IsVisible, "Credit repayment reminder appears three days before its deadline");
        var items = (ItemsControl)window.FindName("ReminderItems");
        var reminder = items.Items.Cast<PaymentReminder>().Single();
        Assert(reminder.Kind == PaymentReminderKind.Credit && reminder.Amount == 500, "Credit banner shows only the unpaid amount");
        window.UpdateLayout();
        var item = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(0);
        FindFirstButton(item).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(((ScrollViewer)window.FindName("AccountsScroll")).IsVisible && !((HomeView)window.FindName("HomePage")).IsVisible, "Clicking a credit reminder opens accounts");
        var layer = (Grid)window.Template.FindName("WindowEffectsLayer", window);
        Assert(layer.ActualHeight == window.ActualHeight && layer.ActualWidth == window.ActualWidth && !layer.IsHitTestVisible, "Background lighting covers the full window, including the transparent caption");
        var captionPoint = window.PointToScreen(new Point(650, 14));
        var packedPoint = (long)(ushort)(short)Math.Round(captionPoint.X) | ((long)(ushort)(short)Math.Round(captionPoint.Y) << 16);
        SendCaptionMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x00A0, new IntPtr(2), new IntPtr(packedPoint));
        var light = (FrameworkElement)layer.Children[0];
        var pointer = (Point)light.GetType().GetProperty("Position")!.GetValue(light)!;
        Assert(Math.Abs(pointer.X - 650) < 1 && Math.Abs(pointer.Y - 14) < 1, "Native caption movement reaches the lighting effect with correct coordinates");
        Capture(window, Path.Combine(root, "credit-reminder.png"));
        AccountTransfers.Add(store, cash.Id, card.Id, 500, DateTime.Now);
        ((Button)window.FindName("AccountsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!((Border)window.FindName("ReminderBanner")).IsVisible, "Paid credit debt no longer produces a reminder");
        window.Close();
    }

    private static Button FindFirstButton(DependencyObject node)
    {
        if (node is Button button) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindFirstButton(VisualTreeHelper.GetChild(node, i)); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Button not found.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendCaptionMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void CheckRecurringPayments(string root, App app)
    {
        var directory = Path.Combine(root, "recurring");
        var accounts = new AccountStore(directory);
        var catalog = new CategoryStore(directory);
        var cash = new Account { Name = "Наличные", OpeningBalance = 1000, OpeningDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-5)) };
        var category = new Category { Name = "Покупки", Merchants = [new() { Name = "Магазин" }] };
        accounts.Save([cash]);
        catalog.Save([category]);
        var expense = BudgetOperations.Add(accounts, catalog, cash.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today.AddDays(-1).AddHours(12), "Магазин");
        var financialBytes = File.ReadAllBytes(accounts.FilePath);
        var store = new RecurringPaymentStore(directory);
        var window = new MainWindow(directory);
        app.MainWindow = window;
        window.Show();
        var home = (HomeView)window.FindName("HomePage");
        RunDialog<RecurringPaymentDialog>(window, () => FindExpenseDelete(home, expense.Id, "MakeRecurring").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            Assert(((TextBox)dialog.FindName("AmountInput")).Text == "100,00" && ((ComboBox)dialog.FindName("CategoryInput")).SelectedItem is Category { Name: "Покупки" }, "Existing expense pre-fills a recurring plan");
            ((TextBox)dialog.FindName("NameInput")).Text = "Подписка";
            ((DatePicker)dialog.FindName("DateInput")).SelectedDate = DateTime.Today.AddDays(3);
            ((TextBox)dialog.FindName("TimeInput")).Text = "0930";
            ((ComboBox)dialog.FindName("RepeatInput")).SelectedValue = PaymentRepeat.None;
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(dialog.SavedPayment?.FirstDueAt == DateTime.Today.AddDays(3).AddHours(9).AddMinutes(30), "Recurring deadline and formatted time are saved");
        });
        SettleAnimations();
        Assert(File.ReadAllBytes(accounts.FilePath).SequenceEqual(financialBytes), "Copying an expense into a schedule never debits twice");
        Assert(((Border)window.FindName("ReminderBanner")).IsVisible && ((ItemsControl)window.FindName("ReminderItems")).Items.Count == 1, "Three-day reminder appears at the top");
        ((Button)window.FindName("RecurringNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var page = (RecurringPaymentsView)window.FindName("RecurringPage");
        Assert(page.IsVisible && !home.IsVisible, "Recurring payments have a dedicated navigation page");
        Capture(window, Path.Combine(root, "recurring-payments.png"));
        RunDialog<RecurringPaymentDialog>(window, () => ((Button)page.FindName("CreateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog =>
        {
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!string.IsNullOrEmpty(((TextBlock)dialog.FindName("ErrorText")).Text), "Incomplete recurring plan is rejected");
            ((TextBox)dialog.FindName("NameInput")).Text = "Подписка";
            ((TextBox)dialog.FindName("AmountInput")).Text = "200";
            ((DatePicker)dialog.FindName("DateInput")).SelectedDate = DateTime.Today.AddDays(10);
            Capture(dialog, Path.Combine(root, "recurring-dialog.png"));
            ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert(store.Load().Count == 2, "Recurring names may be duplicated");
        var reminder = store.Load().Single(p => p.Amount == 100);
        window.Close();
        var reopened = new MainWindow(directory);
        app.MainWindow = reopened;
        reopened.Show();
        SettleAnimations();
        Assert(((Border)reopened.FindName("ReminderBanner")).IsVisible, "Reminder returns on each launch until marked paid");
        ((Button)reopened.FindName("RecurringNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        page = (RecurringPaymentsView)reopened.FindName("RecurringPage");
        reopened.UpdateLayout();
        FindRecurringAction(page, "PauseRecurring", reminder.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!((Border)reopened.FindName("ReminderBanner")).IsVisible, "Paused plans do not notify");
        reopened.UpdateLayout();
        FindRecurringAction(page, "PauseRecurring", reminder.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(((Border)reopened.FindName("ReminderBanner")).IsVisible, "Resuming a plan restores its reminder");
        reopened.UpdateLayout();
        FindRecurringAction(page, "CompleteRecurring", reminder.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(store.Load().Single(p => p.Id == reminder.Id).NextDueAt is null && !((Border)reopened.FindName("ReminderBanner")).IsVisible, "Marking a one-time plan paid removes its reminder");
        Assert(File.ReadAllBytes(accounts.FilePath).SequenceEqual(financialBytes), "Manual payment mark leaves financial records untouched");
        reopened.UpdateLayout();
        RunDialog<DeleteRecurringPaymentDialog>(reopened, () => FindRecurringAction(page, "DeleteRecurring", reminder.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), dialog => ((Button)dialog.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert(store.Load().Count == 1, "Recurring plan can be deleted independently of expenses");
        reopened.Close();
    }

    private static Button FindRecurringAction(DependencyObject node, string action, Guid id)
    {
        if (node is Button button && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == action && button.DataContext?.GetType().GetProperty("Payment")?.GetValue(button.DataContext) is RecurringPayment payment && payment.Id == id) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindRecurringAction(VisualTreeHelper.GetChild(node, i), action, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Recurring action not found.");
    }

    private static void CheckCategoryPopup(HomeView home, Action open, Action<CategoryDetailsPopup> inspect)
    {
        var windows = Application.Current.Windows.Count;
        open();
        SettleAnimations();
        var popup = (System.Windows.Controls.Primitives.Popup)typeof(HomeView).GetField("detailsPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(home)!;
        Assert(popup.IsOpen && !popup.StaysOpen && popup.AllowsTransparency && Application.Current.Windows.Count == windows, "Category details are a light dismiss popup, not another window");
        var card = (CategoryDetailsPopup)popup.Child;
        inspect(card);
        var owner = Window.GetWindow(home);
        owner.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(owner)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Assert(!popup.IsOpen, "Escape closes category popup");
        open();
        Assert(popup.IsOpen, "Category popup can reopen");
        home.Refresh();
        Assert(!popup.IsOpen, "Refreshing the period closes old details");
    }

    private static void CaptureElement(FrameworkElement window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
