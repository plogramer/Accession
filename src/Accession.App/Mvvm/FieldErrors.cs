using System.ComponentModel;

namespace Accession.App.Mvvm;

/// <summary>
/// Field → error message map for forms. Bind with <c>{Binding Errors[FieldName]}</c>; missing fields return "".
/// </summary>
public sealed class FieldErrors : INotifyPropertyChanged
{
    private Dictionary<string, string> _errors = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string field] => _errors.TryGetValue(field, out var message) ? message : string.Empty;

    public bool HasErrors => _errors.Count > 0;

    public IEnumerable<string> Messages => _errors.Values;

    public void Set(IReadOnlyDictionary<string, string> errors)
    {
        _errors = new Dictionary<string, string>(errors);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
    }

    public void Clear() => Set(new Dictionary<string, string>());
}
