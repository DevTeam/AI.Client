# Local directory dialogue regression (2026-11-11)

## Status

Reproduced; unresolved. Do not treat the earlier successful greeting or direct-function smoke
tests as evidence that Qwen3 0.6B can reliably complete discovery-based file tasks at 4K.

## Reproduction

The opt-in production-agent test now also creates an isolated workspace containing `alpha.txt`
and `notes.md`, grants read access, and records its path in the test project's description.
It sends the user's sequence: a greeting, a question about available files, and a clarification
that the question concerns the working directory. Assertions require a real structured
`list_directory` call, a successful tool result containing the filenames, and a final answer
based on that result. Text containing a call is not accepted or executed.

Enable `AI_LOCAL_AGENT_LIVE_TEST=1` with `AI_LOCAL_MODEL_DIRECTORY` pointing to the isolated
Qwen Q4_K_M package at context 4096. Use a runtime port different from the active Desktop.
`AI_LOCAL_AGENT_REPORT_DIRECTORY` retains a synthetic directory transcript and separate
directory/greeting logs. Set `AI_LOCAL_AGENT_ENGLISH_CONTROL=1` for the English control prompts.
The test uses temporary data and never reads the real user's directory or changes its permissions.

## Observations

- The Russian dialogue completed with unverified descriptions instead of listing files.
- More explicit short instructions about tool evidence and function-call formatting did not
  establish a working directory chain.
- Increasing the schema share and removing inactive control tools from the bootstrap priorities
  also failed the Russian acceptance test. Those production changes were reverted.
- The English control did produce a real `mcp_app__tool_search` call with query `files`, limit 5.
  Its results included delete/edit/file-info/search capabilities; it did not reach the required
  directory listing and the final response denied the ability to inspect files.
- Thus the failures include both model tool-use reliability and discovery quality for vague
  queries. They are not solely a Russian-language or context-overflow problem.

All unsuccessful production prompt/selection experiments were reverted. Only the reproducible
opt-in evaluation and this report remain. No XML/JSON text-to-tool execution fallback, forced
tool call for every message, per-phrase routing, larger context window, or model replacement was
introduced. The directory live acceptance test is intentionally still red; it is a release gate.

Generated trial artifacts are under `artifacts/local-ai-trial/directory-dialog` and
`artifacts/local-ai-trial/directory-english`, not committed. A fix must pass this end-to-end gate
and repeated variants, not only prompt-size checks or direct synthetic function calls.
