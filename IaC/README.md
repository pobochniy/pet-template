# Infrastructure as Code (IaC) для проекта PetTemplate

Эта папка содержит инфраструктуру для развёртывания PetTemplate на виртуальной машине
(`specs/infrastructure.md`, раздел 8). API в compose не входит: его контейнер `dapi`
поднимают workflow GitHub Actions. Пошаговый деплой с нуля, секреты и админка —
[DEPLOYMENT.md](DEPLOYMENT.md).

## 📋 Содержимое

```
IaC/
├── docker-compose.yml       # nginx, certbot, PostgreSQL, Qdrant, эмбеддер, телеметрия
├── nginx/
│   ├── README.md                    # как устроен conf/, CSP, новое имя, проверка без сервера
│   └── conf/
│       ├── nginx.conf               # HTTP: проверка certbot и переадресация; include сайтов (nginx/README.md)
│       ├── sites/                   # HTTPS по файлу на имя: продукт, adminka, grafana
│       ├── snippets/                # общие куски server-блоков: TLS, прокси в API, замок
│       └── admin-allow.inc.example  # образец постоянного списка адресов админки
├── postgres/
│   └── init/
│       ├── 01-app-user.sh       # пользователь приложения — при первом старте на пустом томе
│       └── 02-exporter-user.sh  # пользователь postgres-exporter (pg_monitor) — там же
├── otel-collector/config.yaml   # куда коллектор раскладывает OTLP от API
├── prometheus/prometheus.yml    # кого Prometheus опрашивает сам
├── tempo/tempo.yaml             # хранилище трасс
├── grafana/
│   ├── provisioning/        # источники данных, папка дашбордов, правила тревог
│   ├── on-hold/             # telegram.yml — доставка тревог, пока не включена
│   └── dashboards/          # дашборды JSON
├── setup-vm.sh              # Скрипт подготовки виртуальной машины
├── run-api.sh               # docker run контейнера API и ожидание /health (зовут workflow)
├── publish-static.sh        # атомарная выкатка клиента и админки (зовут workflow)
├── issue-cert.sh            # Сертификат на все имена: выпуск на чистой машине и расширение
├── DEPLOYMENT.md            # Пошаговый деплой, секреты GitHub Actions, админка, бэкап
├── .env.example             # Шаблон переменных окружения
├── .env                     # Переменные окружения (создаётся setup-vm.sh, только на сервере)
├── frontend/                # Симлинк на ~/frontend: releases/<sha>, current, previous
├── adminka/                 # Симлинк на ~/adminka — то же для админки
├── certbot/                 # Сертификаты Let's Encrypt (создаётся автоматически)
│   ├── www/
│   └── conf/
└── README.md                # Этот файл
```

Qdrant и эмбеддер поиска (`specs/search.md`) — в том же compose; модель эмбеддер качает сам
(`DEPLOYMENT.md`, раздел 4, «Модель эмбеддера»). Телеметрия (раздел 10) — коллектор, Prometheus,
Tempo, Grafana за шлюзом админки и экспортёры — `DEPLOYMENT.md`, раздел 11.

Домен `example.com` — символ шаблона `domain`: `dotnet new pet --domain <домен>` заменяет его
во всём проекте, включая `adminka.` и `grafana.`.

## 🚀 Быстрый старт

### 1. Подготовка виртуальной машины

Скопируйте папку `IaC` на виртуальную машину и запустите скрипт подготовки:

```bash
# Скопируйте папку IaC на VM (без .env — он живёт только на сервере)
scp -r IaC user@your-vm:~

# Подключитесь к VM
ssh user@your-vm

# Перейдите в папку IaC
cd ~/IaC

# Запустите скрипт — через bash: в проекте из шаблона бита исполнения у него нет
# (specs/infrastructure.md, 13); остальным скриптам его поставит сам setup-vm.sh
bash setup-vm.sh
```

