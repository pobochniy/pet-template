# Пакеты с известными уязвимостями

`dotnet restore` предупреждает (NU1902/NU1903) о пакетах с опубликованными уязвимостями.

| Пакет | Версия | Уровень | Откуда |
|---|---|---|---|
| `SSH.NET` | 2025.1.0 | high | транзитивно от `Testcontainers` 4.13.0, только в `PetTemplate.Tests`; уйдёт с новой версией Testcontainers |

Подняты вместе с телеметрией (`specs/infrastructure.md`, 10):

- `OpenTelemetry*` 1.15.0 → 1.20.1 — одна линейка; уязвимость `OpenTelemetry.Api` ушла.
- `Microsoft.OpenApi` 2.4.1 → 2.7.5 — исправление GHSA-v5pm-xwqc-g5wc в ветке 2.x. На 3.x
  не переходили: Swashbuckle 10 собран под 2.x; `/swagger/v1/swagger.json` отдаётся.

Что сделать: поднять `Testcontainers*` до версии, которая тянет `SSH.NET` без уязвимостей,
после — `dotnet build` без NU190x.
