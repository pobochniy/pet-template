# Схема базы — копией на каждый тест

Сделано в `tests/PetTemplate.Tests/Containers/PostgresServer.cs`:

- один контейнер `postgres:17` на прогон, данные в tmpfs, `fsync=off`, `synchronous_commit=off`,
  `full_page_writes=off` — аналог «MySQL в tmpfs без fsync» у Lad и ASU;
- схема раскатывается один раз — в шаблонную базу `pettemplate_template` тем же приложением,
  что и в тестах (`EnsureCreated`, после пересборки миграции `Init` под PostgreSQL — `Migrate()`);
- у каждого теста своя база — `CREATE DATABASE … TEMPLATE pettemplate_template`: копия файлов,
  а не DDL. После теста её дропает `TestHosts` (`DROP DATABASE … WITH (FORCE)`).

Замер на 2026-10-31: 43 теста — 4 с (macOS, Docker Desktop), вместе со стартом контейнера.

## Решение на потом

Копия стоит пропорционально размеру шаблона, а не числу таблиц, поэтому заболеть должно нескоро.
Если заболит — пул баз со схемой вместо базы на тест: после теста чистка данных
`TRUNCATE … RESTART IDENTITY CASCADE` по всем таблицам (в Postgres это быстро и транзакционно).
Готовая обвязка — Respawn (умеет Postgres). Помнить: `HasData`-сид чистка сносит.

Перенесено из `Lad/tech-debt/test-schema-per-test.md` через ASU; MySQL → PostgreSQL.
