using System.Windows.Controls;
using System.Windows.Input;

namespace SandinFinance.Desktop;

internal static class TimeInputFormatter
{
    public static void Attach(TextBox input)
    {
        var formatting = false;
        input.TextChanged += (_, _) =>
        {
            if (formatting) return;
            var text = input.Text;
            if (text.Length is < 2 or > 4 || !text.All(c => c is >= '0' and <= '9')) return;
            var caret = input.CaretIndex;
            var selection = input.SelectionLength;
            formatting = true;
            try
            {
                input.Text = text.Insert(2, ":");
                input.Select(text.Length == 2 ? 3 : caret >= 2 ? caret + 1 : caret, selection);
            }
            finally { formatting = false; }
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (input.SelectionLength != 0) return;
            if (e.Key == Key.Back && input.CaretIndex == 3 && input.Text.Length == 3 && input.Text[2] == ':')
            {
                input.Text = input.Text[..1];
                input.CaretIndex = 1;
                e.Handled = true;
            }
            else if (e.Key == Key.Back && input.CaretIndex > 0 && input.Text[input.CaretIndex - 1] == ':')
            {
                input.CaretIndex--;
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && input.CaretIndex < input.Text.Length && input.Text[input.CaretIndex] == ':')
            {
                input.CaretIndex++;
                e.Handled = true;
            }
        };
    }
}
