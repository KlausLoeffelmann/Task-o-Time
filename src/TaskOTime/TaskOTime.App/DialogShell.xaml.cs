using System;
using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

public partial class DialogShell : Window
{
    private readonly DialogShellViewModel _viewModel;

    public DialogShell(DialogShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Closed -= OnClosed;
    }
}
