using System.ComponentModel.DataAnnotations;
using System.Windows.Input;

using Microsoft.Extensions.DependencyInjection;

using StreamFlow.App.ViewModels.Pages;

namespace StreamFlow.App.Commands;

/// <summary>
/// An abstract base class providing a standardized, reusable structure for defining 
/// common audio commands within the application using RoutedCommand principles.
/// </summary>
/// <remarks>
/// Initializes a new instance of the AudioCommandBase.
/// </remarks>
/// <param name="commandName"><b>Required</b> - A unique identifier for this command, used with XAML bindinds.</param>
public abstract class AudioCommandBase(string commandName) : DependencyObject, ICommand
{
    public AudioViewModel AudioVM = App.Services.GetRequiredService<AudioViewModel>();
    // --- Public Properties (ICommand Implementation) ---

    /// <summary>
    /// Gets the CommandName used for binding in XAML.
    /// </summary>
    [Required]
    public string CommandName { get; protected set; } = commandName ?? throw new ArgumentNullException(nameof(commandName));

    /// <summary>
    /// Determines if the command can be executed at the current time. 
    /// This method must be overridden to provide specific logic.
    /// </summary>
    public abstract bool CanExecute(object? parameter);

    /// <summary>
    /// Executes the core audio logic associated with this command.
    /// This method must be overridden to perform the desired action (e.g., playing a sound, adjusting parameters).
    /// </summary>
    /// <param name="parameter">The optional parameter passed when executing the command.</param>
    public abstract void Execute(object? parameter);

    // --- ICommand Implementation ---

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    /// <summary>
    /// Helper method to simulate setting up binding logic if needed outside of XAML bindings.
    /// </summary>
    public void BindToElement(FrameworkElement element)
    {
        // In a real application, you might attach this command to the specific control 
        // that needs to trigger it, or use DependencyProperty setters.
        // For demonstration, we just ensure the structure is ready.
        Console.WriteLine($"Command '{CommandName}' prepared for binding on element: {element.GetType().Name}");
    }

    // --- Overriding Equals/GetHashCode for safety ---
    protected new virtual bool Equals(object obj)
    {
        return obj is AudioCommandBase other && GetType() == other.GetType();
    }

    public new virtual int GetHashCode() => base.GetHashCode();
}
