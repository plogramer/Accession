using Accession.Presentation.Mvvm;
using Microsoft.Extensions.DependencyInjection;

namespace Accession.Presentation.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public ViewModelBase? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    public TViewModel NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        var next = _services.GetRequiredService<TViewModel>();
        if (ReferenceEquals(next, Current))
        {
            return next;
        }

        Current?.OnNavigatedFrom();
        Current = next;
        next.OnNavigatedTo();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return next;
    }
}
