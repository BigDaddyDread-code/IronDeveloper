using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::IronDev.Agent.Models;
using IronDev.Client.Auth;
using IronDev.Core.Auth;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthApiClient _authApiClient;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _emailError = string.Empty;
    [ObservableProperty] private string _passwordError = string.Empty;
    [ObservableProperty] private bool   _hasAttemptedSignIn;
    [ObservableProperty] private bool   _isBusy;

    [ObservableProperty] private ObservableCollection<TenantDto> _availableTenants = new();
    [ObservableProperty] private TenantDto? _selectedTenant;
    
    [ObservableProperty] private ObservableCollection<string> _availableAuthMethods = new() { "Direct Email/Password" };
    [ObservableProperty] private string _selectedAuthMethod = "Direct Email/Password";

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsCredentialsVisible), nameof(IsResolvingVisible), nameof(IsTenantSelectorVisible))]
    private LoginStage _currentStage = LoginStage.Credentials;

    public bool IsCredentialsVisible => CurrentStage == LoginStage.Credentials;
    public bool IsResolvingVisible => CurrentStage == LoginStage.Resolving;
    public bool IsTenantSelectorVisible => CurrentStage == LoginStage.TenantSelection;

    private UserProfileDto? _authenticatedUser;

    /// <summary>Shell sets this callback; fired when sign-in succeeds with a user and tenant.</summary>
    public Action<UserProfileDto, TenantDto>? OnSignIn { get; set; }

    public LoginViewModel(IAuthApiClient authApiClient)
    {
        _authApiClient = authApiClient;
        Email = LoadLastEmail();
    }

    public LoginViewModel(object authService)
        : this(global::IronDev.Agent.Services.BoundaryCompatibility.Auth(authService))
    {
    }

    private static string LoadLastEmail()
    {
        try
        {
            using var store = IsolatedStorageFile.GetUserStoreForAssembly();
            if (!store.FileExists("irondev_last_user.txt")) return string.Empty;
            using var reader = new StreamReader(new IsolatedStorageFileStream("irondev_last_user.txt", FileMode.Open, store));
            return reader.ReadLine() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static void SaveLastEmail(string email)
    {
        try
        {
            using var store = IsolatedStorageFile.GetUserStoreForAssembly();
            using var writer = new StreamWriter(new IsolatedStorageFileStream("irondev_last_user.txt", FileMode.Create, store));
            writer.WriteLine(email);
        }
        catch { /* non-critical */ }
    }

    [RelayCommand]
    private async Task SignInAsync(object? parameter)
    {
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
            // 1. Transition to "Resolving" state early
            CurrentStage = LoginStage.Resolving;

            var login = await _authApiClient.LoginAsync(new LoginRequest(Email, password!));
            _authenticatedUser = await _authApiClient.GetCurrentUserAsync();
            
            await Task.Delay(400); // UI feedback for "Loading your workspace..."

            // 3. Resolve Tenants for this user
            var tenants = await _authApiClient.GetTenantsAsync();
            AvailableTenants.Clear();
            foreach (var t in tenants) AvailableTenants.Add(t);

            if (AvailableTenants.Count == 0)
            {
                EmailError = "This account is not associated with any IronDev workspaces.";
                CurrentStage = LoginStage.Credentials;
                return;
            }

            if (AvailableTenants.Count == 1)
            {
                // Auto-skip selection
                CompleteSignIn(AvailableTenants[0]);
            }
            else
            {
                // Must choose
                SelectedTenant = AvailableTenants.FirstOrDefault(t => t.Name.Contains("IronDev")) ?? AvailableTenants[0];
                CurrentStage = LoginStage.TenantSelection;
            }
        }
        catch (Exception ex)
        {
            EmailError = ex.Message.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase)
                ? "Invalid email or password."
                : $"Connection error: {ex.Message}";
            CurrentStage = LoginStage.Credentials;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void SelectTenant()
    {
        if (SelectedTenant == null) return;
        CompleteSignIn(SelectedTenant);
    }

    private async void CompleteSignIn(TenantDto tenant)
    {
        if (_authenticatedUser == null) return;

        var response = await _authApiClient.SelectTenantAsync(new SelectTenantRequest(tenant.Id));

        SaveLastEmail(_authenticatedUser.Email);
        var profile = new UserProfileDto(
            response.UserId,
            _authenticatedUser.Email,
            response.DisplayName,
            tenant.Id);

        OnSignIn?.Invoke(profile, tenant);
    }

    [RelayCommand]
    private void BackToLogin()
    {
        CurrentStage = LoginStage.Credentials;
        _authenticatedUser = null;
        HasAttemptedSignIn = false;
        EmailError = string.Empty;
        PasswordError = string.Empty;
    }
}
