# 1. Сборка
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Включаем поддержку формата .slnx (на случай, если SDK 8.0 его распознает)
ENV DOTNET_CLI_ENABLE_EXPERIMENTAL_SLNX=true

# Копируем csproj-файлы проектов для кэширования слоев NuGet
COPY ["src/HackBack.Domain/HackBack.Domain.csproj", "src/HackBack.Domain/"]
COPY ["src/HackBack.Infrastructure/HackBack.Infrastructure.csproj", "src/HackBack.Infrastructure/"]
COPY ["src/HackBack.Api/HackBack.Api.csproj", "src/HackBack.Api/"]

# Восстанавливаем зависимости напрямую для API проекта (он сам подтянет ссылки)
RUN dotnet restore "src/HackBack.Api/HackBack.Api.csproj"

# Копируем исходный код
COPY src/ src/

# Собираем и публикуем
WORKDIR "/src/src/HackBack.Api"
RUN dotnet publish "HackBack.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Финальный образ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5170
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 5170

ENTRYPOINT ["dotnet", "HackBack.Api.dll"]