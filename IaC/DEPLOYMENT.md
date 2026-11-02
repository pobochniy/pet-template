# 🚀 Руководство по деплою проекта PetTemplate

Как проект из шаблона PetTemplate встаёт на виртуальную машину с нуля: что настроить на сервере
и в GitHub. Это документация шаблона, а не план выкатки самого шаблона — своего прода у него
нет. Устройство и управление — [README.md](README.md), требования — `specs/infrastructure.md`
(разделы 6–11) и `specs/admin.md` (разделы 3.3 и 7).

Домен `example.com` — символ шаблона `domain`: `dotnet new pet --domain <домен>` заменяет его
во всём проекте — `IaC/`, workflow, `appsettings*.json`, клиент, письма (`specs/infrastructure.md`, 13).

## Быстрый старт

### 1. Подготовка виртуальной машины

```bash
# Скопируйте папку IaC на VM
scp -r IaC user@your-vm:/home/user/

# Подключитесь к VM
ssh user@your-vm

# Запустите скрипт подготовки
cd ~/IaC
chmod +x setup-vm.sh
./setup-vm.sh
```

### 2. Настройка переменных окружения

```bash
# Отредактируйте .env
nano ~/IaC/.env

# Установите безопасные пароли:
# - POSTGRES_PASSWORD   — суперпользователь postgres
# - APP_DB_PASSWORD     — пользователь приложения (раздел 9)
# - POSTGRES_EXPORTER_PASSWORD — пользователь postgres-exporter (раздел 11, «Метрики PostgreSQL, Qdrant и машины»)
openssl rand -hex 24   # пароль без символов, которые ломают .env и строку подключения
```

Пароли и имена читаются только при первом старте Postgres на пустом томе `db-data`.
Переезжаете со старого тома — в `.env` те же пароли, с которыми создавалась база.
Символ `$` в `.env` compose считает подстановкой переменной — в паролях его не используйте.

### 3. Настройка Nginx

Домен — в `nginx/conf/nginx.conf`: `example.com` в `server_name` и путях сертификата
`live/example.com/`, `adminka.example.com` у админки. Адрес API `dapi:8080` уже на месте.
Что в нём важно:

- `location ~* ^/api/admin(/|$) { deny all; }` на основном домене и замок
  `include admin-allow*.inc; deny all;` на `adminka.example.com` — раздел 8;
- `grafana.example.com` — тот же замок и `auth_request` на `Admin/Auth/Verify` — раздел 11;
- `index.html` и маршруты клиента — `Cache-Control: no-cache`, бандлы с хешем в имени —
  `immutable` на год;
- `client_max_body_size 5m` — без него nginx режет тело на 1 МБ раньше API; самое большое
  тело — запись голоса ассистенту (до 4 МБ, `AssistantOptions:Transcription:MaxAudioBytes`);
- нет `X-Frame-Options` и CSP — клиент открывается во фрейме mini-app VK и Telegram
  (комментарий в `nginx.conf`).

Проверка синтаксиса без сервера (с машины разработчика, из корня репозитория):

```bash
docker run --rm -v $PWD/IaC/nginx/conf:/etc/nginx/conf.d:ro nginx nginx -t
```

Без контейнера `dapi` и без сертификата она упадёт на `host not found in upstream "dapi"`
и `cannot load certificate` — это ожидаемо. Grafana на старте nginx не нужна: её имя
разрешается на запросе (`resolver` в `nginx.conf`). Полная проверка — с заглушками:
`--add-host dapi:127.0.0.1` и самоподписанным сертификатом, смонтированным в
`/etc/nginx/ssl/live/example.com/` (`openssl req -x509 -newkey rsa:2048 -nodes -subj /CN=example.com
-keyout privkey.pem -out fullchain.pem`).

### 4. Запуск инфраструктуры и сертификат

```bash
cd ~/IaC
docker compose up -d
```

nginx при старте разрешает `dapi` из `proxy_pass` и читает сертификат. Поэтому на чистой
машине порядок такой:

1. `docker compose up -d database qdrant embeddings certbot`, дождаться `healthy` у `database`
   (`docker compose ps`) — на пустом томе в этот момент заводятся пользователь приложения
   (раздел 9, в логе — `01-app-user.sh: роль … — владелец базы …`) и пользователь
   экспортёра метрик (раздел 11, `02-exporter-user.sh: роль … с pg_monitor`).
   `qdrantcontainer` обязателен до первого `Publish Api`: без Qdrant API не стартует
   (`specs/infrastructure.md`, 6). Эмбеддер — нет, но веса он качает при первом старте
   (ниже, «Модель эмбеддера»);
2. `Publish Api` (раздел 7) — контейнер `dapi` появляется в сети `nginx-proxy-man`;
3. блоки `listen 443` в `nginx.conf` временно закомментировать, `docker compose up -d webserver`;
4. сертификат:
   ```bash
   docker compose run --rm certbot certonly --webroot --webroot-path /var/www/certbot/ \
     -d example.com -d www.example.com -d adminka.example.com -d grafana.example.com
   ```
5. вернуть блоки 443 и `docker compose exec webserver nginx -t && docker compose exec webserver nginx -s reload`;
6. остальное — телеметрия (раздел 11): `docker compose up -d`.

Продление — `crontab` из `README.md`, раздел 4.

#### Модель эмбеддера

`embeddingscontainer` (llama.cpp, `specs/search.md`, 3) сам качает веса
`multilingual-e5-small` в Q8 (126 МБ) с Hugging Face при первом старте — в том
`embeddings-cache` (`docker volume ls` → `iac_embeddings-cache`), смонтированный на весь
`/root/.cache`: нынешние сборки llama.cpp кладут веса в `huggingface/`, прежние — в
`llama.cpp/`. Дальше он стартует с тома, без сети. В репозиторий модель не кладём.
Готов, когда в логе `llama_server: listening`:

