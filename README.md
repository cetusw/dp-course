# Задание 1

## Контекст

**Valuator** - приложение помощник редактора.

Пользователь с помощью формы на главной странице отправляет текст на обработку, после чего перенаправляется на страницу *summary*, где видит результат обработки.

Приложение предоставляет следующие функции:

1. оценивает содержание;
2. проверяет похожесть на другие тексты.

Результат оценки содержания - число *rank* в диапазоне [0..1], равное доле неалфавитных символов в тексте.
Алфавитными считаются символы строчных и прописных букв латинского и русского алфавитов.

Проверка на похожесть делается на основе поиска дубликата текста среди ранее обработанных.
Если найден дубликат, то *similarity* = 1, иначе 0.

## Задание

Первое задание является ознакомительным. Прежде всего нужно ознакомиться с используемыми технологиями и инструментами:

* научиться создавать и запускать Web-приложение на фреймворке Asp.Net Core Pages,
* подключать к проекту Nuget библиотеки,
* запускать пошаговую отладку приложения в среде разработки.

В предоставленном шаблоне приложения необходимо:
1. Указать ваше имя и группу на странице About
2. Дописать недостающий код  *(отмечен комментарием `// TODO: (pa1)`)*
3. В качестве хранилища использовать key-value хранилище Redis.

# Материалы

## Инструменты разработки и программные компоненты

1. [ASP.NET Core 8.0](https://learn.microsoft.com/en-us/aspnet/core/getting-started/?view=aspnetcore-8.0)
2. NoSQL база данных [Redis](https://redis.io/) (официальный docker-образ: [redis](https://hub.docker.com/_/redis))
3. Рекомендуемые IDE: [VS Code](https://code.visualstudio.com/), Rider, Visual Studio

## Статьи

_Это лишь рекомендации, подходящие материалы следует искать самостоятельно_

### Статьи по C#

- [Learn C# in Y minutes](https://learnxinyminutes.com/csharp/)
- Шпаргалки: 1) [Шпаргалка по C#](https://high.tealeaf.su/about-csharp.html); 2) [C# cheatsheet](https://reference-xi.vercel.app/cs.html); 3) [C# Cheatsheet (github.com)](https://github.com/jwill9999/C-Sharp-Cheatsheet)
- [Roadmap for JavaScript and TypeScript developers learning C#](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/tips-for-javascript-developers)
- [Roadmap for Java developers learning C#](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/tips-for-java-developers)
- [Common C# code conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)

### Статьи по Redis

- [Redis - Docker](https://www.w3schools.io/nosql/redis-docker-setup/)
- [Run Redis with Docker Compose](https://kb.objectrocket.com/redis/run-redis-with-docker-compose-1055)
- [NRedisStack guide (C#/.NET)](https://redis.ranebull.me/docs/latest/develop/clients/dotnet/)
- [C#/.NET guide](https://master--redis-doc.netlify.app/docs/connect/clients/dotnet/)
- [Redis as Primary Database in .NET 8 Web API](https://www.csharp.com/article/redis-as-primary-database-in-net-8-web-ap/)
- [Using StackExchangeRedis to integrate Redis with a C# .NET app](https://duongnt.com/stackexchangeredis/)