Скрипт автоматически:
- ✅ Проверит Docker, Docker Compose v2 и права на docker без `sudo`
- ✅ Создаст каталоги `~/frontend`, `~/adminka` (выпуски в них заводит выкатка)
- ✅ Создаст симлинки `IaC/frontend` → `~/frontend` и `IaC/adminka` → `~/adminka`
- ✅ Создаст Docker сеть `nginx-proxy-man`
- ✅ Поставит бит исполнения скриптам `postgres/init/` и `IaC/*.sh`
- ✅ Создаст файл `.env` копией `.env.example`
- ✅ Создаст директории для certbot

### 2. Настройка переменных окружения

Отредактируйте файл `.env` и установите безопасные пароли:

```bash
nano .env
```

```env
# Database configuration
POSTGRES_DB=pettemplatedb
POSTGRES_USER=postgres
POSTGRES_PASSWORD=your_secure_postgres_password
# Пользователь приложения — под ним ходит API (секрет API_DB_CONNECTION_STRING), только DML
APP_DB_USER=pettemplate_app
APP_DB_PASSWORD=your_secure_app_password
# Владелец базы — под ним выкатка накатывает миграции (секрет MIGRATIONS_DB_CONNECTION_STRING)
APP_DB_OWNER=pettemplate_owner
APP_DB_OWNER_PASSWORD=your_secure_owner_password
# Пользователь экспортёра метрик базы (роль pg_monitor)
POSTGRES_EXPORTER_USER=postgres_exporter
POSTGRES_EXPORTER_PASSWORD=your_secure_exporter_password
# Бот тревог Grafana — пока доставка не включена, пусто (DEPLOYMENT.md, «Тревоги в Telegram»)
TELEGRAM_BOT_TOKEN=
# Телеметрия — профиль compose; пусто — без Grafana, Prometheus, Tempo и экспортёров
COMPOSE_PROFILES=observability

# Порты
NGINX_HTTP_PORT=80
NGINX_HTTPS_PORT=443
```

Пароли и пользователи читаются только при **первом** старте Postgres на пустом томе
`db-data`; дальше они живут в самой базе. Поменять пароль в `.env` у существующей базы —
получить `password authentication failed` (как сменить — `DEPLOYMENT.md`, «Пользователь
приложения»). Символ `$` в значениях compose считает подстановкой переменной — в паролях
его не используйте (`openssl rand -hex 24` даёт безопасный вариант).

### 3. Запуск инфраструктуры

```bash
# Запуск в фоновом режиме
docker compose up -d

# Проверка статуса (database — healthy)
docker compose ps

# Просмотр логов
docker compose logs -f
```

### 4. SSL сертификаты

Один сертификат на все имена — `example.com`, `www`, `adminka` и `grafana`, путь `live/example.com/`.
Выпуск на чистой машине и расширение новым именем — одной командой (`DEPLOYMENT.md`, раздел 4):

```bash
bash ~/IaC/issue-cert.sh
```

Продление — cron:

```bash
# Автообновление сертификатов (добавьте в crontab)
0 0 * * * cd ~/IaC && docker compose run --rm certbot renew && docker compose exec webserver nginx -s reload
```

### 5. Настройка GitHub Actions Runner

Раннер — `self-hosted`, без своего label: workflow выкатки ищут его по `runs-on: self-hosted`.
Пошагово — `DEPLOYMENT.md`, раздел 5.

