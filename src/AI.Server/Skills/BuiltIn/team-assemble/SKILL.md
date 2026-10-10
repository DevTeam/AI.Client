---
id: team-assemble
name: Team assemble
icon: users
kind: playbook
aliases: ["team"]
description: Assemble a team or subteam for substantial parallel work, match enabled models to roles, offer separate Git worktrees when usable, and start a branch per teammate after the user's choice.
parameters: {"type":"object","properties":{"task":{"type":"string","description":"The task"}},"additionalProperties":false}
tools: ["app_read","app_runs","app_navigate","ask_user","list_allowed_directories","list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","process_run","run_skill"]
---

The user's instructions take precedence over this playbook. Ask questions and report results in
the user's language; write the charter and the briefs in that language too. This branch becomes
the team's coordination channel and this run its lead. How team messages work is described in
docs/34-asides-and-team-messages.md: every message `app_runs` submits carries this branch as its
sender, `intent` says what it is, and mode Aside adds information without starting a turn.

1. Take `projectId`, `chatId` and `branchId` from output.context. This branch is the team's lead,
   including when it is itself a member of a parent team. Read this branch with `app_read` resource=Messages for what is already
   known, and the project's files only as far as the analysis needs them. If this branch is a member
   of a parent team, its brief and the parent charter bound its scope, owned paths, contracts and
   workspace mode. A subteam can subdivide only that assignment; its lead remains accountable to
   the parent lead. Do not build a subteam merely to add another management layer.
2. Analyse the task before deciding anything:
   - **Goal and done**: what result is wanted, and how its completion is checked.
   - **Domain**: the terms of the task as a short glossary, so every teammate names things the same.
   - **Areas**: the parts of the system or of the subject the task touches.
   - **Plan**: phases in order, each with its outcome.
3. Decompose the plan into parts a single teammate could own: for each, its scope, the paths it
   owns (none may be owned twice), its inputs and outputs, what it depends on and its size.
   Contracts between parts (interfaces, data shapes, file formats) are fixed here, not discovered
   later. When one part implements a contract and another tests it, write the contract's edge
   cases as examples, input → output (empty values, separators, limits, paths that leave a root),
   so both read the same answer instead of each inferring its own.
   Keep the charter compact and decision-oriented: record the result of analysis, not the exploration
   that produced it. For each teammate prepare a working brief with only its goal, owned paths,
   contract examples, dependencies, definition of done and the first executable step. Do not copy
   the whole plan or the same background into every brief: the charter is already in each branch's
   history. A teammate should not need to re-plan the whole team to start its assigned work.
4. Decide whether a team pays off. All of these must hold:
   - at least two parts can run at the same time;
   - their owned paths do not overlap, and shared files have one owner;
   - the contracts between them can be fixed now;
   - each part is substantial work, not a lookup.
   If one fails, do not assemble a team. Say which condition failed and recommend working in this
   branch, or `spawn_subtask` for short independent lookups whose results only need collecting.
   If the person asked for a team regardless, put both options to them with `ask_user` first;
   for a subteam, send the tradeoff to its parent lead instead.
5. Choose 2 to 4 teammates, or the size and roles the user asked for. Typical
   roles: architect, backend, frontend, tests and QA, reviewer, researcher, documentation, DevOps.
   Give each teammate one role, one scope, owned paths, the deliverable, its definition of done
   and the skills that fit its work (for example code-feature-implement, code-tests-add,
   code-review, qa-cases-create). Mark a role as a potential subteam lead only when its own scope
   independently passes step 4; otherwise it does the work directly. Keep the parent's owned paths
   partitioned among its own work and the subteam, with no overlap between simultaneous editors.
   Give each teammate an identity: a name taken in order from Ada, Bo, Cleo, Dan, Eva, Finn, Gia,
   Hal (skipping any this chat already uses) and its role in one or two words in the charter's
   language, written the same way everywhere — "Ada · Backend". The identity is fixed for the
   life of the team; the teammate's focus belongs to its scope, not to its name.
   Never make up an `operationId`: omit it, and the application assigns one and returns it as the
   result's `messageId`.
   Read `app_read` resource=Settings for enabled Connections and resource=Chat with this `branchId`
   for `effectiveBranchSettings`. For each role, recommend an existing connection only when its
   saved `Capability`, `GoodFor`, context limit, image input and known prices support that role's
   actual requirements. Say which evidence supports the recommendation and which fields are
   unknown; never infer ability, speed or price from a model name. Respect the user's stated
   quality, speed, cost or provider preference. The `ForSubtasks` pool belongs to `spawn_subtask`,
   not chat branches. Prefer inheritance when no different connection has a supported advantage.
   A subteam lead may use one connection for coordination while its children inherit or override
   it according to their own work.
   If a connection explicitly configures reasoning effort and its endpoint supports that value,
   factor it into role choice:
   prefer less effort for bounded implementation and lookup, and more for ambiguous contracts,
   integration and review. Never infer an effort setting or provider support from a model name.
   When the endpoint is known to accept Chat Completions `reasoning_effort`, propose an initial
   branch override per role only if it has a clear task-specific benefit; otherwise inherit the
   connection or parent setting. State the proposed effort in the team proposal and charter.
