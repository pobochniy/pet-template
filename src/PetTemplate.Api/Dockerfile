# Используется в GitHub Actions (publish-api.yml): сборка и тесты идут там, сюда
# скидываются готовые файлы публикации — сборки внутри образа нет (спец. infrastructure, 6).
# Поэтому restore с закрытым фидом pobhub — тоже на раннере, с NUGET_GITHUB_TOKEN
# в переменных шага: ни в образ, ни в аргументы docker build токен не попадает.
# Рантайм — той же мажорной версии, что TargetFramework: публикация под net10.0
# на aspnet:9.0 не стартует (You must install or update .NET)

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY . .

ENTRYPOINT [ "dotnet", "PetTemplate.Api.dll" ]
