# Инструкции для агента

Применяются ко всему репозиторию. Общение и проектная документация — на русском; код, комментарии и сообщения коммитов — на английском. Для коммитов используется Conventional Commits.

## Ориентация

- `README.md` — обзор YouTube Subscriptions Toolkit: два независимых продукта, не клиент и сервер. Изменение одного не требует автоматически менять другой.
- `CLAUDE.md` — подробный справочник текущей архитектуры; прочитай соответствующий раздел перед изменениями и сверь его с кодом.
- `extension/` — Chrome Manifest V3, vanilla JavaScript, без сборки и установки npm-зависимостей. `extension/README.md` — настройка, `extension/STATUS.md` — функции и особенности DOM YouTube.
- `desktop-client/YouTubeDesktopClient/` — Windows-приложение .NET 10/WPF; `YouTubeDesktopClient.Tests/` — xUnit. Настройка описана в `desktop-client/README.md`.
- `docs/superpowers/` — исходные спецификация и план; реализация развилась дальше. При расхождении исследуй текущий код, не восстанавливай старый дизайн автоматически.

## Команды и проверка

Запускай из указанных каталогов:

- `extension/`: `node --test` (в CI Node 22); отдельный модуль: `node --test feedFilter.test.js`. Расширение загружается в Chrome как unpacked из `extension/`.
- `desktop-client/`: `dotnet build YouTubeDesktopClient.sln`; `dotnet test --configuration Release` (как в CI); `dotnet run --project YouTubeDesktopClient`.
- Для одного класса тестов: `dotnet test --filter "FullyQualifiedName~SubscriptionStoreTests"` из `desktop-client/`.
- Desktop требует Windows и .NET 10 SDK (`net10.0-windows`); WebView2 нужен для работы проигрывателя. Проверки CI находятся в `.github/workflows/`.
- После изменения логики запускай соответствующие тесты. Для UI отдельно проверяй затронутый сценарий в Chrome/WPF; unit-тесты не подтверждают работу DOM, OAuth и проигрывателя. В отчёте разделяй выполненные проверки и непроверенное.

## Chrome-расширение

- `background.js` — service worker, маршрутизация сообщений, синхронизация и уведомления; `youtubeApi.js` — API и авторизация; `storage.js` — схема и операции хранения.
- `dashboard.*` — лента и выбор группы; `groups.*` — редактирование групп; `channels.*`, `settings.*`, `logs.*` — отдельные страницы управления.
- Чистую логику держи в общих модулях (`feedFilter.js`, `groupCounts.js`, `groupEditor.js` и др.), а не дублируй между dashboard и `content-groups.js`.
- Соблюдай существующее сочетание ESM и обычных скриптов с `window.YST*`/CommonJS для тестов. При добавлении зависимости проверь порядок `<script>` в HTML и `content_scripts[].js` в `manifest.json`.
- `content-groups.js` работает на всех страницах YouTube и обрабатывает SPA-переходы. Сохраняй обработку `yt-navigate-*`, восстановление после перерисовки и очистку оверлея; DOM-ограничения описаны в `STATUS.md`.
- Пользовательские настройки хранятся в `chrome.storage.sync`, кэш и метаданные — в `local`; сохраняй существующие лимиты и синхронизацию UI через `chrome.storage.onChanged`.
- Все фоновые перезаписи кэша подписок/видео проводи через очередь `exclusive()`; не вызывай её вложенно — получится ожидание самой себя. Ошибка одного канала не должна уничтожать его предыдущий кэш или прерывать весь синк.
- Ошибки действий показывай через `toast.js`, диагностику пиши через `logger.js` в журнал `logs.html`; не превращай статус синхронизации в постоянное сообщение об ошибке.

## Windows-клиент

- Сохраняй разделение: `Api/` и узкие интерфейсы — API; `Auth/` — OAuth; `Storage/` — данные; `Sync/` — синхронизация; view model — поведение; XAML/`Views/` — отображение.
- Тесты используют рукописные fake-интерфейсы и `FakeHttpMessageHandler`, без mocking-библиотеки. Для хранения используй временные каталоги; при тестировании ошибок учитывай статический `NotificationCenter` и очищай его.
- `SubscriptionStore` разделяет пользовательские `settings.json` и восстанавливаемый `cache.json`. Сохраняй блокировку read-modify-write, атомарную замену файлов, значения по умолчанию новых полей и изоляцию аккаунтов.
- Фоновая синхронизация обновляет свежие видео, расширение ленты загружает историю. Не перематывай `UploadsNextPageToken` при обычном синке; сохраняй дедупликацию `Storage/VideoMerge.cs` и ограничения параллелизма/кэша.
- Исключения не должны выходить из timer callback; изменения WPF выполняй через Dispatcher. Пользовательские сообщения направляй в `Diagnostics/NotificationCenter.cs`, диагностику — в `Logging/Logger.cs`.
- Desktop использует OAuth `youtube.force-ssl`, расширение — `youtube`; не унифицируй scopes механически. Desktop: PKCE с loopback redirect и DPAPI для refresh token.
- Desktop не внедряет код в DOM YouTube. WebView2 отображает официальный IFrame player, watch-страницу и Home. Инициализируй WebView2 с общим environment до навигации и освобождай закрытые вкладки.
- «Watched» — локальная отметка; Embed не записывает историю YouTube. Сохраняй различие Embed/FullPage и fallback для видео, запрещающих встраивание.
- Цвета WPF задавай через семантические `DynamicResource` и согласованные словари `Themes/Light.xaml`/`Dark.xaml`; WinForms-меню трея оформляется отдельно. Не возвращай удалённые implicit usings WinForms/Drawing — они конфликтуют с WPF.

## Данные и границы изменений

- Перед работой проверь `git status --short`; сохраняй чужие незакоммиченные изменения, особенно локальную OAuth-настройку `extension/manifest.json`. Не меняй git index flags для сокрытия правок автоматически.
- Не добавляй credentials, токены, cookies или реальные данные аккаунта в код, тесты, документацию и логи. Для примеров используй placeholders и существующий `credentials.example.json`.
- Runtime desktop находится в `%AppData%\YouTubeSubscriptionsToolkit\`: `credentials.json`, `token.bin`, `account.json`, `log.txt`, `WebView2/`, `accounts/{channelId}/settings.json` и `cache.json`. Не сбрасывай и не мигрируй реальные данные ради проверки; используй временные данные тестов.
- Учитывай квоту YouTube API: сохраняй ETag, кэш, ограничение запросов и остановку расширения при исчерпании квоты; не запускай массовые записи в реальном аккаунте для тестирования.
- При изменении архитектуры обновляй соответствующую документацию; не включай в инструкции фиксированные количества тестов и другие быстро устаревающие результаты запусков.
