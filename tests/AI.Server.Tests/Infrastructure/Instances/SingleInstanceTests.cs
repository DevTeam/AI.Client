namespace AI.Infrastructure.Tests.Instances;

using AI.Contracts.FileSystem;
using AI.Infrastructure.Instances;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-instance-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingHandoff _handoff = new();

    private SingleInstance NewInstance(TimeSpan? answerTimeout = null) =>
        new(new SystemFileSystem(), _handoff) { AnswerTimeout = answerTimeout ?? TimeSpan.FromSeconds(5) };

    [Fact]
    public void ShouldMakeTheFirstLaunchTheInstance()
    {
        using var first = NewInstance().Claim(_root, InstanceRequest.Activate);

        first.IsForwarded.ShouldBeFalse();
        File.Exists(Path.Combine(_root, ".desktop.lock")).ShouldBeTrue();
    }

    [Fact]
    public void ShouldHandALaterLaunchToTheRunningInstance()
    {
        using var first = NewInstance().Claim(_root, InstanceRequest.Activate);
        using var activated = new ManualResetEventSlim();
        first.OnRequest(_ =>
        {
            activated.Set();
            return true;
        });

        using var second = NewInstance().Claim(_root, InstanceRequest.Activate);

        second.IsForwarded.ShouldBeTrue();
        activated.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        _handoff.ProcessIds.ShouldBe([Environment.ProcessId]);
    }

    [Fact]
    public void ShouldPassOnTheRequestOfALaterLaunch()
    {
        using var first = NewInstance().Claim(_root, InstanceRequest.Activate);
        var requests = new List<InstanceRequest>();
        first.OnRequest(request =>
        {
            lock (requests) requests.Add(request);
            return true;
        });

        using var second = NewInstance().Claim(_root, InstanceRequest.NewWindow);
        using var third = NewInstance().Claim(_root, InstanceRequest.Activate);

        lock (requests) requests.ShouldBe([InstanceRequest.NewWindow, InstanceRequest.Activate]);
    }

    [Fact]
    public void ShouldDeliverARequestThatCameBeforeTheWindow()
    {
        using var first = NewInstance().Claim(_root, InstanceRequest.Activate);
        using var second = NewInstance().Claim(_root, InstanceRequest.Activate);
        var activations = 0;

        first.OnRequest(_ =>
        {
            Interlocked.Increment(ref activations);
            return true;
        });

        second.IsForwarded.ShouldBeTrue();
        activations.ShouldBe(1);
    }

    [Fact]
    public void ShouldKeepDifferentDataDirectoriesApart()
    {
        var other = _root + "-other";
        try
        {
            using var first = NewInstance().Claim(_root, InstanceRequest.Activate);
            using var second = NewInstance().Claim(other, InstanceRequest.Activate);

            second.IsForwarded.ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(other)) Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void ShouldTakeOverOnceTheInstanceHasExited()
    {
        NewInstance().Claim(_root, InstanceRequest.Activate).Dispose();

        using var second = NewInstance().Claim(_root, InstanceRequest.Activate);

        second.IsForwarded.ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldTakeOverFromAWindowThatIsClosing()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var first = NewInstance().Claim(_root, InstanceRequest.Activate);
        first.OnRequest(_ => false);
        var closing = Task.Run(async () =>
        {
            await Task.Delay(300, cancellation);
            first.Dispose();
        }, cancellation);

        using var second = await Task.Run(() => NewInstance().Claim(_root, InstanceRequest.Activate), cancellation);
        await closing;

        second.IsForwarded.ShouldBeFalse();
    }

    [Fact]
    public void ShouldStartOnItsOwnWhenTheHolderNeverAnswers()
    {
        Directory.CreateDirectory(_root);
        // A holder without the channel, such as an older build.
        using var held = new FileStream(Path.Combine(_root, ".desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        using var second = NewInstance(TimeSpan.FromSeconds(1)).Claim(_root, InstanceRequest.Activate);

        second.IsForwarded.ShouldBeFalse();
        _handoff.ProcessIds.ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingHandoff : IForegroundHandoff
    {
        private readonly List<int> _processIds = [];

        public IReadOnlyList<int> ProcessIds
        {
            get
            {
                lock (_processIds) return _processIds.ToArray();
            }
        }

        public void AllowForeground(int processId)
        {
            lock (_processIds) _processIds.Add(processId);
        }
    }
}
