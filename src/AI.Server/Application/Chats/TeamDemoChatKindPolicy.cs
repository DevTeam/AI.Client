namespace AI.Application.Chats;

using System.Text.Json;
using AI.Contracts.Chats;
using AI.Domain.Chats;

/// <summary>
/// The demo chat the application guide sets up to show team work: a lead with two teammates, written
/// without a model. The main branch holds the person's task, the lead's charter, Ada's finished
/// report and Bo's question still waiting for the lead; each teammate's branch holds its brief and
/// its reply. Every team message carries the sender, intent and identity the real protocol would,
/// so the branch rows, sender cards, queue labels and the Team widget show what they show in a real
/// team. See docs/34-asides-and-team-messages.md.
/// </summary>
public sealed class TeamDemoChatKindPolicy : IChatKindPolicy
{
    public const string Title = "Guide team demo";

    private const string Request = "Add a CSV export of orders: an API endpoint, a download button and tests.";

    private const string Plan = "This splits into parts that can run at the same time on separate files, so I am "
        + "running it as a team: Ada builds the endpoint, Bo writes the tests, and I coordinate here.\n\n"
        + "This chat was set up by the application guide to show team work. Nothing ran a model; it is removed "
        + "when the tour ends unless you write in it.";

    private const string Charter = "# Team charter\n\n"
        + "**Goal.** `GET /orders.csv` returns every order as CSV; done when the endpoint and its tests pass.\n\n"
        + "| Teammate | Scope | Owns | Done |\n|---|---|---|---|\n"
        + "| Ada · Backend | The export endpoint | `src/Api/Export` | The endpoint returns the agreed columns |\n"
        + "| Bo · Tests | API tests for the export | `tests/Export` | Every contract case has a passing test |\n\n"
        + "**Contract.** Columns `id,date,total`; dates as ISO 8601; an empty list is the header line alone.\n\n"
        + "**Protocol.** Report `status` as an aside; send `question`, `blocker` and `done` as messages to the lead.";

    private const string AdaBrief = "You are Ada · Backend in this team. Run the team-contribute skill first.\n\n"
        + "Scope: the export endpoint in `src/Api/Export`. Done: it returns the charter's columns.";

    private const string AdaReply = "The endpoint is in place and returns the charter's columns. I reported `done` to the lead.";

    private const string BoBrief = "You are Bo · Tests in this team. Run the team-contribute skill first.\n\n"
        + "Scope: API tests in `tests/Export`. Done: every contract case has a passing test.";

    private const string BoReply = "The tests for the columns and the empty list are written. The charter does not say "
        + "which delimiter to use, so I asked the lead and wait for the answer.";

    private const string AdaDone = "Endpoint ready: `GET /orders.csv` returns `id,date,total`, an empty list gives the "
        + "header alone. Checked with the endpoint's own test.";

    private const string LeadAck = "Ada · Backend is done: the endpoint matches the charter's columns. Bo · Tests is still "
        + "writing the tests.";

    private const string BoQuestion = "Which delimiter should the CSV use, a comma or a semicolon? The charter fixes the "
        + "columns but not the separator, and the tests depend on it.";

    /// <summary>What <see cref="InitializeAsync"/> writes; more means the person has written in it.</summary>
    private const int InitialMessages = 10;

    public ChatKind Kind => ChatKind.TeamDemo;
    public ChatKindBehavior Behavior { get; } = new();

    public void ValidateState(JsonElement? state, int version)
    {
        ConversationChatKindPolicy.RequireVersion(version);
        ConversationChatKindPolicy.RequireEmpty(state);
    }

    public async Task<ChatDetails> InitializeAsync(ChatDetails chat, IChatService chats, CancellationToken token)
    {
        var lead = new MessageSender(chat.Id, chat.Id, "decision");
        var task = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(task, null, "User", Request, chat.Revision), token);
        var plan = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(plan, task, "Assistant", Plan, chat.Revision), token);
        var charter = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(charter, plan, "User", Charter, chat.Revision,
            BranchId: chat.Id, Delivery: MessageDelivery.Aside, Sender: lead), token);

        // Each teammate's branch starts from the charter, as team-assemble starts it.
        var ada = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(ada, charter, "User", AdaBrief, chat.Revision,
            BranchId: ada, ParentBranchId: chat.Id, Sender: lead, BranchMember: new TeamMember("Ada", "Backend")), token);
        chat = await Append(chat, chats, new AppendChatMessageRequest(Guid.CreateVersion7(), ada, "Assistant", AdaReply,
            chat.Revision, BranchId: ada), token);
        var bo = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(bo, charter, "User", BoBrief, chat.Revision,
            BranchId: bo, ParentBranchId: chat.Id, Sender: lead, BranchMember: new TeamMember("Bo", "Tests")), token);
        chat = await Append(chat, chats, new AppendChatMessageRequest(Guid.CreateVersion7(), bo, "Assistant", BoReply,
            chat.Revision, BranchId: bo), token);

        // Their messages to the lead: Ada's part is done; Bo's question has no answer yet.
        var done = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(done, charter, "User", AdaDone, chat.Revision,
            BranchId: chat.Id, Sender: new MessageSender(chat.Id, ada, "done")), token);
        var ack = Guid.CreateVersion7();
        chat = await Append(chat, chats, new AppendChatMessageRequest(ack, done, "Assistant", LeadAck, chat.Revision,
            BranchId: chat.Id), token);
        return await Append(chat, chats, new AppendChatMessageRequest(Guid.CreateVersion7(), ack, "User", BoQuestion,
            chat.Revision, BranchId: chat.Id, Sender: new MessageSender(chat.Id, bo, "question")), token);
    }

    private static async Task<ChatDetails> Append(ChatDetails chat, IChatService chats, AppendChatMessageRequest request,
        CancellationToken token) =>
        await chats.AppendMessageAsync(chat.ProjectId, chat.Id, request, token)
        ?? throw new InvalidOperationException("The team demo chat could not be written.");

    public string? NavigationMode(JsonElement? state) => null;
    public bool AllowsServer(Guid serverId) => true;
    public bool AllowsTool(Guid serverId, string toolName) => true;

    public async Task<bool> ShouldCleanUpAsync(StoredChatSummary summary,
        Func<CancellationToken, Task<ChatDetails?>> loadChat, CancellationToken token)
    {
        if (summary.Title != Title) return false;
        return await loadChat(token) is { Messages.Count: <= InitialMessages } chat && chat.Kind == Kind.Value;
    }

    public Task OnHostStartedAsync(CancellationToken token) => Task.CompletedTask;
    public Task OnHostStoppingAsync(CancellationToken token) => Task.CompletedTask;
}
