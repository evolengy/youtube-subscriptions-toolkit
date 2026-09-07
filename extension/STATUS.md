# Status

Chrome-расширение (Manifest V3) для управления YouTube-подписками через официальный YouTube Data API v3. Вдохновлено функционалом PocketTube и "YouTube channel location", собрано под свои нужды.

## Готово и работает

- **Группы подписок** — создание/удаление групп и эмодзи-иконка (поле `icon`; дефолт-фоллбэк 📁 на рендере) в дашборде. Назначение каналов — отдельная страница [groups.html](groups.html) (кнопка «Edit groups», `chrome.tabs.create`): список каналов, поиск по имени, чипы-фильтры `All / Ungrouped / <группа> (N)`, у каждого канала чип на группу (клик = вкл/выкл членство). Живое обновление по `chrome.storage.onChanged`; scroll списка сохраняется при массовом редактировании. Логика — чистый [groupEditor.js](groupEditor.js) (`window.YSTGroupEditor`: `listGroups` / `channelRows` / `toggleMembership`), `node --test groupEditor.test.js` (6 тестов). Пикер — [emojiPicker.js](emojiPicker.js) (`window.YSTEmoji`) + подобранный набор ~390 эмодзи [emojiData.js](emojiData.js), поиск по имени/ключевым словам, «вставить свой» через Enter, стили [emojiPicker.css](emojiPicker.css) (общие для дашборда и content-скрипта); `firstEmoji` (первая графема через `Intl.Segmenter`) покрыт `node --test emojiPicker.test.js`. Иконку можно менять **и в дашборде, и в сайдбаре youtube.com** (клик по иконке группы → пикер); видна в списке групп, сайдбаре и заголовке оверлея. `chrome.storage.onChanged` синхронит обе стороны вживую.
- **Лента по группе** с фильтрами (тип Video/Short/Live, длительность `<4 / 4–20 / >20 мин`, дата загрузки `сегодня / неделя / месяц`, текстовый поиск по заголовку+каналу), сортировкой (дата/длительность/просмотры), отметка "просмотрено". Логика фильтров — общий чистый модуль [feedFilter.js](feedFilter.js) (`window.YSTFeed.applyFilters`), один на дашборд (ES-модуль) и оверлей (content-скрипт); есть `node --test feedFilter.test.js`.
- **Управление каналами** — отдельная страница [channels.html](channels.html) (открывается кнопкой «Manage channels» из дашборда, `chrome.tabs.create`): таблица Channel / Status / Last upload, сорт по клику на заголовок, чипы-фильтры по статусу с счётчиками, поиск, «Refresh now», живое обновление по `chrome.storage.onChanged`. Статус активности: Active `<30д` / Quiet `<180д` / Dormant / Dead / Unknown (по последней дате в `videosCache`); кружок с тултипом. Dead → кнопка Unsubscribe. В дашборде остались только маленькие точки статуса в списке «Assign channels». Логика — чистый модуль [channelHealth.js](channelHealth.js) (`window.YSTHealth`: `classifyChannel` / `buildChannelRows` / `countByStatus`), `node --test channelHealth.test.js` (13 тестов).
- **Бейдж локации канала** — страна канала рядом с названием на watch-странице. Логика в [content-groups.js](content-groups.js) (раньше был отдельный `content-location.js` — content-скрипт с match только `watch*` не инжектится на SPA-переходах). Channel id берётся из ссылки автора (`ytd-video-owner-renderer a[href]` → `@handle` или `/channel/UC…`); `channels.list` резолвит handle через `forHandle=`.
- **Встройка в сайдбар YouTube** — секция "My groups" в левом меню youtube.com, переключение группы открывает лёгкий оверлей с лентой поверх контента, не трогая ленту YouTube напрямую ([content-groups.js](content-groups.js)). Нода вставляется **между `#sections` и `#footer`** — прямым ребёнком `ytd-guide-renderer`, не внутрь `#sections` (им владеет Polymer и таскает/сносит чужие ноды — попытка держать секцию «под Подписками» приводила к драке за позицию и зависанию вкладки). Итог: секция внизу списка гайда, над футер-ссылками. Постоянный throttle-`MutationObserver` только ре-инжектит ноду, если гайд перестроился и её снесло; за позицию не воюет. Управление группами — в dashboard.html.
- **Авторизация** — OAuth через `chrome.identity.getAuthToken`, один и тот же токен (scope `https://www.googleapis.com/auth/youtube`) используется для всех вызовов API.
- **Периодическое обновление** — `chrome.alarms`, раз в 45 минут подтягивает новые видео по всем каналам.

