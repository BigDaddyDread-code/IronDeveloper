using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IUserService _userService;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _emailError = string.Empty;
    [ObservableProperty] private string _passwordError = string.Empty;
    [ObservableProperty] private bool   _hasAttemptedSignIn;
    [ObservableProperty] private bool   _isBusy;

    [ObservableProperty] private ObservableCollection<global::IronDev.Core.Auth.TenantDto> _availableTenants = new();
    [ObservableProperty] private global::IronDev.Core.Auth.TenantDto? _selectedTenant;
    
    [ObservableProperty] private ObservableCollection<string> _availableAuthMethods = new() { "Direct Email/Password" };
    [ObservableProperty] private string _selectedAuthMethod = "Direct Email/Password";

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsTenantSelectorVisible), nameof(IsCredentialsVisible))]
    private LoginStage _currentStage = LoginStage.WorkspaceSelection;

    // Helper for XAML visibility bindings (backward compat or simplicity)
    public bool IsTenantSelectorVisible => CurrentStage == LoginStage.WorkspaceSelection;
    public bool IsCredentialsVisible => CurrentStage == LoginStage.Credentials;

    private global::IronDev.Services.User? _authenticatedUser;

    /// <summary>Shell sets this callback; fired when sign-in succeeds with a user and tenant.</summary>
    public Action<global::IronDev.Core.Auth.UserProfileDto, global::IronDev.Core.Auth.TenantDto>? OnSignIn { get; set; }

    public LoginViewModel(global::IronDev.Services.IUserService userService)
    {
        _userService = userService;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var tenants = await _userService.GetAllActiveTenantsAsync();
            AvailableTenants.Clear();
            foreach (var t in tenants) AvailableTenants.Add(t);
            
            SelectedTenant = AvailableTenants.FirstOrDefault(t => t.Name == "IronDev Project") 
                          ?? AvailableTenants.FirstOrDefault();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ContinueFromWorkspace()
    {
        if (SelectedTenant == null) return;
        
        EmailError = string.Empty;
        PasswordError = string.Empty;
        HasAttemptedSignIn = false;
        CurrentStage = LoginStage.Credentials;
    }

    [RelayCommand]
    private async Task SignInAsync(object? parameter)
    {
        // Stage 2: Actual Authentication (Expected to be in Credentials stage)
        EmailError = string.Empty;
        PasswordError = string.Empty;
        HasAttemptedSignIn = true;

        string? password = null;
        if (parameter is string s) password = s;
        else if (parameter is System.Windows.Controls.PasswordBox pb) password = pb.Password;

        if (string.IsNullOrWhiteSpace(Email)) { EmailError = "Email is required."; return; }
        if (string.IsNullOrWhiteSpace(password)) { PasswordError = "Password is required."; return; }

        IsBusy = true;
        try 
        {
            // a) Validate credentials
            _authenticatedUser = await _userService.ValidateCredentialsAsync(Email, password!);
            
            if (_authenticatedUser == null)
            {
                EmailError = "Invalid email or password.";
                return;
            }

            // b) Check membership for selected tenant
            if (SelectedTenant == null) return;
            var isMember = await _userService.IsMemberOfTenantAsync(_authenticatedUser.Id, SelectedTenant.Id);
            
            if (!isMember)
            {
                EmailError = "User is not assigned to this workspace.";
                return;
            }

            // Success
            var profile = new global::IronDev.Core.Auth.UserProfileDto(
                _authenticatedUser.Id, 
                _authenticatedUser.Email, 
                _authenticatedUser.DisplayName, 
                SelectedTenant.Id);

            OnSignIn?.Invoke(profile, SelectedTenant);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void CancelTenantSelection()
    {
        CurrentStage = LoginStage.WorkspaceSelection;
        _authenticatedUser = null;
        HasAttemptedSignIn = false;
        EmailError = string.Empty;
        PasswordError = string.Empty;
    }
}