6. For a team editing files in a Git repository, check whether separate Git worktrees are usable
   before asking the user. Offer
   them only when every teammate can finish its assigned edits from the same committed base;
   a nested team must use the parent charter's workspace mode and cannot offer another one;
   a later teammate phase that needs another teammate's new files uses the shared directory in a
   top-level team; a nested team must ask its parent lead to revise the phase instead. Use
   `process_run` with Git argument arrays and a verified repository root to check Git availability,
   the current branch and HEAD, `git status --porcelain=v1 -uall`, and `git worktree list --porcelain`.
   Prefer distinct worktree paths such as `<root>/.worktrees/<chatId>-<member>` when that parent is
   ignored and the repository root is granted for read and write. Check that each path is absent,
   has no registered worktree, and will not be tracked by the
   repository (for an in-repository parent, check its ignore rule). If none exists, a separate
   existing directory can be chosen and granted after the user selects worktree mode. Do not use a
   per-chat temporary directory: teammates must see the same persistent path. Never use
   a process to bypass a missing directory grant. The lead's integration checkout must be clean,
   including staged and untracked changes: worktrees start at committed HEAD and integration
   needs a clean checkout. If assignments depend on one another's new files, the checkout is
   dirty, or Git/worktree tools are unavailable, offer only the shared directory for a top-level
   team and explain why; for a nested team ask the parent lead to revise the phase;
   do not stash, commit or discard existing work to enable this mode.
   Research-only teams and non-Git projects use the shared directory.
   For a subteam in worktree mode, the lead's assigned worktree is its integration checkout. Its
   children start from that worktree's committed HEAD after verifying it is clean. Their paths
   stay within the parent's owned paths, and each gets a distinct Git branch and granted worktree
   destination. If the lead worktree is dirty or children need its uncommitted files, ask the
   parent lead to revise the phase; do not silently switch the subteam to shared-directory mode.
7. For a top-level team, show the proposed team with `ask_user`, labelled "Team": the plan in one
   line per phase and one line per teammate, including its proposed connection and reasoning effort
   or their inheritance, and
   any proposed subteam.
   Name this lead branch's effective connection and explain that descendants inherit later changes
   to it unless they override the field. Do not change the parent team's connection to optimize
   one role. When step 6 found independent assignments, a clean Git checkout and usable
   tools, offer "Start in separate Git worktrees (Recommended)", "Start in the shared directory",
   "Adjust the team" and "Work without a team";
   explain that worktrees start at the chosen committed HEAD, teammates make local commits for
   integration, clean worktrees are removed afterward while their branches remain, and no branch
   is pushed. Show the proposed base, Git branch names and worktree paths when already known. If
   no granted destination exists, say that choosing worktrees also needs an existing
   destination directory and a project grant. Otherwise keep "Start the team (Recommended)",
   "Adjust the team" and "Work without a team". Set `allowOther` on. Adjust and repeat on an answer;
   stop on "Work without a team", dismissed, expired or interrupted. This single answer authorizes
   the selected team mode and the shown per-role connections; never switch modes or models silently
   after a failure. If a proposed approval mode or tool policy would loosen access, obtain the
   person's explicit choice of that exact branch scope and effect on descendants before applying
   it. Otherwise inherit the current approval mode and tool policies.
   For a subteam, send the same concrete proposal to the parent lead with `app_runs` mode Send,
   intent `Question`, unless the brief already specifies its decomposition, connections and
   workspace mode. Carry on only after an `Answer`; the parent lead asks the person when the
   decision belongs to them. A nested proposal never expands the parent charter's paths or
   silently changes its workspace mode.
8. If worktrees were chosen but no suitable granted destination exists, ask the person for one
   existing directory with `ask_user` (`pathKind` "directories") in a top-level team. In a nested
   team, request the destination from its parent lead first; the parent handles any user decision
   and obtains a missing grant. In a top-level team, use `project-directory-add` through `run_skill`
   for recursive read/write access. If no destination
   or grant is selected, stop without creating a team. Recheck that the destination is outside
   the repository or ignored inside it, and plan a distinct absent child path per teammate,
   for example `<destination>/<repository-name>-<chatId>-<member>`.
   Run `git-manage-worktree` with `run_skill` and `action=create` for each teammate, passing the
   verified repository root, exact empty destination, a unique team branch name and the same
   verified base commit. Do not create a chat branch until every worktree is ready. If creation
   stops partway through, report the created paths and branches for recovery and ask how to proceed;
   do not discard them or fall back to the shared directory. The team's Git branches are separate
   from its chat branches.
   In a nested team, stop when the required grant is not available after the parent lead's answer.
