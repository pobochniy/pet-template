# nginx

`conf/` смонтирован в контейнер `webserver` как `/etc/nginx/conf.d` (только чтение). nginx сам
подключает оттуда только `*.conf` верхнего уровня — `nginx.conf`; остальное подключается явно.

```
conf/
├── nginx.conf              HTTP всех имён: проверка certbot и 308 на HTTPS; последняя строка —
│                           include sites/*.conf
├── sites/                  HTTPS — по файлу на имя
│   ├── product.conf        продукт и API; CSP продукта — map $csp_product
│   ├── adminka.conf        админка за замком по адресу; CSP админки — map $csp_admin
│   └── grafana.conf        Grafana за шлюзом: замок, auth_request в API, свои map и кеш
├── snippets/               общие куски server-блоков, подключаются строкой include
│   ├── tls.inc             http2 и пути сертификата
│   ├── proxy-api.inc       proxy_pass в dapi и заголовки
│   └── admin-lock.inc      замок по адресу: admin-allow*.inc и deny all
└── admin-allow.inc.example образец постоянного списка адресов админки
```

На сервере рядом лежат ещё `admin-allow.inc` (если заведён) и `admin-allow-open.inc` — его пишет
и стирает workflow `Admin Access` (`IaC/DEPLOYMENT.md`, «Админка»).

Статика у продукта и админки своя, а не общим куском: у продукта CSP повторяется в блоке
`index.html` и есть `/media/`, у админки — свои CSP и `X-Frame-Options`, а `add_header` в
`location` отменяет унаследованные с server.

## CSP

Политика каждого сайта — одна строка `map $host $csp_… { default "…"; }` в его файле.
Хеш инлайн-скрипта `src/index.html` в политике продукта вписывает и сверяет
`npm run csp:hash` (`src/PetTemplate.Web/web/tools/csp-hash.mjs`; хук `pre-commit`, `web-tests.yml`).

## Сертификат

Один на все имена из `server_name` HTTP-блока `nginx.conf`, `live/<первое имя>/` — `live/example.com/`. Выпуск и расширение новым именем — `IaC/issue-cert.sh`
(`IaC/DEPLOYMENT.md`, раздел 4): пока сертификата нет, сайты из `sites/` не дают nginx
подняться, и скрипт на время выпуска комментирует строку `include` в `nginx.conf`.
Продление — `crontab` из `IaC/README.md`, раздел 4.

## Новое имя

1. DNS: A-запись на эту VM.
2. Файл в `sites/` — по образцу соседей; инструмент за шлюзом (приёмник трасс, Jaeger UI и подобные) —
   по образцу `grafana.conf`: тот же замок и тот же `auth_request`, своя аутентификация у инструмента
   не включается вовсе, порт на хост не пробрасывается.
3. Имя — в `server_name` HTTP-блока `nginx.conf`: `IaC/issue-cert.sh` берёт имена сертификата
   оттуда, отдельного списка нет. Первым там остаётся `example.com` — по нему назван каталог
   сертификата в `snippets/tls.inc`.
4. На сервере — отвезти `nginx/conf`, `bash ~/IaC/issue-cert.sh`: он расширит сертификат и перечитает nginx.

## Проверка без сервера

С машины разработчика, из корня репозитория. Сертификат — временный самоподписанный, `dapi` —
заглушка на `127.0.0.1`: без них `nginx -t` падает на `cannot load certificate` и
`host not found in upstream "dapi"` и настоящих ошибок за ними не видно.
Grafana на старте не нужна: её имя разрешается на запросе (`resolver` в `sites/grafana.conf`).

```bash
ssl=$(mktemp -d) && mkdir -p "$ssl/live/example.com" &&
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=example.com \
  -keyout "$ssl/live/example.com/privkey.pem" -out "$ssl/live/example.com/fullchain.pem" 2>/dev/null &&
docker run --rm --add-host dapi:127.0.0.1 \
  -v "$PWD/IaC/nginx/conf:/etc/nginx/conf.d:ro" -v "$ssl:/etc/nginx/ssl:ro" nginx:1.30 nginx -t
rm -rf "$ssl"
```
