# Админка: переход с окна `Admin Access` на пропуск (клиентский сертификат)

Сейчас `adminka.example.com` и `grafana.example.com` закрыты замком по адресу
(`snippets/admin-lock.inc`: `include admin-allow*.inc; deny all;`). Чтобы поработать, их открывает
всем на 1–8 часов workflow `Admin Access`. Чем это грозит — [admin-perimeter.md](admin-perimeter.md):
пока окно открыто, к форме входа и до-входовому коду API пускают весь интернет, утёкший пароль
входит с первой попытки, а срок окна держат раннер и cron VM.

Взамен — **пропуск**: клиентский сертификат своего CA, `ssl_verify_client on` на обоих
server-блоках. Без пропуска nginx отвечает `400` ещё при TLS — до статики, `/api/` и формы входа.
Работает с любого VPN, окна и cron не нужно, код API не меняется.

**Образец — ASU, PR [pobochniy/asu#132](https://github.com/pobochniy/asu/pull/132)** (ветка
`admin-client-certs`). Там то же устройство, что в шаблоне (перенесено из Lad), так что переносится
почти один к одному: заменить домены `asau.space` → `example.com`, контейнер `aapi` → `dapi`,
`~/asu-admin-ca` → `~/PetTemplate-admin-ca` (`sourceName` шаблона заменит имя в проектах). Брать
итог PR целиком, а не первый коммит: пять кругов ревью нашли в первом варианте ошибки (ниже).

## Что сделать

### Новое

1. **`IaC/admin-ca.sh`** — запускается на машине владельца, не на сервере: ключ CA на VM не
   попадает. Команды:
   - `init` — CA и пустой список отозванных (`crl.pem`) в `ADMIN_CA_DIR`;
   - `issue <логин>` — `<логин>.p12` в текущий каталог и пароль к нему;
   - `revoke <логин>` — отзывает **все** действующие пропуска логина и пересобирает `crl.pem`;
   - `list` — кому выдано, до какого числа, что отозвано;
   - `push <user@host>` — `ca.crt` и `crl.pem` в `~/IaC/nginx/conf/admin-ca/` и `nginx -t && reload`,
     если nginx запущен (не запущен — только кладёт файлы).

   Сроки: CA и CRL — 10 лет, пропуск — 2 года (решение ASU 2026-11-16: CRL отвозится руками, а
   просроченный CRL nginx не принимает — не пустит никого). Ключи EC P-256; `.p12` — 3DES и SHA-1:
   AES/PBKDF2 из OpenSSL 3 по умолчанию не открывают связка ключей macOS и iOS.
2. **`IaC/nginx/conf/snippets/admin-cert.inc`** вместо `admin-lock.inc`:

   ```nginx
   ssl_verify_client      on;
   ssl_verify_depth       1;
   ssl_client_certificate /etc/nginx/conf.d/admin-ca/ca.crt;
   ssl_crl                /etc/nginx/conf.d/admin-ca/crl.pem;
   ```

   Подключить в `sites/adminka.conf` и `sites/grafana.conf` вместо `admin-lock.inc`.
   `deny` на `/api/admin*` в `product.conf` остаётся — он от замка не зависит.
3. **`IaC/issue-cert.sh`** — до certbot проверить `nginx/conf/admin-ca/ca.crt` и `crl.pem`: без них
   nginx не стартует вовсе, вместе с основным сайтом. Сообщение — что выполнить на машине владельца.
4. **`IaC/setup-vm.sh`** — вместо записи cron `admin-access.sh expire` предупреждение, если файлов
   CA нет.

### Удалить

`.github/workflows/admin-access.yml`, `IaC/admin-access.sh`, `IaC/nginx/conf/snippets/admin-lock.inc`,
`IaC/nginx/conf/admin-allow.inc.example`; в `.gitignore` и `.template.config/template.json` —
`admin-allow.inc` и `admin-allow-open.inc` заменить на `IaC/nginx/conf/admin-ca/` и `*.p12`.

### Документы и код

- `specs/admin.md`, раздел 7 — «Замок по адресу» → пропуск: почему не адрес и не окно, CA у
  владельца, сроки, пропуск на человека (замена потерянного — **сначала `revoke`, потом `issue`**),
  ответ `400` без пропуска, `421` при переиспользовании соединения (ниже); раздел 8, этап 1 —
  без `Admin Access`; раздел 10 «Готово, когда» — `400`/`200` вместо `403`.