```bash
docker logs -f embeddingscontainer 2>&1 | grep -m1 listening
# Проверка изнутри сети — вектор из 384 чисел
docker run --rm --network nginx-proxy-man curlimages/curl -s http://embeddingscontainer:8080/v1/embeddings \
  -H 'Content-Type: application/json' -d '{"input":["query: проверка"]}' | head -c 200
```

**С сервера нет доступа к Hugging Face** — скачать файл на своей машине и положить в том
по тому же пути, по которому его ищет сервер (`--hf-repo`/`--hf-file` в `docker-compose.yml`),
либо поменять запуск на `-m` с путём к файлу:

```bash
# у себя
curl -L -o multilingual-e5-small-q8_0.gguf \
  https://huggingface.co/TwinSunsLLC/multilingual-e5-small-gguf/resolve/main/multilingual-e5-small-q8_0.gguf
scp multilingual-e5-small-q8_0.gguf user@your-vm:~/
# на сервере: файл — в том, запуск — с -m вместо --hf-repo/--hf-file
docker run --rm -v iac_embeddings-cache:/cache -v ~/:/src alpine \
  sh -c 'mkdir -p /cache/models && cp /src/multilingual-e5-small-q8_0.gguf /cache/models/'
# command: -m /root/.cache/models/multilingual-e5-small-q8_0.gguf --embeddings --pooling mean --host 0.0.0.0 --port 8080
```

Другая модель — `EMBEDDINGS_REPO` и `EMBEDDINGS_FILE` в `.env` вместе с `SearchOptions`
(размерность, имя коллекции, префиксы, порог) и полным реиндексом из админки
(`tech-debt/embeddings-model.md`, «Что придётся сделать при переходе»).

### 5. Настройка GitHub Actions Runner

Settings → Actions → Runners → New self-hosted runner — там же актуальная версия и токен.
Label не нужен: workflow шаблона ищут раннер по `runs-on: self-hosted`.

```bash
mkdir ~/actions-runner && cd ~/actions-runner
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz
./config.sh --url https://github.com/<owner>/<repo> --token YOUR_TOKEN
sudo ./svc.sh install
sudo ./svc.sh start
```

Пользователь раннера — тот же, под которым лежит `~/IaC`, и в группе `docker`: workflow
зовут `docker` без `sudo` и пишут в `~/frontend`, `~/adminka`, `~/IaC/nginx/conf`,
`~/db-backups`.

### 6. Секреты GitHub Actions

Settings → Secrets and variables → Actions (`specs/infrastructure.md`, раздел 9). Workflow
`Publish Api` и `Rollback Api` пробрасывают их в контейнер как переменные окружения (набор
один, новый секрет — строкой `--env` в обоих) — всё остальное берётся из `appsettings.json`
и `appsettings.Release.json` (адреса соседних контейнеров — не секреты).

| Секрет | Переменная в контейнере | Значение |
|---|---|---|
| `API_DB_CONNECTION_STRING` | `ConnectionStrings__AppConnection` | `Host=dbcontainer;Port=5432;Database=pettemplatedb;Username=pettemplate_app;Password=…` — пользователь и пароль из `APP_DB_USER`/`APP_DB_PASSWORD` в `.env`, **не** `postgres` |
| `JWT_SIGNING_KEY` | `JwtOptions__SigningKey` | `openssl rand -base64 48`; короче 32 байт API не стартует |
| `ADMIN_JWT_SIGNING_KEY` | `AdminOptions__SigningKey` | Ключ админской схемы — **другой**, чем `JWT_SIGNING_KEY`: совпадение API на старте не пропускает (`specs/admin.md`, 3.3) |
| `TELEGRAM_BOT_TOKEN` | `Telegram__BotToken` | Токен бота — им подписан `initData` mini-app Telegram (`specs/accounts.md`, 5.6). Входу на сайте токен не нужен. Пусто — вход из mini-app Telegram закрыт |
| `VK_MINIAPP_PRIVATE_KEY` | `VkOptions__PrivateKey` | «Защищённый ключ» mini-app VK — им VK подписывает параметры запуска (5.6). Пусто — вход из mini-app VK закрыт |
| `SMTP_PASSWORD` | `MailOptions__Password` | Пароль SMTP-ящика `info@` у провайдера (`specs/mail.md`, 5). Провайдер без авторизации — секрет пустой. Остальные поля почты (`Host`, `Port`, `UserName`, `From`, `PublicBaseUrl`) — в `appsettings.Release.json`; пустой `Host` — почта выключена, письма только пишутся в лог |
| `ASSISTANT_API_KEY` | `AssistantOptions__Providers__main__ApiKey` | Ключ OpenAI-совместимого поставщика модели ассистента (`specs/assistant.md`, 16 и 17). Пусто — запрос уходит без ключа, и поставщик, которому ключ нужен, ответит отказом: ход кончится строкой «модель не отозвалась» |
| `NUGET_GITHUB_TOKEN` | — (restore) | Токен GitHub с `read:packages` для фида `pobhub` |

Ключи остальных внешних сервисов — тоже секретами, по той же схеме `Секция__Ключ`.

Телеметрии секреты не нужны: `OTEL_EXPORTER_OTLP_ENDPOINT` (`http://otelcollectorcontainer:4317`)
и `OTEL_SERVICE_NAME` (`pettemplate-api`) прописаны в `Publish Api` и `Rollback Api` прямо
(раздел 11).

Публичные id площадок — не секреты, а **переменные** (Variables) того же раздела; пустая —
кнопки площадки нет:

