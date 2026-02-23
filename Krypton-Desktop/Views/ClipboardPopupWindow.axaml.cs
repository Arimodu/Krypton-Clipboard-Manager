using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Krypton_Desktop.ViewModels;

namespace Krypton_Desktop.Views;

public partial class ClipboardPopupWindow : Window
{
    public ClipboardPopupWindow()
    {
        InitializeComponent();
        Deactivated += OnWindowDeactivated;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && DataContext is ClipboardPopupViewModel vm)
        {
            if (vm.SelectedItem != null)
            {
                vm.PasteItemCommand.Execute(vm.SelectedItem).Subscribe();
                e.Handled = true;
            }
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Close when window loses activation (user clicks elsewhere)
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Deactivated -= OnWindowDeactivated;
        if (DataContext is ClipboardPopupViewModel vm)
        {
            vm.Cleanup();
        }
    }
}
