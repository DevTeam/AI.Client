# Headless CLI

CLI использует серверный чат, Submit и события выполнения, как Web. Локальная session содержит ID чата, ID проекта и адрес Host.

    dotnet run --project src/AI.Client.Cli -- session create --project "My project" --host http://localhost:52173/
    dotnet run --project src/AI.Client.Cli -- session send --session <id> --message "Hello"
    dotnet run --project src/AI.Client.Cli -- session show --session <id>
    dotnet run --project src/AI.Client.Cli -- session delete --session <id>

create поддерживает --connection <name>; иначе чат наследует подключение проекта. send поддерживает --cancel-after-ms <ms> и останавливает серверный запуск при отмене. show читает серверную историю. delete удаляет серверный чат и локальную session.

AI_CLIENT_SESSION_DIRECTORY задаёт каталог sessions. Для изоляции проверок используйте отдельный AI_CLIENT_DATA_DIRECTORY у Host и локальный тестовый AI endpoint. Transcript служит диагностике, а не источником истории. Старые sessions не поддерживаются.
