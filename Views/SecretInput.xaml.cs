using System.Windows;
using System.Windows.Controls;

namespace SoleKey.Views;

public partial class SecretInput : UserControl
{
    private bool updating;
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(SecretInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));
    public static readonly DependencyProperty ReadOnlyProperty = DependencyProperty.Register(nameof(ReadOnly), typeof(bool), typeof(SecretInput), new PropertyMetadata(false));
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool ReadOnly { get => (bool)GetValue(ReadOnlyProperty); set => SetValue(ReadOnlyProperty, value); }
    public SecretInput()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Reveal.IsChecked = false;
        Unloaded += (_, _) => Reveal.IsChecked = false;
    }
    public void FocusSecret() { if (ReadOnly) return; Masked.Focus(); }
    private static void ValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SecretInput)d;
        if (control.Masked is null || control.updating) return;
        control.updating = true;
        try { control.Masked.Password = (string?)e.NewValue ?? ""; }
        finally { control.updating = false; }
    }
    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        updating = true;
        try { SetCurrentValue(ValueProperty, Masked.Password); GetBindingExpression(ValueProperty)?.UpdateSource(); }
        finally { updating = false; }
    }
}