## 🏗️ Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                    Internet (Port 80/443)               │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
                  ┌──────────────┐
                  │    Nginx     │ (webserver)
                  │ example.com  │
                  │  adminka.    │
                  └──────┬───────┘
                         │
         ┌───────────────┼────────────────┐
         │               │                │
         ▼               ▼                ▼
    ┌──────────┐   ┌──────────┐    ┌──────────────┐
    │ Frontend │   │   API    │───▶│ PostgreSQL   │
    │ ~/frontend   │  (dapi)  │    │ dbcontainer  │
    │ ~/adminka│   │  :8080   │───▶│ Qdrant       │
    └──────────┘   └──────────┘───▶│ эмбеддер     │
                                   └──────────────┘
                         │ OTLP           │ pg_monitor
                         ▼                ▼
              ┌────────────────┐   ┌───────────────────┐
              │ otel-collector │   │ postgres-exporter │
              └──┬──────────┬──┘   │ node-exporter     │
       метрики   │          │      │ cadvisor          │
                 ▼          │      └─────────┬─────────┘
         ┌────────────┐     │ трассы         │
         │ Prometheus │◀────┼────────────────┘ scrape
         └─────┬──────┘     ▼
               │        ┌───────┐
               │        │ Tempo │
               │        └───┬───┘
               └─────┬──────┘
                     ▼
               ┌──────────┐  только через nginx: grafana.example.com,
               │ Grafana  │  сеть grafana-gateway, вход — сессия админки
               └──────────┘

                        nginx-proxy-man network
```

### Компоненты:

1. **Nginx (webserver)**
   - Отдаёт сборку клиента из `~/frontend` на `example.com` и сборку админки из `~/adminka`
     на `adminka.example.com`
   - Проксирует `/api/` на контейнер `dapi:8080`
   - Закрывает `/api/admin*` на основном домене и весь `adminka.example.com` замком по адресу
     (`DEPLOYMENT.md`, «Админка»)
   - Обрабатывает SSL/TLS (certbot)

2. **API контейнер (dapi)**
   - Создаётся через GitHub Actions (`Publish Api`), в compose не входит
   - Порт: `127.0.0.1:5000:8080` — снаружи только через nginx
   - Подключается к сети `nginx-proxy-man`, потолок памяти 1.5 ГБ (`run-api.sh`)
   - `GET /health` — база и Qdrant; выкатка ждёт его и сама возвращает прежний образ
   - Миграций на старте не накатывает: их до замены контейнера накатывает бандл под владельцем
     базы (`publish-api.yml`, шаг `apply migrations`)

3. **PostgreSQL 18 (dbcontainer)**
   - База данных: `pettemplatedb`, владелец — `pettemplate_owner` (под ним идут миграции);
     API ходит под `pettemplate_app` — только DML (`DEPLOYMENT.md`, «Пользователь приложения»); суперпользователь
     `postgres` — только для обслуживания и бэкапа
   - Данные в именованном томе `db-data` (`docker volume ls` → `iac_db-data`)
   - Порт на хост не публикуется вовсе — только сеть `nginx-proxy-man`
   - Healthcheck — `pg_isready`

4. **Qdrant (qdrantcontainer)** — `specs/search.md`, раздел 3
   - Обязателен, как база: в нём векторный индекс поиска; API на старте заводит в нём
     коллекцию и без него не поднимается
   - Данные в томе `qdrant-data`, порты `6333`/`6334` — только на `127.0.0.1`. Не бэкапится:
     восстанавливается реиндексом из админки (`DEPLOYMENT.md`, раздел 10)

5. **Эмбеддер (embeddingscontainer)** — `specs/search.md`, раздел 3
   - llama.cpp на процессоре, `multilingual-e5-small` в Q8: считает векторы для поиска
     по смыслу. Веса качает сам при первом старте в том `embeddings-cache`
   - Портов нет: к нему ходит только API по сети `nginx-proxy-man`
   - Не обязателен для старта API: лежит — индексация ждёт в очереди, поиск идёт по словам

6. **Certbot**
   - Получение и продление сертификатов Let's Encrypt
   - Хранит сертификаты в `./certbot/conf`

7. **Телеметрия** (`DEPLOYMENT.md`, раздел 11) — профиль compose `observability`, включён
   строкой `COMPOSE_PROFILES` в `.env`; у каждого контейнера потолок памяти
   - `otel-collector` принимает OTLP от API и раскладывает: метрики — в `prometheus`
     (30 дней или 2 ГБ, том `prometheus-data`), трассы — в `tempo` (7 дней, том `tempo-data`)
   - `postgres-exporter`, `node-exporter`, `cadvisor` и сам Qdrant — Prometheus забирает их `/metrics` сам
   - `grafana` — без своих учёток, за шлюзом админки (`grafana.example.com`, кука сессии
     админки и `auth_request` в nginx); источники, дашборды и тревоги — файлами из `grafana/`
   - Портов на хост нет ни у кого из них

## 📦 Управление инфраструктурой

### Основные команды Docker Compose

```bash
# Запуск всех сервисов
docker compose up -d