| Переменная | В контейнере | Значение |
|---|---|---|
| `TELEGRAM_BOT_ID` | `TelegramLoginOptions__BotId` | Id бота — начало его токена до двоеточия (`123456:…` → `123456`) |
| `VK_ID_APP_ID` | `VkIdOptions__AppId` | Id приложения «Веб» в кабинете VK ID |
| `ASSISTANT_ENDPOINT` | `AssistantOptions__Providers__main__Endpoint` | Адрес OpenAI-совместимого API модели ассистента вместе с версией пути: `https://api.openai.com/v1`, `https://openrouter.ai/api/v1`, своя карта — `http://<хост>:<порт>/v1`. Пусто — ассистент отвечает «модель не отозвалась» |
| `ASSISTANT_MODEL` | `AssistantOptions__SeedModels__0__Key` | Идентификатор модели у поставщика — стартовая строка справочника моделей (`specs/assistant.md`, 16.2). Заводится один раз, в **пустую** таблицу `ai_models` на старте API; дальше моделями владеет админка (раздел «Модели»), и смена переменной справочник не перепишет. Пусто — строки нет, и ассистент недоступен |
| `ASSISTANT_TRANSCRIPTION_MODEL` | `AssistantOptions__Transcription__Model` | Модель распознавания речи у того же поставщика (`ASSISTANT_ENDPOINT`, `ASSISTANT_API_KEY`) — его `POST /audio/transcriptions` (`specs/assistant.md`, 6.2): `whisper-1`, `whisper-large-v3`. Пусто — голоса нет: кнопка микрофона в чате отвечает «распознавание речи не настроено». Цена минуты — `AssistantOptions:Transcription:CostPerMinute` в `appsettings.Release.json` (по умолчанию ноль) |

**Ассистент** (`specs/assistant.md`, 16–17). Поставщик — любой с OpenAI-совместимым
`/chat/completions` и вызовом инструментов: адрес — переменной `ASSISTANT_ENDPOINT`, ключ —
секретом `ASSISTANT_API_KEY`, модель — `ASSISTANT_MODEL`, модель расшифровки голоса —
`ASSISTANT_TRANSCRIPTION_MODEL` (тот же адрес и ключ: своего секрета у голоса нет).
Запись голоса — до 4 МБ (`AssistantOptions:Transcription:MaxAudioBytes`), и `client_max_body_size`
в `IaC/nginx/conf/nginx.conf` (5m) должен быть больше неё. Код поставщика в шаблоне один —
`main`; второй поставщик (запасная модель другого сервиса) — ещё одна тройка `--env`
`AssistantOptions__Providers__<код>__…` в обоих workflow и строка справочника с этим кодом.
Провайдер-специфичные поля запроса (folder id и т. п.) — `AssistantOptions__Providers__<код>__ProviderOptions__<поле>`.
Ходы ассистента идут своей дорожкой очереди команд в `AssistantOptions:Workers` потоков
(по умолчанию 2) и не ждут за письмами и реиндексом поиска.

**Расход и лимиты ассистента** (`specs/assistant.md`, 13.5–13.6). Баланса нет — расход
только учитывается по ценам справочника моделей (`ai_models.cost_in/cost_cached/cost_out`,
за 1000 токенов, в валюте `SpendOptions:Currency`; стартовой строке — `AssistantOptions:SeedModels:0:CostIn…`).
Цены по умолчанию нулевые: токены считаются и лимиты по токенам работают, деньги — нет.
Лимит пространства по умолчанию — `SpendOptions:DefaultLimit` в `appsettings.json`
(в шаблоне — 2 млн токенов в сутки и 30 млн в месяц на пространство), своё значение
для пространства и платежи поставщику — в админке, раздел «Расход» (роль `Billing`).
Тревога «Остаток у поставщика модели ниже порога» молчит, пока там не записан платёж;
порог — `SpendOptions:ProviderBalanceAlarm`. Дашборд — «Ассистент».

**Вход через Telegram и VK** (`specs/accounts.md`, 5.1–5.6). Адреса возврата — в
`appsettings.json` (`TelegramLoginOptions:RedirectUri`, `VkIdOptions:RedirectUri`), на домене
проекта: `https://<домен>/auth/telegram` и `https://<домен>/auth/vk`.

- **Telegram, сайт.** Бот — у @BotFather (`/newbot`); там же `/setdomain` — домен сайта
  (Login Widget, Allowed URLs) и адрес возврата `/auth/telegram`. Сервер в Telegram не ходит:
  открытые ключи подписи — копией в `TelegramLoginOptions:Keys` (из
  `https://oauth.telegram.org/.well-known/jwks.json`). Telegram сменил ключи — вход падает,
  в логе API «unknown key»: обновить копию и выкатить API.
- **Telegram, mini-app.** У того же бота в @BotFather — Mini App (`/newapp`) с адресом
  `https://<домен>/miniapp`. Ссылка-приглашение в mini-app — `t.me/<бот>?startapp=inv_<токен без дефисов>`.
- **VK ID, сайт.** Кабинет VK ID (`id.vk.ru`) → приложение типа «Веб»: id → `VK_ID_APP_ID`,
  доверенный адрес возврата — `https://<домен>/auth/vk`. Секрета у входа нет — PKCE.
- **VK, mini-app.** `vk.com/apps?act=manage` → приложение «VK Mini Apps» с адресом
  `https://<домен>/vk`; «Защищённый ключ» → `VK_MINIAPP_PRIVATE_KEY`. Приглашение —
  `vk.com/app<id>#inv_<токен без дефисов>`.

Пароль суперпользователя Postgres секретом не нужен: `Backup Db` зовёт `pg_dump` через
`docker exec` по сокету внутри контейнера, а там вход доверенный (раздел 10).

