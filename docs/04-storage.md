# Storage

By default, data is located in `%LOCALAPPDATA%/AI`. `AI_CLIENT_DATA_DIRECTORY` sets a separate Host data directory, and the `--data-dir` command-line option overrides both (`AI.Host --help` lists all options).

Projects and chats use schema 2; runs use schema 4. Old formats are not supported and are not migrated. A new data directory is required; user files are not removed automatically.

The chat manifest contains `MessageIds` and branches. Messages are written separately to `<chatPath>.nodes/<messageId>.json`. Immutable nodes are written first, then the manifest is replaced atomically. A failure before the replacement leaves the previous history intact. Modifying an existing node is forbidden.

Repositories serialize revision checking and writing within a single Host. Multiple processes must not write to the same directory. Unused nodes are currently retained; a garbage collector is not implemented.

Connections and MCP settings are written to a single `settings.json`. Secrets are protected by the user account.

A command remains in the queue until the response is committed. Retries reuse the original message ID and a deterministic response ID. This prevents duplication of saved history but does not guarantee a single external HTTP request in case of failure.
