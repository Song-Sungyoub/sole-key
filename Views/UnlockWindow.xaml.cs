using System.Windows;
using SoleKey.ViewModels;

namespace SoleKey.Views;
public partial class UnlockWindow : Window
{
    public UnlockWindow(UnlockViewModel vm)
    {
        InitializeComponent(); DataContext = vm; WindowDialogs.Connect(this, vm);
        vm.UnlockAnimationRequested += UnlockMotion.PlayOpeningAsync;
        Closed += (_, _) => { vm.Deactivate(); vm.UnlockAnimationRequested -= UnlockMotion.PlayOpeningAsync; };
    }
}