`JWT_SIGNING_KEY` — настоящий секрет: этим ключом подписываются токены, и тот, кто его
знает, выпишет себе токен от имени любого пользователя. Значение из `appsettings.json`
лежит в git и известно всем, у кого есть доступ к репозиторию (а у шаблона — всем, кто его
видел), — на сервере ключ обязан быть свой. Смена ключа разлогинивает всех: выданные токены
перестают проходить проверку.

**Почта** (`specs/mail.md`, 5–7). Письма — подтверждение адреса, сброс пароля, «почту
сменили»; без SMTP они только пишутся в лог API, и восстановление по почте не работает.

1. Выбрать провайдера с DKIM (хостинг домена, Яндекс 360, Mail.ru для бизнеса, Mailgun),
   завести ящик `info@<домен>`.
2. Проверить с сервера, какой порт открыт: `nc -vz <smtp-хост> 465` (и 587, 25) — 25 с VDS
   часто закрыт.
3. В `src/PetTemplate.Api/appsettings.Release.json`, секция `MailOptions`: `Host`, `Port`,
   `UseSsl` (465 — `true`, 587 — `false`, STARTTLS), `UserName`, `From`, `FromName`,
   `PublicBaseUrl` — адрес сайта для ссылок в письмах. Пароль — секретом `SMTP_PASSWORD`.
4. DNS-зона домена: MX провайдера, SPF (`v=spf1 include:<провайдер> ~all`), DKIM
   (`<selector>._domainkey`), DMARC (`v=DMARC1; p=none; rua=mailto:info@<домен>`).
5. Проверка: вписать свою почту в профиле — письмо во «Входящих», в заголовках SPF, DKIM,
   DMARC — `pass`.

Очередь писем пока живёт в процессе API поверх таблицы `local_queue` (`tech-debt/localqueue-postgres.md`):
письма, не ушедшие до перезапуска, уходят после него — строки остаются в базе.

### 7. Деплой через GitHub Actions

Выкатка — только руками, push в `main` ничего не выкатывает.

- **API**: Actions → Publish Api → Run workflow. Контейнер `dapi` публикует порт только
  на `127.0.0.1:5000` — снаружи API достижим лишь через nginx
- **Откат API**: Actions → Rollback Api (`previous`, `latest` или тег по дате);
  версии — Actions → List Api Versions
- **Frontend**: Actions → Publish Angular → Run workflow
- **Админка**: Actions → Publish Admin → Run workflow
- **Бэкап базы**: Actions → Backup Db → Run workflow — дамп в `~/db-backups` на VM;
  перед выкаткой с миграцией, которая что-то удаляет, — обязательно (раздел 10)
- **Открыть админку**: Actions → Admin Access → Run workflow (раздел 8)

Миграции базы накатывает сам API при старте — отдельного шага нет; поэтому у пользователя
приложения есть права на схему (раздел 9).

### 8. Админка

`adminka.example.com` — отдельная сборка (`ng build admin`) с того же сервера и тот же API
(`specs/admin.md`, разделы 2 и 7). Замков два, и оба на nginx: `allow`/`deny` на весь
server-блок админки и `deny` на `/api/admin*` основного домена — API один, и без второго
замка `POST example.com/api/Admin/Login` шёл бы мимо первого. Почти всё время админка закрыта
для всех и открывается на время workflow `Admin Access`.

1. **DNS**: A-запись `adminka.example.com` на тот же адрес, что у `example.com`.
2. **Сертификат** — вторым доменом в существующий, путь `live/example.com/` не меняется:
   ```bash
   docker compose run --rm certbot certonly --webroot --webroot-path /var/www/certbot/ \
     --expand -d example.com -d www.example.com -d adminka.example.com
   docker compose exec webserver nginx -s reload
   ```
3. **Открыть на время** — Actions → **Admin Access** → Run workflow: `open` и срок
   (1, 2, 4 или 8 часов). Раннер на сервере пишет рядом с `nginx.conf` файл
   `admin-allow-open.inc` с `allow all;` и перезагружает nginx — админка открыта всем,
   без списка адресов: админы ходят через меняющиеся VPN (`tech-debt/admin-perimeter.md`).
   Закончили — Run workflow с `close`, файл стёрт. Забыли — запуск по расписанию (раз
   в 15 минут) закроет сам, когда выйдет срок; GitHub запускает расписание с опозданием,
   так что «не раньше чем». Повторный `open` продлевает срок. Запустить workflow может
   любой с правом записи в репозиторий. Закрытым считается то, что nginx отвечает `403`
   на `https://adminka.example.com/` с самого сервера, а не отсутствие файла: если reload при
   закрытии не прошёл, запуск по расписанию увидит это по ответу и повторит.

   Раннеру для этого нужно то же, что остальным workflow на VM: `docker` без `sudo`
   и запись в каталог, смонтированный в `/etc/nginx/conf.d` (`~/IaC/nginx/conf`, если
   раннер и `IaC` под одним пользователем, как в разделах 1 и 5). Каталог не его —
   `chown` на пользователя раннера; `sudo` в workflow не добавлять. Проверка — от имени
   раннера на сервере:
   ```bash
   docker ps -q --filter label=com.docker.compose.service=webserver   # один id, без sudo
   touch ~/IaC/nginx/conf/.probe && rm ~/IaC/nginx/conf/.probe        # без ошибки
   command -v flock curl                                              # оба на месте
   ```

   Постоянный список адресов, если понадобится, — отдельным `admin-allow.inc` (образец
   `admin-allow.inc.example`, на MVP не заводится); workflow его не трогает. Нет ни
   того, ни другого файла — админка закрыта, основной сайт при этом работает.
