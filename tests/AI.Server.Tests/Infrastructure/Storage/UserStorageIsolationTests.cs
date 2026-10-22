namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Projects;
using AI.Application.Users;
using AI.Contracts.Settings;
using AI.Contracts.Usage;
using AI.Domain.Projects;
using AI.Domain.Users;
using AI.Infrastructure.Settings;
using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

/// <summary>
/// One repository instance serves every user, as the singletons of the Host do; what one user stores
/// must not be read for another.
/// </summary>
public sealed class UserStorageIsolationTests
{
    private static readonly UserId Other = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
    private readonly AmbientUserContext _users = new();
    private readonly MemoryFileSystem _files = new();
    private readonly InstanceStorageLocation _instance = new(new ServerOptions("data", null, true));

    [Fact]
    public void UserRootIsUnderTheInstanceRootAndFollowsTheScope()
    {
        var location = new UserStorageLocation(_instance, _users);

        Should.Throw<InvalidOperationException>(() => location.RootDirectory);
        using (_users.Enter(UserId.Local))
        {
            location.RootDirectory.ShouldBe(Path.Combine(_instance.RootDirectory, "users",
                "00000000000000000000000000000001"));
        }

        using (_users.Enter(Other))
        {
            location.RootDirectory.ShouldBe(Path.Combine(_instance.RootDirectory, "users", Other.Value.ToString("N")));
        }
    }

    [Fact]
    public async Task ProjectsOfOneUserAreAbsentForAnother()
    {
        var token = TestContext.Current.CancellationToken;
        using var repository = new JsonProjectRepository(_files,
            new ProjectStoragePaths(new UserStorageLocation(_instance, _users)), new ProjectDocumentSerializer());
        var project = new Project(new ProjectId(Guid.CreateVersion7()), "Project", "", DateTimeOffset.UnixEpoch);

        using (_users.Enter(UserId.Local))
        {
            (await repository.SaveAsync(project, 0, token)).ShouldBe(ProjectSaveResult.Saved(1));
            (await repository.GetAsync(project.Id, token)).ShouldNotBeNull();
        }

        using (_users.Enter(Other))
        {
            (await repository.GetAsync(project.Id, token)).ShouldBeNull();
            (await repository.ListAsync(token)).ShouldBeEmpty();
        }

        using (_users.Enter(UserId.Local))
        {
            (await repository.ListAsync(token)).Single().Project.Id.ShouldBe(project.Id);
        }
    }

    [Fact]
    public async Task SettingsAreWrittenUnderTheirUsersRoot()
    {
        var token = TestContext.Current.CancellationToken;
        var location = new UserStorageLocation(_instance, _users);
        using var repository = new JsonGlobalSettingsRepository(_files, new GlobalSettingsPaths(location));

        using (_users.Enter(UserId.Local))
        {
            await repository.SaveAsync(new GlobalSettings([], [], []), token);
        }

        using (_users.Enter(UserId.Local))
        {
            _files.Files.Keys.ShouldContain(Path.Combine(location.RootDirectory, "settings.json"));
        }

        using (_users.Enter(Other))
        {
            _files.Files.Keys.ShouldNotContain(Path.Combine(location.RootDirectory, "settings.json"));
        }
    }

    [Fact]
    public async Task UsageMonthsAreNotSharedBetweenUsers()
    {
        var token = TestContext.Current.CancellationToken;
        using var ledger = new JsonLinesTokenUsageLedger(new UserStorageLocation(_instance, _users), _files, _files.Path);

        using (_users.Enter(UserId.Local))
        {
            await ledger.AppendAsync(Record(), token);
            // Loads the month into the ledger's cache.
            (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, token)).Count.ShouldBe(1);
        }

        using (_users.Enter(Other))
        {
            (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, token)).ShouldBeEmpty();
            await ledger.AppendAsync(Record(), token);
            await ledger.AppendAsync(Record(), token);
        }

        using (_users.Enter(UserId.Local))
        {
            (await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, token)).Count.ShouldBe(1);
        }
    }

    private static TokenUsageRecord Record() =>
        new(Guid.CreateVersion7(), new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), TokenUsagePurpose.Answer,
            "m", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new TokenCounts(1, 1), false,
            1_000, null, null);
}
