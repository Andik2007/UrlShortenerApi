# UrlShortenerApi
<img width="1561" height="614" alt="image" src="https://github.com/user-attachments/assets/2789428b-2fac-4097-8579-3ce7d1bd620a" />

# URL Shortener & Analytics API

REST API сервис для сокращения ссылок с поддержкой кастомных алиасов, времени жизни (TTL), кэширования и детальной аналитики переходов.

## Стек технологий
* **Платформа:** C#, .NET (ASP.NET Core Minimal API)
* **База данных и ORM:** PostgreSQL, Entity Framework Core
* **Кэширование:** In-Memory Cache (`IMemoryCache`)
* **Документация:** Swagger / OpenAPI

## Основной функционал
* `POST /api/shorten` — генерация короткого кода (Base62) или создание кастомного алиаса с защитой от коллизий и настройкой времени жизни ссылки.
* `GET /{code}` — быстрый HTTP-редирект с использованием оперативного кэша и логированием метаданных перехода (`User-Agent`, время клика).
* `GET /api/stats/{code}` — агрегированная статистика переходов и история последних кликов.
* `DELETE /api/{code}` — удаление ссылки и инвалидация кэша.
