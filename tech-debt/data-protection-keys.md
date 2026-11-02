# Ключи Data Protection живут внутри контейнера

Каждый старт API пишет в лог предупреждение `FileSystemXmlRepository[60]: Storing keys in
a directory '/root/.aspnet/DataProtection-Keys' that may not be persisted outside of the
container`. Оно право буквально: тома у `pettemplate-api` нет, выкатка делает `docker rm`
и поднимает контейнер заново (`specs/infrastructure.md`, раздел 6) — кольцо ключей
создаётся с нуля на каждый деплой.

## Почему это пока не болит

Этими ключами не защищено ничего: аутентификация — JWT со своим `JwtOptions:SigningKey`,
cookie-схемы, antiforgery, серверной сессии и `IDataProtector` в коде нет
(`grep -r DataProtection src` пуст). Чинить раньше, чем появится что защищать, —
заводить том ради пустого каталога.

## Когда заболит

Вход по cookie, antiforgery на формах, одноразовые ссылки через `IDataProtector`
(сброс пароля из админки — `specs/admin.md`, раздел 12). Отказ будет **отложенным и
не связанным по времени с выкатом**: ссылка из письма перестаёт работать, если между
отправкой и переходом был деплой.

## Что делать, когда дойдут руки

1. Том `--volume dataprotection-keys:/root/.aspnet/DataProtection-Keys` в `docker run`
   (`publish-api.yml` и `rollback-api.yml`) — дёшево, но том становится секретом вне бэкапа базы.
2. `PersistKeysToDbContext<ApplicationContext>` — кольцо едет с базой и попадает в `pg_dump`;
   цена — сущность и миграция.
3. `SetApplicationName` в любом варианте: по умолчанию имя берётся из пути сборки.
