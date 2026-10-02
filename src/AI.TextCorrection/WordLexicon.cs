namespace AI.TextCorrection;

/// <summary>Conservative vocabulary. Unknown words are left for manual editing.</summary>
public sealed class WordLexicon : IWordLexicon
{
    private readonly Dictionary<string, HashSet<string>> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _vocabulary = new(StringComparer.Ordinal)
    {
        ["en"] = "hello hi how are you doing please help thanks thank the a an this that these those is it in on at to of for with from and or not yes no can could would should have has had be been do does did make change fix add remove update create project file files folder code test tests build run error errors warning warnings message chat text input output language keyboard layout settings interface service public private class method async await return string int bool var static dotnet git npm node blazor windows linux macos java javascript typescript python rust api http https json css html sql ui di id pr db ok read write open close save send cancel undo good morning evening day world work works working need want what when where why which all more some only now then use using check review implement implementation support automatic correction detect probability different words phrase phrases english russian ukrainian local server client browser bug feature button cursor selection selected model models performance memory context stream token tokens tool tools function functions result results null true false version system application",
        ["ru"] = "коды кино привет здравствуйте добрый день вечер утро как дела спасибо пожалуйста помоги помогите нужно нужен нужна нужны хочу можно сделай делать добавить добавь удалить удали изменить измени исправить исправь исправление ошибка ошибки предупреждение предупреждения проект проекта проекты файл файла файлы папка папки код тест тесты тестов сборка собрать запустить запуск работает работа работать сообщение сообщения чат чата текст текста ввод вывода вывод язык языка языки раскладка раскладки клавиатура настройки настройка интерфейс сервис метод методы класс классы функция функции результат результаты проверить проверь проверка реализуй реализация поддержка автоматически автоматическое автоматический вероятность разные слова слово фраза фразы русский английский украинский локально сервер клиент браузер кнопка курсор выделение модель модели память контекст инструмент инструменты сегодня сейчас потом сначала ещё только очень хорошо плохо да нет не и или что это эта этот эти для на в с по из от к у а но если то чтобы когда где почему какой какие сколько есть был была были будет будут быть всё все мне меня мой моя мы вы ты он она они его её наш ваш другой другие новый новая новые старый большое маленький первый второй без при после перед между также здесь там давай давайте покажи объясни напиши ответ вопрос вопросы решение решения отдельный отдельном библиотека библиотеки подключи подключить встроить встрой выбор отмена отменить сохранение сохранить отправить отправка скорость быстро медленно начало конец строка строки символ символы изменить изменения проверить проверки готово готовый решения использование использовать нужно учитывать языков высокая перепутан",
    };

    public bool Contains(string layoutId, string word) =>
        _words.TryGetValue(layoutId, out var words) && words.Contains(word);

    public WordLexicon()
    {
        foreach (var (id, vocabulary) in _vocabulary)
            _words[id] = new HashSet<string>(vocabulary.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
    }
}