4. **Секрет** `ADMIN_JWT_SIGNING_KEY` (раздел 6), затем Publish Api и Publish Admin.
   Сборка (`dist/admin/browser`) ложится в `~/adminka` (прежняя — в `~/adminka-backup`),
   nginx видит её томом `./adminka` — симлинком `IaC/adminka` → `~/adminka` из `setup-vm.sh`.
   `Publish Admin` ждёт те же ворота `web-tests.yml`, что и `Publish Angular` (там и спеки
   админки): красные словари или спеки продукта останавливают и выкатку админки. Бюджет
   сборки — свой, ошибка на 600 КБ.
5. **Первый админ** — строками в базе, по строке на роль; кода, который выдал бы их сам,
   нет намеренно (`specs/admin.md`, 3.1). Номера ролей — `AdminRole` (`specs/admin.md`, 3.2):
   1 `AdminManagement`, 2 `Accounts`, 4 `Billing` (запись в «Расход»), 8 `Models` (правка
   справочника моделей и сверка), 16 `Dialogs` (чужая переписка с ассистентом — выдавать
   осознанно, спец. admin, 3.2). Идентификатор — свой `users.id` (`uuid`, строкой). Имена
   в базе — snake_case, без кавычек:
   ```sql
   insert into user_in_admin_roles (user_id, role, granted_utc, granted_by_user_id)
   select id, role, now(), null from users, (values (1), (2), (4), (8), (16)) as roles(role)
   where id = '<ваш users.id>';
   ```
   ```bash
   docker exec -it dbcontainer psql -U postgres -d pettemplatedb
   ```
   Следующих админов — приглашениями из раздела «Админы» (`specs/admin.md`, 3.4).

**Проверка замков** — откуда угодно. Первая строка — при закрытой админке; открытая
отвечает `200`:

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://adminka.example.com/                   # 403
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://example.com/api/Admin/Login     # 403
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://example.com/api/admin/login     # 403
curl -s -m 5 -o /dev/null -w '%{http_code}\n' http://<адрес сервера>:5000/api/Admin/Login  # 000
```

Последняя строка — что порт API снаружи закрыт: контейнер `dapi` публикует его только
на `127.0.0.1`. Открытый наружу порт обходит оба замка сразу.

### 9. Пользователь приложения

API ходит в базу не суперпользователем `postgres`, а своей ролью `APP_DB_USER`
(`pettemplate_app`) — аналог `user_write` в Lad/ASU (`specs/infrastructure.md`, 8 и 13).

**Как заводится.** Скрипт `postgres/init/01-app-user.sh` смонтирован в
`/docker-entrypoint-initdb.d`: образ postgres запускает его сам один раз — при первом старте
на пустом томе `db-data`, сразу после создания базы `POSTGRES_DB`. Скрипт создаёт роль
с паролем `APP_DB_PASSWORD` и делает её **владельцем базы**.

**Какие права и почему.** В ASU `user_write` — это `MYSQL_USER` образа mysql: `ALL PRIVILEGES`
на свою базу и ничего за её пределами. Postgres-аналог того же — владелец базы без атрибутов
суперпользователя:

- может: всё со своей схемой `public` (в Postgres 15+ она принадлежит `pg_database_owner`,
  то есть владельцу базы) — таблицы, индексы, `ALTER` и `DROP` в миграциях; `create extension
  citext` (расширение доверенное, суперпользователь не нужен);
- не может: `SUPERUSER`, `CREATEDB`, `CREATEROLE`, `REPLICATION`, `BYPASSRLS` — не создаст
  базу или роль, не включит недоверенное расширение, не прочтёт файлы сервера
  (`pg_read_file`, `COPY … TO PROGRAM`), не тронет чужие базы.

Только DML (`SELECT/INSERT/UPDATE/DELETE`) роли API дать нельзя, пока миграции накатывает сам
API при старте (`Program.cs`, `MigrateAsync`): тогда ему нужны и права на DDL. Разделение на
владельца схемы для миграций и роль только с DML и `default privileges` —
`tech-debt/db-app-role.md`.

**Проверить**, что роль есть и пускает по паролю — тем же путём, что API, из другого
контейнера сети:

```bash
read -rsp 'APP_DB_PASSWORD: ' PGPASSWORD; export PGPASSWORD; echo
docker run --rm --network nginx-proxy-man -e PGPASSWORD postgres:17 \
  psql "host=dbcontainer dbname=pettemplatedb user=pettemplate_app" \
  -c "select current_user, current_database();"
unset PGPASSWORD
```

**Сменить пароль** (или завести роль в базе, созданной без скрипта): поправить
`APP_DB_PASSWORD` в `.env`, пересоздать контейнер, чтобы он увидел новое значение, и запустить
скрипт руками — он идемпотентный, недостающее создаст, пароль выставит заново:

```bash
docker compose up -d database
docker compose exec database /docker-entrypoint-initdb.d/01-app-user.sh
```

Потом — новый пароль в секрет `API_DB_CONNECTION_STRING` и `Rollback Api` с `latest`
(или `Publish Api`): переменные окружения контейнер `dapi` получает только при создании.

### 10. Бэкап и восстановление базы

**Формат — custom (`pg_dump -Fc`).** Почему не plain SQL + `gzip`:

- сжат сам, без конвейера `| gzip` — у которого код возврата `pg_dump` теряется без `pipefail`;
- проверяется целиком: `pg_restore -f /dev/null` прочитывает весь архив и падает на обрезанном
  (аналог строки `Dump completed` у `mysqldump`, которой у `pg_dump` нет);
- `pg_restore` умеет восстановить выборочно (`-t`, `-n`, `-l`/`-L`), с `--clean --if-exists`
  и параллельно (`-j`, с файла);
- нужен SQL глазами — `pg_restore -f - backup.dump | less`.

Дамп — тем `pg_dump`, что в контейнере (той же версии, что сервер), суперпользователем по
сокету: вход доверенный, пароль не нужен и в аргументы не попадает. Владельцы объектов
(`pettemplate_app`) в дампе сохраняются. Это же делает workflow `Backup Db` (дампы в
`~/db-backups`, последние `keep`). Вручную:

```bash
f=~/db-backups/pettemplatedb_$(date +%Y%m%d_%H%M%S).dump
docker exec dbcontainer pg_dump -U postgres -d pettemplatedb -Fc > "$f" \
  && docker exec -i dbcontainer pg_restore -f /dev/null < "$f" && echo "OK $f"
