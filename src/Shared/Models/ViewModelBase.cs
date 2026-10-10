using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shared.Models;

public class ViewModelBase : ObservableObject
{
    public virtual void OnNavigatedTo(object? parameter = null) { }
}