// Feed/RelayCommand.cs
using System.Windows.Input;

namespace YouTubeDesktopClient.Feed;

/// <summary>Minimal ICommand for wiring DataTemplate buttons to view-model methods.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
