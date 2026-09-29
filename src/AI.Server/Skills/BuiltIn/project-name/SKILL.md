---
id: project-name
name: Project name
description: Suggest a clearer, concise name for a project from its saved details.
parameters: {"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"}},"required":["name","description"],"additionalProperties":false}
result: {"type":"object","properties":{"name":{"type":"string","minLength":3,"maxLength":80}},"required":["name"],"additionalProperties":false}
tools: []
---

Use the project name and description supplied in parameters to infer its purpose. Return one
JSON object with a `name` property. Keep it specific and short, in the language of the project.
The caller reads the project through `app_read` before invoking this skill and applies any
requested rename through `app_projects` with the project's current revision.
