# 1. Сборка на .NET 10 (GA)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Конфиг NuGet из репо — детерминированные источники пакетов
COPY NuGet.Config .

# Копируем файлы проектов для кэширования nuget-пакетов
COPY ["src/HackBack.Domain/HackBack.Domain.csproj", "src/HackBack.Domain/"]
COPY ["src/HackBack.Infrastructure/HackBack.Infrastructure.csproj", "src/HackBack.Infrastructure/"]
COPY ["src/HackBack.Api/HackBack.Api.csproj", "src/HackBack.Api/"]

# Восстанавливаем зависимости
RUN dotnet restore "src/HackBack.Api/HackBack.Api.csproj"

# Копируем исходный код (bin/obj/.git исключены .dockerignore)
COPY src/ src/

# Собираем и публикуем
RUN dotnet publish "src/HackBack.Api/HackBack.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Финальный образ .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Каталог для SQLite вне слоёв контейнера — сюда монтируется volume
RUN mkdir -p /app/data

ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=https://+:443
ENV ConnectionStrings__Sqlite="Data Source=/app/data/hackback.db"
# PEM-сертификат Let's Encrypt монтируется из хоста (см. deploy.yml)
ENV Kestrel__Certificates__Default__Path=/etc/letsencrypt/live/hack-analyzer.ddns.net/fullchain.pem
ENV Kestrel__Certificates__Default__KeyPath=/etc/letsencrypt/live/hack-analyzer.ddns.net/privkey.pem
EXPOSE 443

ENTRYPOINT ["dotnet", "HackBack.Api.dll"]
