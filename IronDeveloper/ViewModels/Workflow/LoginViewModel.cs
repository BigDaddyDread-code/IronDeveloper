using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IUserService _userService;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _usernameError = string.Empty;
    [ObservableProperty] private string _passwordError = string.Empty;
    [ObservableProperty] private bool   _hasAttemptedSignIn;
    [ObservableProperty] private bool   _isBusy;

    [ObservableProperty] private ObservableCollection<global::IronDev.Core.Auth.TenantDto> _availableTenants = new();
    [ObservableProperty] private global::IronDev.Core.Auth.TenantDto? _selectedTenant;
    [ObservableProperty] private bool _isTenantSelectorVisible;

    private global::IronDev.Services.User? _authenticatedUser;

    /// <summary>Shell sets this callback; fired when sign-in succeeds with a user and tenant.</summary>
    internal Action<global::IronDev.Core.Auth.UserProfileDto, global::IronDev.Core.Auth.TenantDto>? OnSignIn { get; set; }

    public LoginViewModel(global::IronDev.Services.IUserService userService)
    {
        _userService = userService;
    }

    [RelayCommand]
    private async Task SignInAsync(string? password)
    {
        HasAttemptedSignIn = true;
        UsernameError      = string.Empty;
        PasswordError      = string.Empty;

        // Stage 1: Initial Authentication
        if (!IsTenantSelectorVisible)
        {
            if (string.IsNullOrWhiteSpace(Username)) { UsernameError = "Username is required."; return; }
            if (string.IsNullOrWhiteSpace(password)) { PasswordError = "Password is required."; return; }

            IsBusy = true;
            try 
            {
                _authenticatedUser = await _userService.ValidateCredentialsAsync(Username, password!);
                
                if (_authenticatedUser == null)
                {
                    UsernameError = "Invalid email or password.";
                    return;
                }

                // Load tenants
                var tenants = await _userService.GetUserTenantsAsync(_authenticatedUser.Id);
                if (tenants == null || tenants.Count == 0)
                {
                    UsernameError = "User is not mapped to any tenant.";
                    return;
                }

                AvailableTenants.Clear();
                foreach (var t in tenants) AvailableTenants.Add(t);
                SelectedTenant = AvailableTenants.FirstOrDefault();
                
                IsTenantSelectorVisible = true;
                HasAttemptedSignIn = false; // Reset for stage 2
            }
            finally { IsBusy = false; }
        }
        else
        {
            // Stage 2: Tenant Selection
            if (SelectedTenant == null) return;
            if (_authenticatedUser == null) return;

            var profile = new global::IronDev.Core.Auth.UserProfileDto(
                _authenticatedUser.Id, 
                _authenticatedUser.Email, 
                _authenticatedUser.DisplayName, 
                SelectedTenant.Id);

            OnSignIn?.Invoke(profile, SelectedTenant);
        }
    }

    [RelayCommand]
    private void CancelTenantSelection()
    {
        IsTenantSelectorVisible = false;
        _authenticatedUser = null;
        AvailableTenants.Clear();
    }
}
