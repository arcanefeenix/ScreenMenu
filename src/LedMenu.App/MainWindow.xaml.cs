using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LedMenu.App.ViewModels;

namespace LedMenu.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Enter commits a typed number or name immediately instead of waiting for focus to leave.</summary>
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>
    /// A number box may show an invalid entry while it is being edited, but once focus leaves it must
    /// show the value that is actually stored; it never keeps displaying something the configuration does not hold.
    /// </summary>
    private void NumericBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        // Run after the binding has tried to commit the text.
        Dispatcher.BeginInvoke(() =>
        {
            var binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding != null && (binding.HasError || Validation.GetHasError(box)))
            {
                Validation.ClearInvalid(binding);
                binding.UpdateTarget();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Leaving an editor text box saves anything still waiting so nothing typed is held back.</summary>
    private void EditorText_Lost(object sender, KeyboardFocusChangedEventArgs e) => (DataContext as MainViewModel)?.Editor.FlushPending();

    private void EditorText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as MainViewModel)?.Editor.FlushPending();
        e.Handled = true;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is TabControl) (DataContext as MainViewModel)?.Editor.FlushPending();
    }

    private void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenItemViewModel item }) item.Select();
    }
}


/// <summary>Collapses an element when its bound value is null.</summary>
public sealed class NullToCollapsedConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
