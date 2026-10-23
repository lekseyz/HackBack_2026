FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файл солюшена и csproj файлы для кэширования nuget-пакетов
COPY ["HackBack_2026.slnx", "./"]
COPY ["src/HackBack.Domain/HackBack.Domain.csproj", "src/HackBack.Domain/"]
COPY ["src/HackBack.Infrastructure/HackBack.Infrastructure.csproj", "src/HackBack.Infrastructure/"]
COPY ["src/HackBack.Api/HackBack.Api.csproj", "src/HackBack.Api/"]

# Восстанавливаем пакеты для всего солюшена сразу
RUN dotnet restore "HackBack_2026.slnx"

# Копируем остальной исходный код
COPY src/ src/

# Публикуем конкретный API проект
WORKDIR "/src/src/HackBack.Api"
RUN dotnet publish "HackBack.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5170
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 5170

ENTRYPOINT ["dotnet", "HackBack.Api.dll"]