```

Скачать — `scp user@your-vm:~/db-backups/<файл>.dump .`

**Восстановление** — в чистую базу, API остановлен (иначе он держит соединения и тут же
пишет):

```bash
docker stop dapi
docker exec dbcontainer psql -U postgres -v ON_ERROR_STOP=1 \
  -c "drop database pettemplatedb with (force)" \
  -c "create database pettemplatedb owner pettemplate_app"
docker exec -i dbcontainer pg_restore -U postgres -d pettemplatedb \
  --exit-on-error --single-transaction < ~/db-backups/<файл>.dump
docker start dapi
```

- Роль `pettemplate_app` должна существовать до `pg_restore` — объекты в дампе принадлежат ей.
  На новом сервере её заводит `01-app-user.sh` при первом `docker compose up -d database`
  (`APP_DB_USER` в `.env` — то же имя, что было).
- Имя роли другое — восстанавливать с `--no-owner --role=<новая роль>`: объекты станут её.
  Просто `--no-owner` без `--role` отдаст их `postgres`, и миграции API упадут на
  `permission denied` / `must be owner of table`.
- `--single-transaction` — либо дамп встал целиком, либо база осталась пустой.

**Qdrant не бэкапится.** Векторный индекс целиком выводится из базы: точки — это чанки
текстов сущностей, посчитанные эмбеддером. Пропал том `qdrant-data`, переехали на новый
сервер или восстановили базу из дампа — API на старте заведёт пустую коллекцию
(`QdrantInitializer`), а заполнит её **полный реиндекс**: админка → «Сводка» → «Поиск» →
«Переиндексировать» (`specs/search.md`, 5). До его конца поиск по смыслу находит меньше,
по словам — всё. Снапшоты Qdrant не нужны: после восстановления базы они бы разошлись с ней.

### 11. Шлюз к инструментам (Grafana)

`grafana.example.com` своих учёток не держит: вход — тот же, что в админку
(`specs/admin.md`, 3.3). Вход в админку ставит `HttpOnly`-куку `pettemplate_admin` на
`.example.com` (домен — `AdminOptions:GatewayCookieDomain` в `appsettings.Release.json`),
nginx на каждый запрос спрашивает `GET /api/Admin/Auth/Verify`, пускать ли, кеширует
ответ на минуту и ставит Grafana заголовки `X-WEBAUTH-USER` (идентификатор пользователя)
и `X-WEBAUTH-ROLE` (`Admin` у кого есть `AdminManagement`, иначе `Viewer`). Не вошёл или
кука истекла — переадресация на `adminka.example.com`. Своей кнопки выхода у Grafana нет:
доступ кончается через час, со сроком куки, или выходом из админки — он гасит куку.

1. **DNS**: A-запись `grafana.example.com` на тот же адрес.
2. **Сервер**: скопировать `nginx/conf/nginx.conf`, `docker-compose.yml`, `grafana/`,
   `otel-collector/`, `prometheus/`, `tempo/`, поднять телеметрию и пересоздать nginx (замок —
   тот же, что у админки, и открывается вместе с ней). Пересоздать, а не перезагрузить: nginx
   входит в сеть шлюза `grafana-gateway` с закреплённым адресом `172.30.255.2`, и Grafana верит
   `X-WEBAUTH-*` только с него (`GF_AUTH_PROXY_WHITELIST`) — иначе любой контейнер общей сети
   `nginx-proxy-man` пришёл бы с `X-WEBAUTH-ROLE: Admin`. Подсеть `172.30.255.0/29` не должна
   пересекаться с уже заведёнными (`docker network inspect $(docker network ls -q) | grep Subnet`);
   занята — сменить её в `docker-compose.yml` вместе с обоими `ipv4_address` (у Grafana
   свой, `.3`: со свободным адресом она, стартовав раньше nginx, заняла бы его `.2`)
   и `GF_AUTH_PROXY_WHITELIST`:
   ```bash
   docker compose up -d
   ```
3. **Сертификат** — третьим доменом в тот же (на чистой машине он уже в нём, раздел 4):
   ```bash
   docker compose run --rm certbot certonly --webroot --webroot-path /var/www/certbot/ \
     --expand -d example.com -d www.example.com -d adminka.example.com -d grafana.example.com
   docker compose exec webserver nginx -s reload
   ```
4. **Publish Api** — кука выдаётся с этой версии; до неё вход в админку Grafana не откроет.

**Инструмент без proxy-auth** (приёмник трасс, Jaeger UI и подобные) встаёт так же:
server-блок по образцу Grafana в `nginx.conf` — тот же `include admin-allow*.inc`,
`deny all` и `auth_request`, — а своя аутентификация у инструмента не включается вовсе.
Портов на хост ни у него, ни у Grafana нет, даже на `127.0.0.1`: заголовок
`X-WEBAUTH-USER` иначе подделал бы любой, кто достучался до порта.

**Проверка.** Пока админка открыта (Admin Access), не входя в админку, `https://grafana.example.com/`
переадресует на `adminka.example.com`; после входа в админку открывается Grafana под
вашим идентификатором и с вашей ролью. Снаружи и с самого сервера — порта нет:

