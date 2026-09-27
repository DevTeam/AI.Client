# Headless CLI

The CLI uses the server-side chat, Submit and run events, like the Web. A local session holds the chat ID, the project ID and the Host address.

    dotnet run --project src/AI.Cli -- session create --project "My project" --host http://localhost:52173/
    dotnet run --project src/AI.Cli -- session send --session <id> --message "Hello"
    dotnet run --project src/AI.Cli -- session show --session <id>
    dotnet run --project src/AI.Cli -- session delete --session <id>

create supports --connection <name>; otherwise the chat inherits the project connection. send supports --cancel-after-ms <ms> and stops the server-side run on cancellation. show reads the server history. delete removes the server-side chat and the local session.

AI_CLIENT_SESSION_DIRECTORY sets the sessions directory. To isolate tests, use a separate AI_CLIENT_DATA_DIRECTORY for the Host and a local test AI endpoint. The transcript serves diagnostics, not as a source of history. Old sessions are not supported.
