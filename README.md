# HackBack 2026

Бэкенд для HackBack 2026: churn-анализатор рисков оттока — реальный объяснимый скоринг клиентов, курсорная пагинация, настройки модели, аналитика, SignalR. .NET 10 + EF Core 10 + SQLite.

## Стек

.NET 10 · EF Core 10 (SQLite) · JWT Bearer + refresh · SignalR · Scalar (OpenAPI) · Docker

## Запуск

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'   # appsettings.Development.json окружение просто норм сконфигурировано - используем его
dotnet run --project src/HackBack.Api
```

Открыть: `http://localhost:5170` (или `https://localhost:7081`).

Dev-доступ: `admin@hack.local` / `admin123` (сидируется при пустой таблице Users).

## Команды

```bash
dotnet build HackBack_2026.slnx -warnaserror   # порог приёмки: 0 ошибок, 0 предупреждений
dotnet ef migrations add <Name> --project src/HackBack.Infrastructure --startup-project src/HackBack.Api   # из каталога репо (локальный dotnet-ef из dotnet-tools.json)
```

Миграции применяются при старте (`Database.MigrateAsync()`). Удаление `hackback.db` (+`-wal`/`-shm`) безопасно — пересоздаётся.

## Загрузка тестовых данных

```powershell
Invoke-RestMethod -Method Put -Uri http://localhost:5170/api/v1/source/snapshot `
  -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -InFile data/data.json
```

`token` — из ответа `POST /api/v1/auth/login`. Генерация своего датасета: `python3 data/generate.py --accounts 100 --services 300 --transactions 1000 --seed 42 --reference 2026-11-01T00:00:00.000Z --out ./generated`.

## Как устроено

- `src/HackBack.Domain/` — сущности и доменные сервисы (`RiskScorer` — аддитивный объяснимый скоринг 5 факторов, `PasswordHasher`)
- `src/HackBack.Infrastructure/` — `AppDbContext`, репозитории, `Migrations/`
- `src/HackBack.Api/` — `Program.cs`, `*_Endpoints.cs`, `JwtService.cs`, фоновый `MetricsRecalculationService` (пересчёт скор+ persist-проекций, триггеры: заливка снапшота / PATCH настроек), `AnalyticsHub` (SignalR `/hubs/analytics`)

## Эндпоинты

`auth/login|register|me|logout|refresh` · `PUT /source/snapshot` · `GET /customers` (курсорная пагинация, сортировка, поиск, фильтр по риску) · `GET /customers/{id}` · `POST|PATCH|DELETE /services` · `GET|PATCH /model/settings` · `GET /analytics/summary` · `GET /metrics` (Prometheus, без auth) · docs: `/api/v1/docs` (Scalar) + `/openapi/v1.json`. Всё под Bearer, кроме `login`, `register`, `docs`, `/metrics`.

Realtime: `ModelRecalculated`, `CustomerRiskUpdated` — контракт в `SIGNALR.md`.

## Конфигурация

Всё в `src/HackBack.Api/appsettings.Development.json`:

- `ConnectionStrings:Sqlite` — строка подключения (в Docker переопределяется на `/app/data`)
- `Jwt:Key/Issuer/Audience/ExpiresMinutes` — access-токен (по умолчанию 60 минут)
- `Refresh:ExpiresDays` — срок жизни refresh-токена в БД (кука — session)

## Деплой

Docker + GitHub Actions: push в `main` → build-check → SSH на сервер → сборка образа → контейнер с TLS (Let's Encrypt) на `https://hack-analyzer.ddns.net:3000`, SQLite в volume `hackback_data`. Фронт — соседний репозиторий `HackFront2026` (свой пайплайн, nginx на 443). Детали — в `AGENTS.md`, раздел «Деплой».

## Ловушки

- `dotnet ef` и package-команды запускать из каталога репо — иначе подхватится глобальный NuGet-конфиг с недоступным источником (см. `NuGet.Config`).
- Кириллица в PowerShell может рендериться как `???` — артефакт кодовой страницы, проверяйте байты.
- После ручных проверок убивать процесс по порту (`Get-NetTCPConnection -LocalPort <port> | ... Stop-Process`).
