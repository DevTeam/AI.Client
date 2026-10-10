---
id: project-security-review
name: Project security review
icon: shield
aliases: ["grants"]
kind: playbook
description: Review the current project's directory grants and tool policies and suggest tighter settings; changes nothing.
parameters: {"type":"object","properties":{},"additionalProperties":false}
tools: ["app_read"]
---

1. Read the current project with `app_read` resource=Project and the global settings with
   resource=Settings. Never ask for or show credentials.
2. Report briefly, grouped by risk:
   - directory grants: root, recursive or not, read or write; flag home directories, drive roots,
     system folders and write access that nothing in the project seems to need;
   - MCP servers bound to the project and whether they are enabled;
   - project tool policies that always allow destructive tools, and missing call limits or
     timeouts on tools that reach the network or run processes.
3. End with at most five concrete suggestions, each naming the skill or setting that applies it,
   for example `project-directory-remove`. Do not change anything yourself.