```bash
curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' https://grafana.example.com/  # 302 https://adminka.example.com/
curl -s -m 5 -o /dev/null -w '%{http_code}\n' http://<адрес сервера>:3000/            # 000
docker port grafanacontainer                                                          # пусто
curl -s -m 5 -o /dev/null -w '%{http_code}\n' http://127.0.0.1:3000/                  # 000, на сервере
# Мимо nginx из общей сети заголовкам не верят: не 200, а отказ auth.proxy
docker run --rm --network nginx-proxy-man curlimages/curl -s -o /dev/null -w '%{http_code}\n' \
  -H 'X-WEBAUTH-USER: probe' -H 'X-WEBAUTH-ROLE: Admin' http://grafanacontainer:3000/api/user
```

#### Телеметрия: что показывает Grafana

API шлёт метрики и трассы по OTLP в коллектор (`otelcollectorcontainer:4317`, адрес —
`OTEL_EXPORTER_OTLP_ENDPOINT` в `publish-api.yml` и `rollback-api.yml`). Коллектор раскладывает
их: метрики уходят в Prometheus (принимает OTLP сам, `--web.enable-otlp-receiver`), трассы — в Tempo.
Без этой переменной API не шлёт ничего. Портов на хост нет ни у кого из троих:
своей аутентификации у них нет, видно их только через Grafana.

| Контейнер | Конфиг | Хранит |
|---|---|---|
| `otelcollectorcontainer` | `otel-collector/config.yaml` | ничего, держит до 150 МБ |
| `prometheuscontainer` | `prometheus/prometheus.yml` | метрики, 30 дней или 2 ГБ |
| `tempocontainer` | `tempo/tempo.yaml` | трассы, 30 дней — как метрики |
| `postgresexportercontainer` | переменные в `docker-compose.yml` | ничего, выставляет состояние PostgreSQL |
| `nodeexportercontainer` | флаги в `docker-compose.yml` | ничего, выставляет процессор, память, диск машины |
| `cadvisorcontainer` | флаги в `docker-compose.yml` | ничего, выставляет то же по каждому контейнеру |
| `qdrantcontainer` | `docker-compose.yml` | векторный индекс (`specs/search.md`); свой `/metrics` на `6333` |

Дашборды (папка PetTemplate): `pettemplate-overview` — HTTP, почта, рантайм .NET;
`queue` — очередь команд (письма, индексация поиска); `postgres`, `qdrant`, `host-node`,
`containers` — база, векторный индекс, машина, контейнеры. Источники данных и дашборды заводятся из файлов, а не в интерфейсе: том Grafana
пересоздаётся, а файлы лежат в репозитории (`grafana/provisioning/`, дашборды —
`grafana/dashboards/*.json`). Дашборд поправили в интерфейсе — экспортируйте JSON
(Share → Export) в `grafana/dashboards/`: при пересоздании Grafana версия из файла
перезапишет правку. Новый дашборд — тоже файлом рядом.

Поменяли конфиги — скопировать их на сервер и пересоздать контейнер:

```bash
docker compose up -d --force-recreate otel-collector prometheus tempo grafana
```

**Проверка.** Метрики дошли до Prometheus (с самого сервера, через сеть контейнеров):

```bash
docker run --rm --network nginx-proxy-man curlimages/curl -s \
  'http://prometheuscontainer:9090/api/v1/label/__name__/values' | tr ',' '\n' | grep -E 'http_server|pettemplate_mail|local_command_queue'
```

`pettemplate_mail_*` появляются с первым письмом, `local_command_queue_*` — через минуту после
старта API.

#### Метрики PostgreSQL, Qdrant и машины

API шлёт метрики сам, а стороннее только выставляет `/metrics`, и Prometheus забирает
его раз в 30 секунд (`prometheus/prometheus.yml`, `scrape_configs`): Qdrant — свой
`/metrics`, PostgreSQL — через `postgres-exporter`, машина — `node-exporter`, контейнеры — `cadvisor`. Дашборды к ним — готовые
с grafana.com (номер — в описании дашборда), в `grafana/dashboards/`.

Экспортёру PostgreSQL нужен свой пользователь: встроенная роль `pg_monitor` — только служебная
статистика (`pg_stat_*`, `pg_settings`), к строкам таблиц доступа нет, не больше трёх соединений.
Заводит его `postgres/init/02-exporter-user.sh` — сам, на пустом томе, рядом с пользователем
приложения (раздел 9), из `POSTGRES_EXPORTER_USER`/`POSTGRES_EXPORTER_PASSWORD` в `.env`
(пароль — `openssl rand -hex 24`). Пароль не задан — скрипт пользователя не заводит, а база
всё равно встаёт.

**Том заведён раньше экспортёра** (или сменить пароль): вписать пароль в `~/IaC/.env`,
пересоздать контейнер базы, чтобы он увидел новое значение, и запустить скрипт руками — он
идемпотентный:

```bash
docker compose up -d database
docker compose exec database /docker-entrypoint-initdb.d/02-exporter-user.sh
docker compose up -d postgres-exporter
```

**Проверка.** Все цели отвечают — у каждой `1`:

```bash
docker run --rm --network nginx-proxy-man curlimages/curl -s \
  'http://prometheuscontainer:9090/api/v1/query?query=up' | grep -o '"job":"[a-z]*"[^]]*"[01]"'
```

`0` у `qdrant` — лежит сам Qdrant, а с ним и API. `1` у `postgres` значит только, что жив экспортёр; до базы он достучался, если
`pg_up` = `1`. `0` — пароль не тот или пользователя нет: `docker logs postgresexportercontainer`.

#### Тревоги в Telegram