# Остановка всех сервисов. Без -v: -v удаляет тома, а с ними базу
docker compose down

# Перезапуск конкретного сервиса
docker compose restart webserver

# Проверка и перечитывание конфигурации nginx без перезапуска
docker compose exec webserver nginx -t && docker compose exec webserver nginx -s reload

# Просмотр логов конкретного сервиса
docker compose logs -f webserver

# Проверка статуса
docker compose ps
```

### Управление API контейнером (через GitHub Actions)

API контейнер (`dapi`) управляется через GitHub Actions и не входит в docker-compose.yml.

```bash
# Просмотр логов API
docker logs dapi

# Просмотр логов в реальном времени
docker logs -f dapi

# Перезапуск API (вручную, не рекомендуется)
docker restart dapi

# Проверка переменных окружения
docker inspect dapi | grep -A 10 "Env"
```

Откат — workflow `Rollback Api`, список версий — `List Api Versions`.

### Управление базой данных

```bash
# Подключение суперпользователем — по сокету внутри контейнера, пароль не спрашивается
docker exec -it dbcontainer psql -U postgres -d pettemplatedb

# С правами пользователя приложения — посмотреть, что видит API. Вход по сокету
# доверенный, пароль не проверяется; проверка пароля — DEPLOYMENT.md, «Пользователь приложения»
docker exec -it dbcontainer psql -U pettemplate_app -d pettemplatedb

# Бэкап — workflow Backup Db (дамп в ~/db-backups). Вручную — формат custom (-Fc):
docker exec dbcontainer pg_dump -U postgres -d pettemplatedb -Fc > backup_$(date +%Y%m%d).dump
docker exec -i dbcontainer pg_restore -l < backup_$(date +%Y%m%d).dump > /dev/null && echo OK

# Восстановление — DEPLOYMENT.md, «Бэкап и восстановление базы»
```

Ручной SQL — без кавычек, snake_case: `select user_name, email from profiles;`.
Подключение к базе с машины разработчика и изнутри докера — `src/PetTemplate.Db/README.md`.

### Управление фронтендом

Клиент деплоится через GitHub Actions выпусками в `~/frontend/releases/<sha>`, админка —
в `~/adminka/releases/<sha>`; nginx отдаёт тот, на который смотрит ссылка `current`
(`DEPLOYMENT.md`, раздел 7).

```bash
# Выпуски и ссылки
ls -l ~/frontend ~/frontend/releases

# Откат на прежний выпуск — переключить ссылку, без пересборки
ln -sfn "$(readlink ~/frontend/previous)" ~/frontend/current.tmp && mv -Tf ~/frontend/current.tmp ~/frontend/current

