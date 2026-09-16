using System.Collections.Generic;

namespace Animora.Desktop.Shell;

/// <summary>One sidebar section: a group title plus its nav entries.</summary>
public sealed class NavGroupViewModel
{
    public NavGroupViewModel(string title, IReadOnlyList<NavEntryViewModel> entries)
    {
        Title = title;
        Entries = entries;
    }

    public string Title { get; }

    public IReadOnlyList<NavEntryViewModel> Entries { get; }
}
