# Используется в git hub actions, сборка там, сюда скидываются готовые файлы

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR publishfolder
COPY . .

ENTRYPOINT [ "dotnet", "PetTemplate.Api.dll" ]