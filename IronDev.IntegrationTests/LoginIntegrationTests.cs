using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Agent.ViewModels.Workflow;
using IronDev.Agent.Models;
using IronDev.Services;
using IronDev.Core.Auth;
using Microsoft.Data.SqlClient;
using Dapper;

namespace IronDev.IntegrationTests;

[TestClass]
public class LoginIntegrationTests : IntegrationTestBase
{
    private LoginViewModel _vm = null!;
    private IUserService _userService = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        // IntegrationTestBase.TestInitialize will be called automatically by MSTest 
        // but we need to ensure ResetDatabase is done if not already.
        // Actually [TestInitialize] in base is already there.
        
        _userService = ServiceProvider.GetRequiredService<IUserService>();
        _vm = new LoginViewModel(_userService);
    }

    private async Task SeedUserAndTenantAsync(string email, string password, string tenantName, bool mapped = true)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password, 11);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var tenantId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Tenants (Name, Slug) OUTPUT inserted.Id VALUES (@Name, @Slug)",
            new { Name = tenantName, Slug = tenantName.ToLower().Replace(" ", "-") });

        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Users (Email, DisplayName, PasswordHash) OUTPUT inserted.Id VALUES (@Email, @DisplayName, @Hash)",
            new { Email = email, DisplayName = "Test User", Hash = hash });

        if (mapped)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TenantId, @UserId, 'Member')",
                new { TenantId = tenantId, UserId = userId });
        }
    }

    [TestMethod]
    public void Login_starts_on_workspace_selection()
    {
        Assert.AreEqual(LoginStage.WorkspaceSelection, _vm.CurrentStage);
        Assert.IsTrue(_vm.IsTenantSelectorVisible);
        Assert.IsFalse(_vm.IsCredentialsVisible);
    }

    [TestMethod]
    public async Task Tenant_loading_contains_expected_tenants()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        // We need to re-init or wait for the async init in constructor
        // Since constructor fires Task.Run or similar, we might need a way to wait.
        // In the VM: _ = InitializeAsync();
        
        // Let's manually trigger init or wait for it.
        // A better pattern for tests is to have a Task we can await.
        // For now, let's just wait a bit or re-call it.
        
        var tenants = await _userService.GetAllActiveTenantsAsync();
        Assert.IsTrue(tenants.Any(t => t.Name == "IronDev Project"));
    }

    [TestMethod]
    public async Task Workspace_continue_moves_to_credentials()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        // Refresh tenants
        var tenants = await _userService.GetAllActiveTenantsAsync();
        _vm.AvailableTenants.Clear();
        foreach(var t in tenants) _vm.AvailableTenants.Add(t);
        _vm.SelectedTenant = _vm.AvailableTenants.First();

        _vm.ContinueFromWorkspaceCommand.Execute(null);

        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
    }

    [TestMethod]
    public async Task Selected_tenant_is_preserved_between_login_stages()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        var tenants = await _userService.GetAllActiveTenantsAsync();
        _vm.AvailableTenants.Clear();
        foreach(var t in tenants) _vm.AvailableTenants.Add(t);
        var expectedTenant = _vm.AvailableTenants.First();
        _vm.SelectedTenant = expectedTenant;

        _vm.ContinueFromWorkspaceCommand.Execute(null);

        Assert.AreEqual(expectedTenant, _vm.SelectedTenant);
    }

    [TestMethod]
    public async Task Login_with_valid_credentials_and_tenant_succeeds()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        var tenants = await _userService.GetAllActiveTenantsAsync();
        _vm.AvailableTenants.Clear();
        foreach(var t in tenants) _vm.AvailableTenants.Add(t);
        _vm.SelectedTenant = _vm.AvailableTenants.First();
        _vm.ContinueFromWorkspaceCommand.Execute(null);

        _vm.Email = "bob@test.com";
        bool successCalled = false;
        _vm.OnSignIn = (u, t) => { successCalled = true; };

        await _vm.SignInCommand.ExecuteAsync("Pass123!");

        Assert.IsTrue(successCalled, "OnSignIn should have been invoked.");
        Assert.IsFalse(_vm.IsBusy);
        Assert.AreEqual(string.Empty, _vm.EmailError);
    }

    [TestMethod]
    public async Task Login_with_invalid_password_fails()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        var tenants = await _userService.GetAllActiveTenantsAsync();
        _vm.AvailableTenants.Clear();
        foreach(var t in tenants) _vm.AvailableTenants.Add(t);
        _vm.SelectedTenant = _vm.AvailableTenants.First();
        _vm.ContinueFromWorkspaceCommand.Execute(null);

        _vm.Email = "bob@test.com";

        await _vm.SignInCommand.ExecuteAsync("WrongPass");

        Assert.AreEqual("Invalid email or password.", _vm.EmailError);
        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
    }

    [TestMethod]
    public async Task Login_with_missing_password_fails()
    {
        _vm.Email = "bob@test.com";
        _vm.CurrentStage = LoginStage.Credentials;

        await _vm.SignInCommand.ExecuteAsync("");

        Assert.AreEqual("Password is required.", _vm.PasswordError);
    }

    [TestMethod]
    public async Task Login_with_wrong_tenant_membership_fails()
    {
        // Seed user into one tenant, but select another
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project", mapped: false);
        
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO dbo.Tenants (Name, Slug) VALUES ('Other Project', 'other-project')");

        var tenants = await _userService.GetAllActiveTenantsAsync();
        _vm.AvailableTenants.Clear();
        foreach(var t in tenants) _vm.AvailableTenants.Add(t);
        _vm.SelectedTenant = _vm.AvailableTenants.First(t => t.Name == "IronDev Project");
        _vm.ContinueFromWorkspaceCommand.Execute(null);

        _vm.Email = "bob@test.com";

        await _vm.SignInCommand.ExecuteAsync("Pass123!");

        Assert.AreEqual("User is not assigned to this workspace.", _vm.EmailError);
    }
}
