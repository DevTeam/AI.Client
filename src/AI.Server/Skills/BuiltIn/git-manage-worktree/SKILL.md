---
id: git-manage-worktree
name: Git manage worktree
icon: git-branch
kind: playbook
description: Create an isolated Git worktree on a new local branch or remove a clean worktree after checking its path and preserving its branch.
parameters: {"type":"object","properties":{"action":{"type":"string","enum":["create","remove"]},"repositoryPath":{"type":"string"},"worktreePath":{"type":"string"},"branchName":{"type":"string"},"baseCommit":{"type":"string"}},"additionalProperties":false}
tools: ["list_allowed_directories","process_run","ask_user","read_text_file","get_file_info","create_directory","run_skill"]
---

Use `process_run` with Git argument arrays, an absolute `workingDirectory`, and no shell
interpolation. A team may run this playbook after the user chose separate worktrees in
`team-assemble`; that choice authorizes the exact creation and later removal of clean, integrated
team worktrees while retaining their branches. Outside a team,
ask only for the action, repository, destination, branch or base that the user did not specify.

1. Verify Git is available. Resolve `repositoryPath` with `git rev-parse --show-toplevel`; use the
   returned absolute root, not an assumed current directory. Check `git worktree list --porcelain`,
   the repository status and the project directory grants with `list_allowed_directories`. A
   worktree path must be fully qualified, absent for creation or registered for removal, and
   covered by a persistent project recursive read/write grant, not a chat temporary directory.
   Never use a process to reach outside those grants.
   If the chosen path lacks access, ask the user to grant its existing parent through
   `project-directory-add` using `run_skill` or choose an accessible path; do not create it first.
2. For creation, resolve `baseCommit` (or an explicitly chosen ref) to a commit with
   `git rev-parse --verify`, then verify the new branch name with `git check-ref-format --branch`
   and that `refs/heads/<branchName>` does not exist. Check the destination's resolved parent:
   it must not be `.git`, a symlink/junction leading elsewhere, an existing worktree, or an
   unrelated directory. If it is inside the repository, verify the destination is ignored by
   Git before creation; do not modify tracked `.gitignore` or local excludes on the user's behalf.
   Do not assume uncommitted files from the source checkout appear in the new worktree. If its
   parent does not exist, create only that verified parent within the granted directory.
3. When the exact create action is authorized, run `git worktree add -b <branchName>
   <worktreePath> <baseCommit>`. Inspect the result, `git worktree list --porcelain`, the new
   worktree's HEAD, branch and status. If Git fails after creating anything, leave it intact and
   report the actual path and branch for recovery; do not retry with `--force`. When Git fails
   because another process holds a lock — an IDE, another Git command, another chat's run —
   report the holding process and name it instead of freeing it. Never stop, kill or restart a
   process this run did not start; for a Git process this skill started, call `ask_user` naming it
   or its PID and act only on an explicit affirmative answer, while a declined, dismissed,
   expired or interrupted answer leaves it running.
4. For removal, resolve and verify the exact registered worktree path. Check its branch, HEAD,
   `git status --porcelain=v1 -uall`, and whether its commits have been integrated or deliberately
   retained on a named branch. Never remove a dirty worktree or the main checkout. Resolve the
   target and verify it equals the registered worktree path within the granted parent before any
   recursive removal. Show the path and retained
   branch. If removal was not already authorized, ask with "Remove clean worktree (Recommended)"
   and "Keep worktree"; dismissed, expired or interrupted keeps it. Run `git worktree remove
   <worktreePath>` without `--force`, then verify the path is no longer registered. Keep the branch
   for recovery; never delete a branch or prune unrelated worktrees here.
5. Report the created or removed path, branch, base or retained HEAD, and any incomplete action.
