# Используется в GitHub Actions (publish-api.yml): сборка и тесты идут там, сюда
# скидываются готовые файлы публикации — сборки внутри образа нет (спец. infrastructure, 6).
# Рантайм — той же мажорной версии, что TargetFramework: публикация под net10.0
# на aspnet:9.0 не стартует (You must install or update .NET)

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .

# GET /health — база и Qdrant (спец. infrastructure, 6). curl и wget в aspnet:10.0 нет,
# ставить их ради проверки незачем: bash в образе есть, и /dev/tcp у него свой. Выкатка
# ждёт не этот статус, а сама спрашивает 127.0.0.1:5000/health (IaC/run-api.sh); этот
# видно в `docker ps` и в метриках cAdvisor
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]

# Не от root: пользователь `app` (APP_UID, 1654) есть в образе .NET с версии 8. Писать API
# на диск нечего — ключи Data Protection в базе (спец. infrastructure, 3.1), справка смонтирована
# только на чтение, порт 8080 не привилегированный. Уязвимость в API — процесс без root в контейнере
USER $APP_UID

ENTRYPOINT [ "dotnet", "PetTemplate.Api.dll" ]
