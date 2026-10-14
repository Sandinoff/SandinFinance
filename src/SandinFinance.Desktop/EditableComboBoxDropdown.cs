using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SandinFinance.Desktop;

public static class EditableComboBoxDropdown
{
    public static void Attach(ComboBox combo)
    {
        if (combo.IsLoaded) AttachEditor(combo, new RoutedEventArgs());
        else combo.Loaded += AttachEditor;

        void AttachEditor(object sender, RoutedEventArgs args)
        {
            combo.Loaded -= AttachEditor;
            combo.ApplyTemplate();
            if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox editor) return;
            editor.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) =>
            {
                if (!combo.HasItems || combo.IsDropDownOpen) return;
                // Finish caret placement and release editor capture before opening the list.
                combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (combo.IsLoaded && combo.IsEnabled && combo.HasItems) combo.IsDropDownOpen = true;
                }));
            }), handledEventsToo: true);
        }
    }
}
