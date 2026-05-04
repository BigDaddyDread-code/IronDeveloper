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
            "INSERT INTO dbo.Tenants (Name, Slug, IsActive) OUTPUT inserted.Id VALUES (@Name, @Slug, 1)",
            new { Name = tenantName, Slug = tenantName.ToLower().Replace(" ", "-") });

        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Users (Email, DisplayName, PasswordHash, IsActive) OUTPUT inserted.Id VALUES (@Email, @DisplayName, @Hash, 1)",
            new { Email = email, DisplayName = "Test User", Hash = hash });

        if (mapped)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TenantId, @UserId, 'Member')",
                new { TenantId = tenantId, UserId = userId });
        }
    }

    [TestMethod]
    public void Login_starts_on_credentials_stage()
    {
        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
        Assert.IsTrue(_vm.IsCredentialsVisible);
        Assert.IsFalse(_vm.IsTenantSelectorVisible);
        Assert.IsFalse(_vm.IsResolvingVisible);
    }

    [TestMethod]
    public async Task Login_with_valid_credentials_moves_to_resolving()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        _vm.Email = "bob@test.com";

        var task = _vm.SignInCommand.ExecuteAsync("Pass123!");
        await Task.Delay(50); // Allow it to transition to Resolving
        
        Assert.AreEqual(LoginStage.Resolving, _vm.CurrentStage);
        Assert.IsTrue(_vm.IsBusy);
        
        await task;
    }

    [TestMethod]
    public async Task Login_with_multiple_tenants_shows_selection_after_resolution()
    {
        // Seed two tenants for the same user
        var hash = BCrypt.Net.BCrypt.HashPassword("Pass123!", 11);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Users (Email, DisplayName, PasswordHash, IsActive) OUTPUT inserted.Id VALUES ('multi@test.com', 'Multi User', @Hash, 1)",
            new { Hash = hash });

        var t1 = await connection.ExecuteScalarAsync<int>("INSERT INTO dbo.Tenants (Name, Slug, IsActive) OUTPUT inserted.Id VALUES ('T1', 't1', 1)");
        var t2 = await connection.ExecuteScalarAsync<int>("INSERT INTO dbo.Tenants (Name, Slug, IsActive) OUTPUT inserted.Id VALUES ('T2', 't2', 1)");

        await connection.ExecuteAsync("INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@T1, @UserId, 'Member')", new { T1 = t1, UserId = userId });
        await connection.ExecuteAsync("INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@T2, @UserId, 'Member')", new { T2 = t2, UserId = userId });

        _vm.Email = "multi@test.com";
        await _vm.SignInCommand.ExecuteAsync("Pass123!");

        Assert.AreEqual(LoginStage.TenantSelection, _vm.CurrentStage);
        Assert.IsTrue(_vm.IsTenantSelectorVisible);
        Assert.AreEqual(2, _vm.AvailableTenants.Count);
    }

    [TestMethod]
    public async Task Login_with_valid_credentials_and_single_tenant_succeeds_directly()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        
        _vm.Email = "bob@test.com";
        bool successCalled = false;
        _vm.OnSignIn = (u, t) => { successCalled = true; };

        await _vm.SignInCommand.ExecuteAsync("Pass123!");

        Assert.IsTrue(successCalled, "OnSignIn should have been invoked immediately as there is only one tenant.");
    }

    [TestMethod]
    public async Task Login_with_invalid_password_stays_on_credentials_with_error()
    {
        await SeedUserAndTenantAsync("bob@test.com", "Pass123!", "IronDev Project");
        _vm.Email = "bob@test.com";

        await _vm.SignInCommand.ExecuteAsync("WrongPass");

        Assert.AreEqual("Invalid email or password.", _vm.EmailError);
        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
    }

    [TestMethod]
    public async Task Login_with_missing_password_shows_error()
    {
        _vm.Email = "bob@test.com";
        await _vm.SignInCommand.ExecuteAsync("");

        Assert.AreEqual("Password is required.", _vm.PasswordError);
        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
    }

    [TestMethod]
    public async Task Back_to_login_from_tenant_selection_resets_to_credentials()
    {
        // Setup state to be in selection
        _vm.CurrentStage = LoginStage.TenantSelection;
        
        _vm.BackToLoginCommand.Execute(null);

        Assert.AreEqual(LoginStage.Credentials, _vm.CurrentStage);
    }
}
