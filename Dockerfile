# 1. Сборка на .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Копируем файлы проектов для кэширования nuget-пакетов
COPY ["src/HackBack.Domain/HackBack.Domain.csproj", "src/HackBack.Domain/"]
COPY ["src/HackBack.Infrastructure/HackBack.Infrastructure.csproj", "src/HackBack.Infrastructure/"]
COPY ["src/HackBack.Api/HackBack.Api.csproj", "src/HackBack.Api/"]

# Восстанавливаем зависимости
RUN dotnet restore "src/HackBack.Api/HackBack.Api.csproj"

# Копируем исходный код
COPY src/ src/

# Собираем и публикуем
WORKDIR "/src/src/HackBack.Api"
RUN dotnet publish "HackBack.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Финальный образ .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5170
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 5170

ENTRYPOINT ["dotnet", "HackBack.Api.dll"]