# То же для админки
ln -sfn "$(readlink ~/adminka/previous)" ~/adminka/current.tmp && mv -Tf ~/adminka/current.tmp ~/adminka/current
```

## 🔐 Безопасность

### Рекомендации:

1. **Используйте сильные пароли** для Postgres — и суперпользователя, и пользователя приложения
2. **Настройте firewall** на VM:
   ```bash
   sudo ufw allow 22/tcp    # SSH
   sudo ufw allow 80/tcp    # HTTP
   sudo ufw allow 443/tcp   # HTTPS
   sudo ufw enable
   ```
3. **Не публикуйте порты БД, Qdrant и API наружу** — см. следующий раздел
4. **Регулярно обновляйте** Docker образы и систему
5. **Делайте бэкапы** БД (`Backup Db`), обязательно — перед миграцией, которая что-то удаляет

### Доступ к БД: ufw здесь не работает

Docker пишет свои правила в цепочку `DOCKER` таблицы nat, и они срабатывают **раньше**
правил ufw. Опубликованный через `ports:` порт доступен из интернета независимо от того,
что написано в ufw: `ufw deny 5432/tcp` его не закроет, а `ufw allow` ничего не открывает,
потому что порт и так открыт. Проверить, кто на самом деле слушает:

```bash
sudo ss -tulpn | grep 5432        # 0.0.0.0:5432 — торчит наружу, 127.0.0.1:5432 — закрыт, пусто — не опубликован
sudo iptables -t nat -S DOCKER | grep 5432
```

Поэтому у `database` в `docker-compose.yml` порта нет вовсе. Если нужен клиент (DBeaver,
DataGrip) с локальной машины — раскомментируйте публикацию, и только на локальный интерфейс:

```yaml
    ports:
      - "127.0.0.1:${DB_PORT:-5432}:5432"
```

То же правило — у контейнера API (`127.0.0.1:5000`, в `Publish Api`).

После `docker compose up -d database` порт доступен только с самой VM. Ходить в базу
с локальной машины — через SSH-туннель, открывать порт для этого не нужно:

```bash
ssh -N -L 15432:127.0.0.1:5432 user@<ip-вм>
# дальше клиент подключается на 127.0.0.1:15432
```

Локальный конец — отдельный порт, чтобы не путать прод с дев-базой из корневого
`docker-compose.yml` (`5432`): обе называются `pettemplatedb`.

В DBeaver, DataGrip и TablePlus то же самое настраивается на вкладке SSH в свойствах
подключения, отдельный терминал не нужен.

Отдельно помните про фаервол провайдера (security group в панели хостинга): он живёт
выше ufw и Docker, и порты 80/443 должны быть открыты именно там.

## 🐛 Troubleshooting

### Nginx не запускается

```bash
# Проверьте логи
docker compose logs webserver

# Проверьте конфигурацию
docker compose exec webserver nginx -t

# Проверьте, что порты 80/443 свободны
sudo ss -tulpn | grep -E ':(80|443) '
```

`host not found in upstream "dapi"` — контейнера API ещё нет в сети `nginx-proxy-man`:
nginx разрешает имена из `proxy_pass` на старте. Сначала `Publish Api`, потом nginx.
`cannot load certificate` — сертификата ещё нет (раздел 4 выше).

### База данных не запускается

```bash
# Проверьте логи
docker compose logs database

# Проверьте здоровье
docker inspect --format '{{.State.Health.Status}}' dbcontainer

# Том на месте
docker volume ls | grep db-data
```

`APP_DB_USER не задан` (или `APP_DB_OWNER`) в логе первого старта — в `.env` нет пары
`APP_DB_USER`/`APP_DB_PASSWORD` или `APP_DB_OWNER`/`APP_DB_OWNER_PASSWORD`;
скрипт `postgres/init` упал, и образ postgres оставил том недоинициализированным: исправить
`.env`, `docker compose down -v` (база ещё пустая) и `up -d` заново.

### API не стартует

```bash
docker logs dapi
```

- Не подключается к БД — контейнеры в одной сети?
  ```bash
  docker network inspect nginx-proxy-man
  ```
  Строка подключения в секрете `API_DB_CONNECTION_STRING`:
  `Host=dbcontainer;Port=5432;Database=pettemplatedb;Username=pettemplate_app;Password=...`
- `password authentication failed for user "pettemplate_app"` — пароль в секрете не тот,
  с которым создавался том (`DEPLOYMENT.md`, «Пользователь приложения»).
- `permission denied for table …` — у `pettemplate_app` нет прав на таблицу: она создана
  не владельцем (`pettemplate_owner`), и `default privileges` на неё не распространились
  (например, восстановление с `--no-owner`). Перезапустить `01-app-user.sh` руками —
  `DEPLOYMENT.md`, «Пользователь приложения».
- Упал шаг `apply migrations` в `Publish Api` — строка `MIGRATIONS_DB_CONNECTION_STRING`
  (пользователь `pettemplate_owner`, пароль `APP_DB_OWNER_PASSWORD`); старый контейнер при этом не тронут.
- Короткий ключ подписи — секрет `JWT_SIGNING_KEY` (`DEPLOYMENT.md`, раздел 6).

### Фронтенд показывает 404

```bash
# Проверьте, что выпуск есть и current на него смотрит
ls -l ~/frontend ~/frontend/current/