- `specs/infrastructure.md` — строки про `admin-allow*.inc`, `admin-access.yml`, cron, дерево `IaC/`,
  таблица служебных workflow.
- `IaC/DEPLOYMENT.md` — раздел 4 (чистая машина): `admin-ca.sh init/issue/push` **до**
  `issue-cert.sh`; раздел 8 «Админка»: пропуска, установка `.p12` в браузер, «ушёл админ», «потерян
  единственный ноутбук» (новый CA); требование OpenSSL 3+ на машине владельца.
- `IaC/README.md`, `IaC/nginx/README.md` («Проверка без сервера» — с тестовым CA), `README.md`.
- `admins.component.ts` (подсказка «workflow Admin Access») — про пропуск.
- `LoginLimiter` — XML-док «`allow` … не прикрывает в тот день, когда админку откроют на время»
  заменить: лимит нужен и за пропуском (пропуск не знает пароля), а счёт по учётке общий с открытым
  входом продукта. Тест вида `ProductFailuresLockAdminLogin` — закрепить это поведение.
- Справка — сверить скиллом `help-sync`.

## Грабли, найденные ревью в ASU

- **Порядок на чистой машине.** `issue-cert.sh` до заведённого CA падает — CA и `push` идут раньше.
- **`push` без поднятого nginx** падал на reload — теперь кладёт файлы и выходит с `0`.
- **`revoke` снимает все пропуска логина**, и выданный взамен тоже: порядок «выдать, потом отозвать»
  оставлял владельца без доступа.
- **LibreSSL macOS** (`/usr/bin/openssl`) не понимает `$ENV::` в `openssl.cnf`: `init` оставлял CA
  наполовину. Скрипт требует OpenSSL 3+ (`brew install openssl`).
- **Уборка при сбое `init`** — на `trap EXIT`, не `ERR`: `ERR` без `set -E` не срабатывает внутри функций.
- **Сбой `gencrl` после `revoke`** — отзыв уже в `index.txt`, а `crl.pem` старый; повторный `revoke`
  должен пересобирать CRL, а не отвечать «действующих нет». Ошибки openssl не глушить `2>/dev/null`.
- **HTTP/2 и один сертификат сайта на все имена.** Браузер может отправить запрос к adminka по
  соединению, открытому к основному домену, — без пропуска. nginx отвечает `421`, браузер открывает
  новое соединение уже с пропуском — проверить, что так и есть.

## Как проверить

nginx с конфигом ветки в Docker (рецепт «Проверка без сервера» в `IaC/nginx/README.md`) и CA от
`admin-ca.sh`:

| Запрос | Ответ |
|---|---|
| adminka без пропуска, с отозванным или чужим | `400` |
| adminka по IP без SNI, `Host: adminka…` | `400` |
| adminka с действующим пропуском | `200` |
| grafana без пропуска | `400` |
| основной домен | `200` |
| `POST <домен>/api/Admin/Login` | `403` |
| соединение к основному домену, `Host: adminka…` | `421` |

Плюс `admin-ca.sh` целиком во временном `ADMIN_CA_DIR` на `/bin/bash` 3.2: `init`, `issue` дважды на
логин, `revoke`, повторный `revoke`, `list`; сбой `gencrl` через обёртку openssl. `.p12` импортируется в
связку ключей macOS (`security import`).

## В проектах из шаблона (на сервере)

1. На машине владельца: `admin-ca.sh init`, `issue` каждому админу, `push <user@host>`.
2. Выкатить конфиг nginx; убрать cron: `crontab -l | grep -v 'admin-access.sh expire' | crontab -`;
   удалить `~/IaC/admin-access.sh` и `admin-allow-open.inc`.
3. Раздать `.p12` с паролем, поставить в браузер; проверить таблицу выше на живом сервере.

## Что остаётся

Вход админа по-прежнему можно запереть неверными паролями через открытый вход продукта: счёт по
учётке общий. В ASU это отдельная карточка (`tech-debt/admin-login-shared-count.md`) — правильнее,
чтобы дверь админки считала только свои неудачи.

## После

Удалить этот файл, [admin-perimeter.md](admin-perimeter.md) и их строки в `README.md`.
