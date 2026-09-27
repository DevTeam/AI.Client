namespace AI.Infrastructure.Tests.Credentials;

using AI.Infrastructure.Credentials;
using Shouldly;
using Xunit;

/// <summary>
/// The keyring stores against scripted <c>security</c> and <c>secret-tool</c> answers, so the
/// decisions that keep credentials readable are checked on any machine.
/// </summary>
public sealed class KeyringMasterKeyStoreTests
{
    private readonly MasterKeyFormat _format = new();

    [Fact]
    public void KeychainShouldReturnAnExistingKey()
    {
        var key = _format.Create();
        var commands = new ScriptedCommands(new CommandResult(0, _format.Encode(key) + "\n", ""));

        new KeychainMasterKeyStore(commands, _format).TryGetOrCreate().ShouldBe(key);
        commands.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public void KeychainShouldCreateAMissingKeyAndCheckThatItWasKept()
    {
        var commands = new ScriptedCommands(new CommandResult(44, "", "The specified item could not be found."));
        commands.OnAdd = encoded => [new CommandResult(0, "", ""), new CommandResult(0, encoded, "")];

        var key = new KeychainMasterKeyStore(commands, _format).TryGetOrCreate();

        key.ShouldNotBeNull();
        commands.Calls.Select(call => call[0]).ShouldBe(["find-generic-password", "add-generic-password", "find-generic-password"]);
    }

    [Fact]
    public void KeychainShouldGiveUpWhenTheKeyDidNotStay()
    {
        var commands = new ScriptedCommands(new CommandResult(44, "", ""));
        commands.OnAdd = _ => [new CommandResult(0, "", ""), new CommandResult(44, "", "")];

        new KeychainMasterKeyStore(commands, _format).TryGetOrCreate().ShouldBeNull();
    }

    [Fact]
    public void KeychainShouldGiveUpOnAnyOtherFailure()
    {
        new KeychainMasterKeyStore(new ScriptedCommands(new CommandResult(51, "", "User interaction is not allowed.")), _format)
            .TryGetOrCreate().ShouldBeNull();
    }

    [Fact]
    public void SecretServiceShouldGiveUpWhenTheToolIsMissing()
    {
        new SecretServiceMasterKeyStore(new ScriptedCommands(null), _format).TryGetOrCreate().ShouldBeNull();
    }

    [Fact]
    public void SecretServiceShouldGiveUpWhenTheServiceIsNotRunning()
    {
        var commands = new ScriptedCommands(new CommandResult(1, "", "Cannot autolaunch D-Bus without X11 $DISPLAY"));

        new SecretServiceMasterKeyStore(commands, _format).TryGetOrCreate().ShouldBeNull();
        commands.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public void SecretServiceShouldStoreAMissingKeyThroughStandardInput()
    {
        var commands = new ScriptedCommands(new CommandResult(1, "", ""));
        commands.OnAdd = encoded => [new CommandResult(0, "", ""), new CommandResult(0, encoded, "")];

        var key = new SecretServiceMasterKeyStore(commands, _format).TryGetOrCreate();

        key.ShouldNotBeNull();
        commands.Inputs.ShouldContain(_format.Encode(key));
        // The key never appears on a command line, where other users could read it.
        commands.Calls.SelectMany(call => call).ShouldNotContain(_format.Encode(key));
    }

    /// <summary>
    /// Answers the first lookup with <c>first</c>; once a key is written, answers with what
    /// <see cref="OnAdd"/> returns for it.
    /// </summary>
    private sealed class ScriptedCommands(CommandResult? first) : ICommandRunner
    {
        private readonly Queue<CommandResult?> _answers = new([first]);

        public Func<string, CommandResult[]> OnAdd { get; set; } = _ => [];

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public List<string> Inputs { get; } = [];

        public CommandResult? Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
        {
            Calls.Add(arguments);
            if (standardInput is not null) Inputs.Add(standardInput);
            var written = arguments[0] is "add-generic-password" or "store"
                ? standardInput ?? arguments[arguments.Count - 1]
                : null;
            if (written is not null)
            {
                foreach (var answer in OnAdd(written)) _answers.Enqueue(answer);
            }

            return _answers.Dequeue();
        }
    }
}
