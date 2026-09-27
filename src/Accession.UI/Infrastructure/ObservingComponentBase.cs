using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.AspNetCore.Components;

namespace Accession.UI.Infrastructure;

/// <summary>
/// Re-renders when observed view models, collections or commands change. Several changes in a row
/// produce one render, so view models can keep raising fine-grained notifications.
/// </summary>
public abstract class ObservingComponentBase : ComponentBase, IDisposable
{
    private readonly List<object> _observed = [];
    private bool _renderQueued;

    /// <summary>Replaces the observed objects. Call from OnParametersSet.</summary>
    protected void Observe(params object?[] sources)
    {
        Unobserve();
        foreach (var source in sources.OfType<object>())
        {
            if (source is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnSourceChanged;
            }

            if (source is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += OnSourceChanged;
            }

            if (source is ICommand command)
            {
                command.CanExecuteChanged += OnSourceChanged;
            }

            _observed.Add(source);
        }
    }

    public void Dispose()
    {
        Unobserve();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs a command if it can execute; used by event handlers in markup. Yields first so the command runs
    /// after the web view's event returns: commands may open modal Windows dialogs (nested message loops).
    /// </summary>
    protected static async Task Run(ICommand command, object? parameter = null)
    {
        await Task.Yield();
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    protected static bool Disabled(ICommand command, object? parameter = null) => !command.CanExecute(parameter);

    private void Unobserve()
    {
        foreach (var source in _observed)
        {
            if (source is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= OnSourceChanged;
            }

            if (source is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged -= OnSourceChanged;
            }

            if (source is ICommand command)
            {
                command.CanExecuteChanged -= OnSourceChanged;
            }
        }

        _observed.Clear();
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        _ = InvokeAsync(() =>
        {
            _renderQueued = false;
            StateHasChanged();
        });
    }
}