# Проверьте симлинк
ls -la ./frontend

# Проверьте конфигурацию nginx
docker compose exec webserver nginx -T
```

## 📚 Дополнительные ресурсы

- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [PostgreSQL Docker image](https://hub.docker.com/_/postgres)
- [Nginx Documentation](https://nginx.org/en/docs/)
- [Let's Encrypt Documentation](https://letsencrypt.org/docs/)
- [GitHub Actions Self-Hosted Runners](https://docs.github.com/en/actions/hosting-your-own-runners)

## 🔄 Процесс деплоя

Выкатка — руками, `Run workflow` (`specs/infrastructure.md`, раздел 1): push в `main`
ничего не выкатывает.

### Деплой API (через GitHub Actions):

1. Разработчик запускает workflow "Publish Api" в GitHub
2. Тесты на облачном раннере (`api-tests.yml`); красные — дальше не идёт
3. Раннер на VM собирает API и Docker образ `pettemplate-api:<дата>`, накатывает миграции
4. `run-api.sh` заменяет контейнер `dapi` и ждёт `200` от `/health`; не дождался —
   поднимает прежний образ, выкатка красная
5. Старые образы удаляются (`Cleanup Old Images`, остаются 5 последних)

### Деплой фронтенда и админки (через GitHub Actions):

1. Разработчик запускает workflow "Publish Angular" ("Publish Admin")
2. Сначала — тесты клиента (`web-tests.yml`)
3. Сборка ложится в `~/frontend/releases/<sha>` (`~/adminka/…`), ссылка `current`
   переключается на неё целиком, прежний выпуск остаётся `previous`
4. Nginx сразу отдаёт новый выпуск; `index.html` не кэшируется, бандлы — навсегда,
   а чанки вкладок со старым `index.html` берутся из `previous`

## 📝 Checklist перед первым запуском

- [ ] Docker и Docker Compose v2 установлены, пользователь в группе `docker`
- [ ] Запущен скрипт `setup-vm.sh`
- [ ] Отредактирован файл `.env` с безопасными паролями (`POSTGRES_PASSWORD`, `APP_DB_PASSWORD`,
  `APP_DB_OWNER_PASSWORD`, `POSTGRES_EXPORTER_PASSWORD`); `COMPOSE_PROFILES=observability` на месте
- [ ] Запущена инфраструктура: `docker compose up -d`, `database` — `healthy`
- [ ] Получен сертификат на `example.com`, `www.example.com`, `adminka.example.com`
- [ ] Настроен GitHub Actions self-hosted runner
- [ ] Добавлены секреты в GitHub (см. `DEPLOYMENT.md`, раздел «Секреты GitHub Actions»)
- [ ] Настроен firewall на VM и у провайдера
- [ ] `docker port dbcontainer` — пусто (или только `127.0.0.1:…`)

## 🎉 Готово!

После выполнения всех шагов инфраструктура готова к работе. Используйте GitHub Actions для деплоя API, клиента и админки.
