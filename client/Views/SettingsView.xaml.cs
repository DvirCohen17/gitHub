using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnChanged;
            _vm = e.NewValue as SettingsViewModel;
            if (_vm == null) return;
            _vm.PropertyChanged += OnChanged;
            UpdatePreview();
        };
    }

    // AvalonEdit's Text and highlighting aren't bindable: copy them over.
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.PreviewCode) or nameof(SettingsViewModel.PreviewHighlighting)) UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_vm == null) return;
        if (preview.Text != _vm.PreviewCode) preview.Text = _vm.PreviewCode;
        preview.SyntaxHighlighting = _vm.PreviewHighlighting;
    }

    private async void SaveCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        byte[] png = cropper.RenderPng(256);
        await _vm.SaveCroppedAsync(png);
    }
}