9. Write the charter into this branch: `app_runs` with `operation` `Submit`, `branchId` = this lead's `branchId`, mode `Aside`,
   intent `Decision`, no `operationId`, and content headed "Team charter" with: goal and done,
   glossary, phases, a table of teammates whose first column is the identity ("Ada · Backend"),
   then scope, owned paths, deliverable and done; the lead as "Lead"; the contracts; and the
   protocol below. Record the chosen workspace mode. For worktrees, record the repository root,
   base commit, each teammate's absolute worktree path and Git branch, and the lead's integration
   checkout. Record the proposed connection and reasoning effort for each role and whether each
   inherits from this lead or the connection.
   For a subteam record the parent lead's identity, branch, assigned scope and integration checkout.
   Owned paths remain repository-relative and must be resolved inside the assigned
   worktree. A compact Mermaid diagram may follow the text when it clarifies actual phases,
   dependencies or ownership. Keep every assignment, contract and decision explicit in text: the
   diagram is a visual summary, not a source of instructions for teammates. Keep the result's
   `messageId`: it is the charter's id. Name teammates by their identity in the phases and
   contracts too. Make that call alone in its step: the aside joins this turn after the call's
   result, and the branches must start from it.
10. In the next step, read this lead branch with `app_read` and confirm the charter message is there.
   Then for each teammate: `app_runs` with `operation` `Submit`, `branchId` = this lead's `branchId`, mode `Fork`,
   `parentMessageId` = the charter's id, no `operationId`, intent `Decision`, `wait` false, `memberName` and `role` = its identity (no `title`: the
   application names the branch "Name · Role" and signs its messages so), content = the teammate's
   brief: "You are <Name> · <Role> in this team. Run the team-contribute skill first." followed by the
   lead branch id (this branch's `branchId`), scope, owned paths, deliverable, definition of done,
   skills to use and whom to ask. In worktree
   mode include its exact absolute worktree path, Git branch and base commit. Pass `branchSettings`
   in this Fork for each approved role-specific `connectionId`, `reasoningEffort`, `approvalMode` or `toolPolicies`;
   omit fields that should inherit. For a tool policy use the exact `serverId`, `name` and
   `schemaHash` from `app_read` resource=McpTools, and include its decision, call limit and timeout
   (null when inherited). These initial settings are saved with the branch before its first model
   call. An enabled connection id must come from the Settings read; inspect the branch's effective
   settings with `app_read` after the Fork if the tool result is uncertain. Never fork first and
   then race its first turn with `SetBranchSettings`. For a subteam lead, explicitly ask it in the
   brief to run `team-assemble` within its assignment, integrate its children, and report one
   completed result to this branch. Every branch
   inherits the analysis and the charter from the message it starts from, so the brief repeats
   only what is the teammate's own. Each result's `branchId` is that teammate's branch; every
   message it sends you carries the same branchId in its header.
   End each brief with a compact handoff: "Current decision", "First step", and "Open dependency"
   (use "none" when there is no dependency). These are facts and actions, not a request for the
   teammate to explain its private reasoning.
11. If this is a subteam, first send its parent lead one `Status` Aside with the approved members,
   assigned paths, models and expected deliverable. Later questions and the integrated `Done`
   follow the parent-team protocol; do not make the parent inspect child branches. Then answer
   with the team as a table (identity, deliverable) and one line on how it works: the
   teammates report to this branch, which coordinates them with the team-coordinate skill. When
   a visual helps the user understand the team, add one small `mermaid` fenced diagram that shows
   the actual phase or dependency relationships. Choose a suitable diagram type; prefer a
   top-to-bottom flowchart for dependencies, and do not invent sequencing between parallel parts.
   Use the same teammate identities and terms as the charter. Keep the table and essential facts
   in text so the diagram remains optional for readers and model teammates. Omit the diagram if
   it repeats the table without adding clarity or delays starting the work. Do not wait for the
   teammates in this turn, and do not read their branches over and over: what they
   send arrives here as messages.

Protocol to put in the charter:
- Teammates write to this lead branch with `app_runs` with `operation` `Submit`, `branchId` = the lead branch id recorded in their brief.
- `status` goes as an Aside: it costs the lead nothing until its next step. `question`, `blocker`
  and `done` go as ordinary messages and wake the lead. No acknowledgements, no thanks.
- Every message is self-contained: what, why, and what is needed back.
- Reports carry only new information. A question gives the concrete case, options, recommended
  answer and what is blocked. A done report names deliverables and verification evidence. Do not
  repeat the charter, earlier reports, tool transcripts or private reasoning.
- Everyone is named by identity — "Ada · Backend", "Lead" — in plans, decisions and reports; the
  application signs each message with its sender's identity, so nobody introduces themselves.
- A teammate changes only its owned paths; anything else is a question to the lead.
- The charter's workspace mode applies to every teammate. In worktree mode, file operations and
  commands use that teammate's assigned worktree; local commits are reported to the lead, who
  integrates them. In shared-directory mode, nobody changes the shared Git state except the lead.
- A teammate starts changing its owned paths early and grows the result; reading without changing
  anything is not progress.
- A conflict between a contract, a reference and another teammate's work is a question to the lead
  as soon as it is found; until the lead decides, the charter's contract holds.
- Before each major step a teammate reads this lead branch since the charter for new decisions.
- Teammates do not message each other; the lead routes what one needs from another.
- Nobody polls: waiting for an answer means ending the turn, and the answer arrives as a message.
  Reading the same branch again and again with nothing new in it stops the turn.
