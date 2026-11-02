# Старт, память и образ API не настраивались

API публикуется и запускается с настройками .NET по умолчанию: `dotnet publish` без
идентификатора среды, образ `mcr.microsoft.com/dotnet/aspnet:10.0`
(`src/PetTemplate.Api/Dockerfile`), серверный сборщик мусора. Пока ничего из этого не мешает:
контейнер перезапускается только на деплое. Записано, чтобы при первой жалобе на старт,
память или размер образа было с чего начать.

## Native AOT — не делаем

MVC-контроллеры, EF Core на Npgsql, Swashbuckle, Qdrant.Client, SignalR, OpenAI SDK —
рефлексия, под AOT не рассчитаны. Чинить внешние библиотеки ради выигрыша, который
не нужен, не будем; пиковая производительность от AOT не растёт.

## Что рассмотреть вместо него, по отдельности

1. **ReadyToRun** — `<PublishReadyToRun>true</PublishReadyToRun>` и `-r linux-x64`
   в `dotnet publish`: быстрее старт, крупнее публикация.
2. **Сборщик мусора** — `DOTNET_GCHeapHardLimit` в `docker run` или workstation GC:
   на виртуалке с PostgreSQL и Qdrant рядом серверный GC держит кучу на каждое ядро.
3. **Chiseled-образ** — `aspnet:10.0-noble-chiseled`: без оболочки, процесс не от root;
   `docker exec … sh` перестанет работать.

Перенесено из `Lad/tech-debt/runtime-tuning.md` через ASU.
