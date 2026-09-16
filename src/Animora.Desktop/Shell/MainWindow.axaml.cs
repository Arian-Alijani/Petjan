using Animora.Desktop.Pages._ControlGallery;
using Avalonia.Controls;

namespace Animora.Desktop.Shell;

public partial class MainWindow : Window
{
    // Phase 03: temporarily hosts the control gallery for visual smoke
    // testing. Phase 05 replaces this with the real app shell.
    public MainWindow(ControlGalleryView gallery)
    {
        InitializeComponent();
        Content = gallery;
    }
}