**По умолчанию выключено** (`specs/infrastructure.md`, 13): `telegram.yml` лежит в
`grafana/on-hold/`, Grafana его не читает. Правила из `grafana/provisioning/alerting/rules.yml`
срабатывают и видны в Alerting → Alert rules, но никуда не уходят. `TELEGRAM_BOT_TOKEN` в `.env`
пустой. Почему не включено сразу: без токена бота и id группы Grafana с `telegram.yml` в
provisioning не стартует вовсе — а у свежего проекта из шаблона их ещё нет.

Правила и получатель — файлами: `rules.yml` — что считается тревогой, `telegram.yml` — куда
её слать. Править их в интерфейсе нельзя — только файлом и пересозданием Grafana. Токен бота
в репозиторий не попадает: он в `~/IaC/.env` на сервере (`TELEGRAM_BOT_TOKEN`; это бот тревог,
не обязательно тот, что для входа, — у секрета Actions с тем же именем своя роль, раздел 6).
id группы лежит прямо в `telegram.yml`: из переменной Grafana читает его числом и не стартует,
а без токена он ничего не даёт.

**Включить** — один раз:

1. В Telegram у [@BotFather](https://t.me/BotFather): `/newbot`, имя и логин бота — он
   ответит токеном вида `123456:ABC...`.
2. Создать закрытую группу, добавить бота администратором — права не нужны никакие:
   писать в группе может любой участник, а админом бот видит все сообщения, и по
   ним находится id группы (шаг 3).
3. Написать в группу любое сообщение и узнать её id — число с минусом:
   ```bash
   curl -s "https://api.telegram.org/bot<токен>/getUpdates" | grep -o '"chat":{"id":-[0-9]*'
   ```
   Пусто — бот ещё не админ или сообщение написано до того, как его добавили; упоминание
   бота (`@логин_бота тест`) он видит в любом случае. id берётся после настройки группы:
   включение тем и подобное переделывает её в супергруппу с новым id.
4. Переложить файл в provisioning и вписать id группы вместо заглушки `-100` в `chatid`:
   ```bash
   git mv IaC/grafana/on-hold/telegram.yml IaC/grafana/provisioning/alerting/telegram.yml
   ```
   Шапку файла про «вне провижининга» — убрать, в `specs/infrastructure.md` (13) — отметить,
   что доставка включена. Токен — в `~/IaC/.env` на сервере:
   ```
   TELEGRAM_BOT_TOKEN=123456:ABC...
   ```
5. Скопировать `grafana/` на сервер и `docker compose up -d --force-recreate grafana`.

**Проверка.** Grafana → Alerting → Contact points → `telegram` → Test: в группу пришло
тестовое сообщение. Alerting → Alert rules → папка PetTemplate — все правила `Normal`.

### 12. Справка (`~/pettemplate-help`)

Статьи `help/` — то, что сайт рассказывает о себе и по чему будет отвечать ассистент
(`specs/help.md`, 6.4). Справку приносит выкатка: последний job Publish Api копирует `help/`
того же коммита на хост, а `dapi` монтирует каталог только на чтение и раз в минуту
перечитывает манифест. Руками заводить ничего не нужно — каталог создаёт сам workflow.

```
~/pettemplate-help/
  releases/<sha>/     статьи и manifest.json (коммит, время, статьи, хэш)
  current → releases/<sha>     относительная ссылка на опубликованный выпуск
```

Хранятся пять последних выпусков. В контейнере каталог виден как `/app/help`
(`-v ~/pettemplate-help:/app/help:ro` в Publish Api и Rollback Api), API читает
`/app/help/current` (`HelpOptions__Path`).

- **Правка формулировки без выкатки API** — Actions → Publish Help → Run workflow с той
  ветки, что сейчас на проде.
- **Откат.** Rollback Api справку не откатывает: в образе её нет. Откатили через фичу —
  переключить справку руками на прежний выпуск:
  ```bash
  ls -lt ~/pettemplate-help/releases                                   # выпуски, свежие сверху
  ln -sfn releases/<sha> ~/pettemplate-help/current.tmp && mv -Tf ~/pettemplate-help/current.tmp ~/pettemplate-help/current
  ```

**Проверка:** через минуту-две после выкатки

```bash
readlink ~/pettemplate-help/current                    # releases/<sha выкаченного коммита>
docker logs dapi 2>&1 | grep 'Help .* published'
```

и в Grafana, дашборд «Ассистент», строка «Справка»: «Версия справки» показывает тот же коммит
(`pettemplate_assistant_help_index_version`, метка `commit`), рядом — доля пустых поисков
(`pettemplate_assistant_help_search`, `found="false"`) — список вопросов, на которые в справке
ответа нет (`specs/help.md`, 10).

## 📚 Подробная документация

- **Инфраструктура**: [IaC/README.md](README.md)
- **Подключение к базе**: `src/PetTemplate.Db/README.md`
- **Workflows**: [.github/workflows/](../.github/workflows/) — `publish-api`, `rollback-api`,
  `list-api-versions`, `cleanup-old-images`, `backup-db`, `publish-angular`, `publish-admin`,
  `admin-access`, `publish-help`, `help-check`

## 🔧 Полезные команды

```bash
# Просмотр логов API
docker logs -f dapi

# Просмотр логов инфраструктуры
cd ~/IaC && docker compose logs -f

# Проверка статуса
docker ps

# Откат API на предыдущую версию
# Actions → Rollback Api → Run workflow

# Откат фронтенда
rm -rf ~/frontend/* && cp -r ~/frontend-backup/* ~/frontend/

# Откат админки
rm -rf ~/adminka/* && cp -r ~/adminka-backup/* ~/adminka/

# Логи телеметрии
docker logs grafanacontainer
docker compose logs otel-collector prometheus tempo postgres-exporter
```
