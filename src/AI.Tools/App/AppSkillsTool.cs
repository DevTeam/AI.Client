namespace AI.Mcp.App;

using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

public enum SkillOperation { Save, Delete }

[McpServerToolType]
public sealed class AppSkillsTool(ISkillCatalog catalog, IAppWrites writes) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) =>
        new Session(catalog, writes, run, reply).Create();

    private sealed class Session(ISkillCatalog catalog, IAppWrites writes, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(ExecuteAsync,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "Create, update, disable or delete a User or current Project SKILL.md. Save needs full "
                              + "SKILL.md content, scope, revision (0 to create) and enabled. Read the current skill first "
                              + "and pass its revision to update. Built-in skills are read-only. Delete rehearses by default; "
                              + "set dryRun=false to apply. Use a fresh operationId per change. 'kind: generic' skills run without "
                              + "tools on the data passed in parameters and need a result schema; 'kind: playbook' skills are "
                              + "instructions for the calling model, list the tools they use in 'tools' and have no result schema. "
                              + "Read a skill's full document and revision with app_read resource=Skills."
            });

        [McpServerTool(Name = "app_skills", ReadOnly = false, Destructive = true, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(AppWriteResult))]
        private Task<CallToolResult> ExecuteAsync(SkillOperation operation, Guid operationId, string scope,
            string? content = null, string? skillId = null, long revision = 0, bool enabled = true,
            bool? dryRun = null, CancellationToken cancellationToken = default) =>
            writes.RunAsync($"Skill.{operation}", operationId, async builder =>
            {
                var projectId = scope == "Project" ? run.ProjectId : (Guid?)null;
                if (scope is not ("User" or "Project") || scope == "Project" && run.ProjectId == Guid.Empty)
                    return builder.Failed("Choose User or the current Project scope.");
                if (operation == SkillOperation.Save)
                {
                    if (dryRun == true) return builder.Failed("Save does not support dryRun.");
                    var saved = await catalog.SaveAsync(new SkillWriteRequest(scope, projectId,
                        content ?? string.Empty, revision, enabled), cancellationToken);
                    return saved.Status switch
                    {
                        "Saved" => builder.Applied($"Saved skill '{saved.Skill!.Name}'.", projectId,
                            revision: saved.Skill.Revision, current: JsonSerializer.SerializeToElement(saved.Skill, reply.Json)),
                        "Conflict" => builder.Conflict(saved.Skill!.Revision,
                            JsonSerializer.SerializeToElement(saved.Skill, reply.Json), projectId),
                        _ => builder.Failed(saved.Error ?? "Skill was not saved.", projectId)
                    };
                }
                if (operation != SkillOperation.Delete) return builder.Failed("Unknown skill operation.");
                if (string.IsNullOrWhiteSpace(skillId)) return builder.Failed("Delete needs skillId.");
                var current = (await catalog.ListAsync(projectId, cancellationToken))
                    .FirstOrDefault(skill => skill.Id == skillId && skill.Source == scope);
                if (current is null) return builder.Failed("Skill not found.");
                if (dryRun != false)
                    return builder.Planned($"Would delete skill '{current.Name}'.", projectId,
                        revision: current.Revision, current: JsonSerializer.SerializeToElement(current, reply.Json));
                var deleted = await catalog.DeleteAsync(skillId, scope, projectId, revision, cancellationToken);
                return deleted.Status switch
                {
                    "Deleted" => builder.Applied($"Deleted skill '{current.Name}'.", projectId),
                    "Conflict" => builder.Conflict(deleted.Skill!.Revision,
                        JsonSerializer.SerializeToElement(deleted.Skill, reply.Json), projectId),
                    _ => builder.Failed(deleted.Error ?? "Skill was not deleted.", projectId)
                };
            });
    }
}
