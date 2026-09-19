using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Animora.Desktop.Controls;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be repopulated in a single
/// batch, raising exactly one <see cref="NotifyCollectionChangedAction.Reset"/>
/// notification (plus <c>Count</c>/<c>Item[]</c> property changes) instead of
/// one <c>Add</c> notification per item.
/// <para>
/// The screens in this app frequently rebuild a bindable list by clearing it and
/// re-adding every element (filters, sorts, segment switches). Doing that on a
/// plain collection makes Avalonia's <c>ItemsControl</c> process one
/// <see cref="INotifyCollectionChanged.CollectionChanged"/> event per item — an
/// O(n) notification storm and a needless layout/realisation pass per element.
/// <see cref="ReplaceAll"/> and <see cref="AddRange"/> collapse that into a
/// single reset, so the UI rebinds the list once regardless of its size
/// (PERFORMANCE.md §3/§4). A Reset is fully supported by Avalonia item
/// binding and by CommunityToolkit.Mvvm, so behaviour is unchanged.
/// </para>
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Creates an empty collection.</summary>
    public RangeObservableCollection()
    {
    }

    /// <summary>Creates a collection pre-populated from <paramref name="items"/>.</summary>
    public RangeObservableCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    /// <summary>
    /// Clears the collection and appends <paramref name="items"/> in one batch,
    /// emitting a single <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// This is the batched equivalent of <c>Clear()</c> followed by a loop of
    /// <c>Add</c> calls, showing exactly the same content.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        if (items is not null)
        {
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }

        RaiseReset();
    }

    /// <summary>
    /// Appends <paramref name="items"/> to the collection in one batch, emitting
    /// a single <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            Items.Add(item);
        }

        RaiseReset();
    }

    /// <summary>
    /// Sends the single notification pair a batched change needs: one Reset for
    /// item bindings and the two scalar property changes (<c>Count</c>,
    /// <c>Item[]</c>) that <c>ItemsControl</c> also observes.
    /// </summary>
    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
