using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fieldlog.App.Infrastructure;

/// <summary>
/// Base class implementing <see cref="INotifyPropertyChanged"/> with a SetField helper.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property.</summary>
    protected void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns <paramref name="value"/> and notifies if it differs from the current value.</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Notify(propertyName);
        return true;
    }
}
