# Status

Chrome-расширение (Manifest V3) для управления YouTube-подписками через официальный YouTube Data API v3. Вдохновлено функционалом PocketTube и "YouTube channel location", собрано под свои нужды.

## Готово и работает

- **Группы подписок** — создание/удаление групп, назначение каналов в группу ([dashboard.html](dashboard.html), [dashboard.js](dashboard.js)).
- **Лента по группе** с фильтрами (Video/Short/Live) и сортировкой (дата/длительность/просмотры), отметка "просмотрено".
- **Управление каналами** — обнаружение "мёртвых" (удалённых/недоступных) каналов, массовая отписка.
- **Бейдж локации канала** — страна канала показывается рядом с названием на странице просмотра ([content-location.js](content-location.js)).
- **Встройка в сайдбар YouTube** — секция "My groups" прямо в левом меню youtube.com, переключение группы открывает лёгкий оверлей с лентой поверх контента, не трогая ленту YouTube напрямую ([content-groups.js](content-groups.js)). Управление группами (создание/переименование/назначение) осталось в dashboard.html.
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

## Известные ограничения / на будущее

- Уведомления по группам (были в списке функций PocketTube) — не реализованы.
- `manifest.json` содержит реальный `oauth2.client_id` (installed-app credential, не секрет) — см. раздел «OAuth / настройка Google Cloud».
- Иконки — простые сгенерированные плейсхолдеры ([icons/](icons)), не финальный дизайн.
