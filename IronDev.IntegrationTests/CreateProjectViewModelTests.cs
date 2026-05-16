using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.ViewModels.Workflow;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CreateProjectViewModelTests
{
    [TestMethod]
    public async Task CreateAsync_ForMissingPathWithoutSkeleton_ShouldShowValidationError()
    {
        var root = Path.Combine(Path.GetTempPath(), "IronDevCreateProject", Guid.NewGuid().ToString("N"));
        var vm = CreateViewModel();
        vm.ProjectName = "Missing Project";
        vm.ProjectPath = root;
        vm.CreateNewProjectSkeleton = false;

        await vm.CreateCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.ErrorMessage, "Local path does not exist");
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task CreateAsync_WithSkeleton_ShouldCreateProjectFilesAndSeedDetectedProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "IronDevCreateProject", Guid.NewGuid().ToString("N"));
        var projectService = new FakeProjectService();
        var profileService = new FakeProjectProfileService();
        var vm = CreateViewModel(projectService, profileService);
        vm.ProjectName = "Alpha Sandbox";
        vm.ProjectPath = root;
        vm.CreateNewProjectSkeleton = true;

        try
        {
            await vm.CreateCommand.ExecuteAsync(null);

            Assert.AreEqual(string.Empty, vm.ErrorMessage);
            Assert.IsTrue(File.Exists(Path.Combine(root, "README.md")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "src", "AlphaSandbox", "AlphaSandbox.csproj")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "tests", "AlphaSandbox.Tests", "AlphaSandbox.Tests.csproj")));
            Assert.AreEqual(root, projectService.CreatedProject?.LocalPath);
            Assert.IsNotNull(profileService.SavedProfile);
            Assert.AreEqual(".NET 10", profileService.SavedProfile.Framework);
            Assert.AreEqual("MSTest", profileService.SavedProfile.TestFramework);
            Assert.AreEqual(root, profileService.SavedProfile.SafeWriteRoot);
            Assert.AreEqual(2, profileService.SavedCommands.Count);
            Assert.IsTrue(profileService.SavedCommands.Exists(c => c.CommandType == "Build" && c.CommandText.Contains("dotnet build")));
            Assert.IsTrue(profileService.SavedCommands.Exists(c => c.CommandType == "Test" && c.CommandText.Contains("dotnet test")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static CreateProjectViewModel CreateViewModel(
        IProjectService? projectService = null,
        IProjectProfileService? profileService = null)
        => new(
            projectService ?? new FakeProjectService(),
            profileService ?? new FakeProjectProfileService(),
            new ProjectProfileDetectionService());

    private sealed class FakeProjectService : IProjectService
    {
        public Project? CreatedProject { get; private set; }

        public Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
        {
            CreatedProject = project;
            return Task.FromResult(123);
        }

        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>(Array.Empty<Project>());

        public Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(CreatedProject);

        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeProjectProfileService : IProjectProfileService
    {
        public ProjectProfile? SavedProfile { get; private set; }
        public List<ProjectCommand> SavedCommands { get; } = new();

        public Task<ProjectProfile?> GetProjectProfileAsync(int projectId, CancellationToken ct = default)
            => Task.FromResult(SavedProfile);

        public Task SaveProjectProfileAsync(ProjectProfile profile, CancellationToken ct = default)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }

        public Task<List<ProjectCommand>> GetProjectCommandsAsync(int projectId, CancellationToken ct = default)
            => Task.FromResult(SavedCommands);

        public Task SaveProjectCommandAsync(ProjectCommand command, CancellationToken ct = default)
        {
            SavedCommands.Add(command);
            return Task.CompletedTask;
        }

        public Task<ProjectCommand?> GetDefaultCommandAsync(int projectId, string commandType, CancellationToken ct = default)
            => Task.FromResult<ProjectCommand?>(null);

        public Task<List<ProjectProfileOption>> GetOptionsByCategoryAsync(string category, CancellationToken ct = default)
            => Task.FromResult(new List<ProjectProfileOption>());
    }
}
