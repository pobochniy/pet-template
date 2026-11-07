#!/usr/bin/env bash
# Сертификат Let's Encrypt на все имена сайта — один, live/example.com/ (IaC/DEPLOYMENT.md, раздел 4).
#
#   bash ~/IaC/issue-cert.sh
#
# Чистая машина: сертификата ещё нет, а сайты из nginx/conf/sites/ без него не дают nginx
# подняться. Скрипт комментирует строку `include …/sites/*.conf;` в nginx.conf, поднимает nginx
# только на HTTP, выпускает сертификат и возвращает строку. Не вышло — строка остаётся
# закомментированной: nginx живёт на HTTP, и запуск можно повторить.
#
# Сертификат уже есть — например, добавилось имя (IaC/nginx/README.md, «Новое имя»): nginx не
# трогается, certbot расширяет сертификат (--expand), nginx перечитывает конфигурацию.
#
# Первый выпуск certbot спрашивает почту и согласие с условиями — отвечать в терминале.
# Продление — не здесь, а cron (IaC/README.md, раздел 4).
set -euo pipefail

# Те же имена, что в server_name HTTP-блока nginx.conf; DNS всех — на эту VM
DOMAINS=(example.com www.example.com adminka.example.com grafana.example.com)
CERT=/etc/letsencrypt/live/example.com/fullchain.pem
CONF=nginx/conf/nginx.conf
INCLUDE='include /etc/nginx/conf.d/sites/*.conf;'

cd "$(dirname "$0")"

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }

# Строка include — в sed как текст, а не как регулярка: в ней `*` и `.`
INCLUDE_RE=$(printf '%s' "$INCLUDE" | sed 's/[][\.*^$/]/\\&/g')
https_off() { sed -i "s|^$INCLUDE_RE|# $INCLUDE|" "$CONF"; }
https_on()  { sed -i "s|^# $INCLUDE_RE|$INCLUDE|" "$CONF"; }

grep -qF "$INCLUDE" "$CONF" || { echo "В $CONF нет строки: $INCLUDE" >&2; exit 1; }

# Имя без проверки certbot в HTTP-блоке не выпустится — ловим расхождение до certbot
server_names=" $(sed -n 's/^ *server_name \(.*\);/\1/p' "$CONF" | head -n 1) "
for domain in "${DOMAINS[@]}"; do
    [[ "$server_names" == *" $domain "* ]] || {
        echo "Имени $domain нет в server_name HTTP-блока $CONF — добавить туда (IaC/nginx/README.md, «Новое имя»)" >&2
        exit 1
    }
done

domain_args=()
for domain in "${DOMAINS[@]}"; do domain_args+=(-d "$domain"); done

# --expand — новое имя дописывается в сертификат; --keep-until-expiring — с теми же именами
# certbot не спрашивает «Keep / Renew» и ничего не перевыпускает: повторный запуск безопасен
issue() {
    docker compose run --rm certbot certonly --webroot --webroot-path /var/www/certbot/ \
        --expand --keep-until-expiring "${domain_args[@]}"
}

# Через &&, а не построчно: под `if !` bash не применяет set -e, и упавший nginx -t
# не остановил бы reload
reload() {
    docker compose exec webserver nginx -t && docker compose exec webserver nginx -s reload
}

# Каталог live/ certbot закрывает от всех, кроме root, — смотрим изнутри его контейнера.
# Код разбираем: 1 — файла нет, а ошибка самого docker (125 и выше) — не повод снимать HTTPS
# с работающего сервера
set +e
docker compose run --rm --entrypoint test certbot -f "$CERT"
cert_rc=$?
set -e
case $cert_rc in
    0|1) ;;
    *) echo "Не проверить сертификат: docker compose run certbot завершился с кодом $cert_rc. nginx не тронут" >&2
       exit 1 ;;
esac

if [ "$cert_rc" -eq 0 ]; then
    say "Сертификат есть — расширяю на: ${DOMAINS[*]}"
    issue
    # Прошлый запуск мог выпустить сертификат и не вернуть строку include
    https_on
    reload
    say "Готово: nginx перечитал конфигурацию"
    exit 0
fi

say "Сертификата нет — nginx на время выпуска только на HTTP"
https_off
docker compose up -d webserver
reload

say "Выпускаю сертификат: ${DOMAINS[*]}"
if ! issue; then
    say "Сертификат не выпущен. nginx остался на HTTP"
    echo "Чаще всего A-запись какого-то имени ещё не на этой VM: dig +short <имя>."
    echo "Исправить и запустить скрипт ещё раз."
    exit 1
fi

say "Возвращаю HTTPS"
https_on
if ! reload; then
    https_off
    say "Сертификат выпущен, но конфигурация HTTPS не прошла nginx -t — nginx остался на HTTP"
    echo "Ошибка — выше. Чаще всего это host not found in upstream \"dapi\": API не выкачен"
    echo "или контейнер dapi упал (DEPLOYMENT.md, раздел 7). Иначе — исправить nginx/conf/sites/."
    echo "Затем запустить скрипт ещё раз."
    exit 1
fi
say "Готово: сайты на HTTPS"
