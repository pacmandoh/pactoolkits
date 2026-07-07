using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>ShadUI Demo Login pattern — singleton VM, Initialize before Show, Close from Submit/Cancel.</summary>
public sealed partial class SensitiveUnlock(DialogManager dialogManager)
    : FormBase(dialogManager), INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
    private string _title = string.Empty;
    private string _hintMessage = string.Empty;
    private string _password = string.Empty;
    private Func<string, string?>? _verify;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string HintMessage
    {
        get => _hintMessage;
        private set => SetProperty(ref _hintMessage, value);
    }

    public string Username { get; } = "pacmandoh";

    public string Password
    {
        get => _password;
        set
        {
            if (!SetProperty(ref _password, value))
            {
                return;
            }

            if (_errors.ContainsKey(nameof(Password)))
            {
                SetPasswordError(null);
            }
        }
    }

    public string? PasswordError
        => _errors.TryGetValue(nameof(Password), out var errors) ? errors[0] : null;

    bool INotifyDataErrorInfo.HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return _errors.Values.SelectMany(static x => x);
        }

        return _errors.TryGetValue(propertyName, out var errors)
            ? errors
            : Array.Empty<string>();
    }

    public void Initialize(string title, string hintMessage, Func<string, string?>? verify = null)
    {
        Title = title;
        HintMessage = hintMessage;
        _verify = verify;
        SetPasswordError(null);
        Password = string.Empty;
    }

    [RelayCommand]
    private void Submit()
    {
        var password = Password.Trim();
        if (string.IsNullOrEmpty(password))
        {
            SetPasswordError("请输入数据库密码");
            return;
        }

        var error = _verify?.Invoke(password);
        if (!string.IsNullOrEmpty(error))
        {
            SetPasswordError(error);
            return;
        }

        CloseDialog(success: true);
    }

    [RelayCommand]
    private void Cancel()
        => CloseDialog();

    private void SetPasswordError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            if (_errors.Remove(nameof(Password)))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Password)));
                OnPropertyChanged(nameof(PasswordError));
            }

            return;
        }

        _errors[nameof(Password)] = [message];
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Password)));
        OnPropertyChanged(nameof(PasswordError));
    }
}
