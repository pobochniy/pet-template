#!/usr/bin/env bash
# Сертификат Let's Encrypt на все имена сайта — один, live/<первое имя>/ (IaC/DEPLOYMENT.md, раздел 4).
#
#   bash ~/IaC/issue-cert.sh
#
# Имена — из server_name HTTP-блока nginx/conf/nginx.conf, первое из них — каталог сертификата
# (он же в snippets/tls.inc). Отдельного списка нет: имя в server_name и есть имя в сертификате.
# DNS всех имён — на эту VM, иначе certbot не выпустит ни одного.
#
# Чистая машина: сертификата ещё нет, а сайты из nginx/conf/sites/ без него не дают nginx
# подняться. Скрипт комментирует строку `include …/sites/*.conf;` в nginx.conf, перезапускает
# nginx только на HTTP, выпускает сертификат и возвращает строку. Не вышло — строка остаётся
# закомментированной: nginx живёт на HTTP, и запуск можно повторить.
#
# Сертификат уже есть — например, добавилось имя (IaC/nginx/README.md, «Новое имя»): certbot
# расширяет сертификат (--expand), nginx перечитывает конфигурацию. Не прошла nginx -t —
# строка include возвращается в прежнее состояние, работающий nginx не трогается.
#
# Первый выпуск certbot спрашивает почту и согласие с условиями — отвечать в терминале.
# Продление — не здесь, а cron (IaC/README.md, раздел 4).
set -euo pipefail

CONF=nginx/conf/nginx.conf
INCLUDE='include /etc/nginx/conf.d/sites/*.conf;'

cd "$(dirname "$0")"

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }

# Строка include — в sed как текст, а не как регулярка: в ней `*` и `.`
INCLUDE_RE=$(printf '%s' "$INCLUDE" | sed 's/[][\.*^$/]/\\&/g')
https_off() { sed -i "s|^$INCLUDE_RE|# $INCLUDE|" "$CONF"; }
https_on()  { sed -i "s|^# $INCLUDE_RE|$INCLUDE|" "$CONF"; }
https_is_on() { grep -q "^$INCLUDE_RE" "$CONF"; }

grep -qF "$INCLUDE" "$CONF" || { echo "В $CONF нет строки: $INCLUDE" >&2; exit 1; }

# server_name первого server-блока nginx.conf — HTTP-блок всех имён
read -r -a DOMAINS <<< "$(sed -n 's/^ *server_name \(.*\);/\1/p' "$CONF" | head -n 1)"
[ "${#DOMAINS[@]}" -gt 0 ] || { echo "В $CONF не найден server_name HTTP-блока" >&2; exit 1; }
CERT=/etc/letsencrypt/live/${DOMAINS[0]}/fullchain.pem

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
    docker compose exec -T webserver nginx -t && docker compose exec -T webserver nginx -s reload
}

# Перезапуск, а не reload: контейнер может крутиться в restart-loop (сайты без сертификата),
# и exec в него не войдёт. Затем ждём, пока nginx ответит на 80-м порту изнутри контейнера —
# certbot сразу пойдёт к нему за проверкой
restart_http() {
    docker compose up -d webserver
    docker compose restart webserver
    for _ in $(seq 1 30); do
        if docker compose exec -T webserver sh -c 'test -s /run/nginx.pid' 2>/dev/null; then
            return 0
        fi
        sleep 1
    done
    echo "nginx не поднялся за 30 секунд: docker compose logs webserver" >&2
    return 1
}

# Каталог live/ certbot закрывает от всех, кроме root, — смотрим изнутри его контейнера.
# Ответ — только словом на stdout: код выхода не отличает «файла нет» от ошибки самого docker
# (compose при своих сбоях тоже выходит с 1), а снимать HTTPS с работающего сервера из-за
# сбоя docker нельзя
cert_state=$(docker compose run --rm -T --entrypoint sh certbot \
    -c "if test -f '$CERT'; then echo present; else echo absent; fi" 2>/dev/null | tr -d '\r' | tail -n 1) || true
case $cert_state in
    present|absent) ;;
    *) echo "Не проверить сертификат: docker compose run certbot не ответил. nginx не тронут" >&2
       exit 1 ;;
esac

if [ "$cert_state" = present ]; then
    say "Сертификат есть — расширяю на: ${DOMAINS[*]}"
    issue
    # Прошлый запуск мог выпустить сертификат и не вернуть строку include
    was_on=0
    if https_is_on; then was_on=1; fi
    https_on
    if ! reload; then
        [ "$was_on" -eq 1 ] || https_off
        say "Сертификат расширен, но конфигурация не прошла nginx -t — nginx работает на прежней"
        echo "Ошибка — выше. Чаще всего это host not found in upstream \"dapi\": API не выкачен"
        echo "или контейнер dapi упал (DEPLOYMENT.md, раздел 7). Иначе — исправить nginx/conf/sites/."
        echo "Затем запустить скрипт ещё раз."
        exit 1
    fi
    say "Готово: nginx перечитал конфигурацию"
    exit 0
fi

say "Сертификата нет — nginx на время выпуска только на HTTP"
https_off
restart_http

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