## OAuth / настройка Google Cloud

- Проект Google Cloud: `298697484227` (общий с desktop-client, но **отдельный** OAuth-клиент).
- Клиент расширения — тип **"Chrome Extension"** (без секрета), `client_id`
  `REPLACE_WITH_YOUR_OAUTH_CLIENT_ID` в [manifest.json](manifest.json).
  `chrome.identity.getAuthToken` принимает только клиент этого типа и матчит его по паре
  *(client_id, ID расширения)*.
- **ID расширения выводится из пути установки** (в манифесте нет поля `"key"`). Переезд папки,
  клонирование репозитория в другое место или загрузка unpacked на другой машине → ID меняется →
  `getAuthToken` падает с `OAuth2 request failed: ... 'bad client id: ...'`.
- **Как чинить смену ID:** взять актуальный ID на `chrome://extensions` → в
  [Google Cloud Console → Credentials](https://console.cloud.google.com/apis/credentials) открыть
  OAuth-клиент → вписать этот ID в поле Item ID. Ничего в коде менять не нужно — привязка живёт
  в консоли, не в репозитории.
- Текущий рабочий ID расширения: `YOUR_EXTENSION_ID` (обновлён 2026-09-07).
- Consent screen в статусе "Testing" — аккаунт должен быть в Test users; **YouTube Data API v3**
  должен быть включён в проекте.
- Чтобы ID перестал зависеть от пути — добавить `"key"` в манифест (публичный ключ в base64) и
  один раз обновить Item ID в консоли под новый детерминированный ID.

## Совместимость с DOM YouTube (проверено 2026-09)

- `content-groups.css` держит **свою палитру** (`--yst-*`, переключается по `html[dark]`) — глобальные
  `--yt-spec-*` YouTube убрал в миграции на новые токены, `var(--yt-spec-*, fallback)` молча
  съезжал на фолбэк и ломал светлую тему. Метрики (шрифт Roboto, пункты гайда 40px/вес 500/радиус 10px,
  кнопки-чипы 36px/радиус 18px, синяя текст-кнопка) сняты с живого YouTube.
- `content-groups.js` строит DOM через `createElement`/`replaceChildren` — под Trusted-Types CSP
  YouTube (`require-trusted-types-for 'script'`) присваивание строки в `.innerHTML` кидает TypeError,
  даже пустой строки. Isolated world сейчас освобождён, но Chrome это освобождение убирает.
- Бейдж локации больше **не** читает `meta[itemprop="channelId"]` — этого тега на
  watch-странице нет (см. выше про ссылку автора).
- Структура гайда: `ytd-guide-renderer > #sections` (Polymer dom-repeat) + `ytd-guide-renderer > #footer`.
  Живы: `ytd-video-owner-renderer #channel-name`, события `yt-navigate-start/finish`.
- На watch-странице гайд-драуэр закрыт (transform off-screen), `#sections` иногда очищается
  полностью — секция «My groups» там висит вне экрана, появляется при открытии меню-гамбургера.

## Известные ограничения / на будущее

- **Mini-guide:** на узком окне YouTube показывает только `ytd-mini-guide-renderer`, полный
  `#sections` не наполняется — секция «My groups» и переключение групп в этом режиме недоступны.
- Уведомления по группам (были в списке функций PocketTube) — не реализованы.
- `manifest.json` содержит реальный `oauth2.client_id` (installed-app credential, не секрет) — см. раздел «OAuth / настройка Google Cloud».
- Иконки — простые сгенерированные плейсхолдеры ([icons/](icons)), не финальный дизайн.
