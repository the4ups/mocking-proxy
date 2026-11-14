# TypedMock: Пошаговый гид, примеры работы и профит для обсуждения с командой

Этот документ подготовлен для демонстрации концепта **TypedMock** коллегам (архитекторам, разработчикам, SDET/QA). В нем собрана суть проблемы, архитектура решения, примеры артефактов на выходе, пошаговые команды для живого демо и аргументы «зачем это нужно проекту».

---

## 1. В чем задача и исходная боль (Why?)

### Как сегодня тестируются микросервисы, зависящие от внешних API:
1. **Прямые вызовы реальных сервисов на тестовых стендах (Integration Env):**
   - **Нестабильность (Flaky):** Внешний стенд упал, перезагружается или сеть моргнула — тесты красные, пайплайн заблокирован.
   - **Невозможность воспроизвести краевые случаи:** Как проверить 500 Internal Error от платежного шлюза, таймаут или редкий статус заказа, если боевой/тестовый API их не отдает по запросу?
   - **Конфликты данных:** Несколько команд или параллельных тестов одновременно меняют одни и те же сущности.

2. **Классический Record-and-Replay (WireMock, Mountebank и сырые JSON-маппинги):**
   - **Проблема Schema Drift (тихое устаревание):** Сторонняя команда обновила API (переименовала поле `userId` -> `accountId`, изменила тип поля). Ваш WireMock продолжает радостно отдавать старый сохраненный JSON. Все тесты **зеленые**, а при выкатке на прод сервис **падает с рантайм-ошибкой**.
   - **Ад поддержки:** Сотни разрозненных JSON-файлов с регулярными выражениями и дублирующимися URL-паттернами. При любом изменении DTO разработчики боятся трогать моки.

3. **In-Memory моки (`Mock<T>`, Fake HttpClient):**
   - Оторваны от реальности: не проверяют реальный HTTP-пайплайн, сериализацию System.Text.Json/Newtonsoft, заголовки, кодировки и роутинг.

---

## 2. Что получилось: Ключевая идея TypedMock

### 💡 Концептуальный сдвиг: «JSON не нужен — сразу C# код»
Традиционные инструменты (WireMock, Mountebank) сохраняют трафик в сотни мёртвых JSON-файлов:
- JSON не проверяется компилятором `dotnet build`.
- При переименовании полей DTO JSON тихо устаревает (Schema Drift).
- JSON невозможно автоматически отрефакторить средствами IDE (`F2 / Rename`).

В **TypedMock** мы отказываемся от промежуточного JSON в пользу **прямой генерации компилируемых C# контроллеров**:
1. **Compile-Time безопасность:** При обновлении контрактов стабы либо компилируются, либо компилятор сразу показывает ошибку (`CS0535` / `CS1061`).
2. **Детерминированный Pattern-Matching:** Входной запрос (Route, Query, Request Body DTO) строго связывается с конкретным ответом прямо в C#-коде (`request switch { { City: "Berlin", TemperatureC: 23 } => ... }`).
3. **Повторные вызовы (Idempotency):** Никаких нестабильных очередей (queues/sequences) — один и тот же запрос всегда детерминированно возвращает один и тот же записанный ответ, выдерживая ретраи (Polly) и параллельные вызовы.
4. **Zero-Dependency хост стабов:** Сгенерированный проект со стабами (`*.Stubs`) — это автономное ASP.NET Core приложение, ссылающееся только на контракты и не зависящее от движка `TypedMock.Core`.
5. **Языковая независимость через OpenAPI / Swagger:** Контракт может поступать не только как C# NuGet, но и как Swagger/OpenAPI спецификация (от сервисов на Go, Java, Python), из которой генерируются типизированные C# DTO и стабы.

```mermaid
flowchart TD
    subgraph Contracts["Контракты (Single Source of Truth)"]
        direction TB
        DotNet["C# NuGet Package\n(RestEase / Refit)"]
        Swagger["OpenAPI / Swagger Spec\n(Language-Agnostic: Go, Java, Python)"]
    end

    subgraph Core["TypedMock Engine (Recorder)"]
        Scanner["Contract Scanner & Route Matcher"]
        Proxy["Record Proxy (Upstream Traffic Interceptor)"]
        CodeGen["C# Stub Generator (Pattern Matching & Value Emitter)"]
    end

    subgraph GeneratedArtifacts["Целевой результат (Zero-JSON, Zero-Dependency)"]
        Controller["Typed C# Stub Controller\n(: ControllerBase, IExternalContract)"]
        Host["In-Memory / Standalone HTTP Host\n(Compile-Time Safe & IDE Refactorable)"]
    end

    Contracts --> Core
    Core --> GeneratedArtifacts
```

### Защита от дрифта и повторяющихся вызовов:
1. **Детерминированное связывание «Вход -> Ответ»:** Входные DTO и параметры запроса превращаются в C# Pattern Matching. Если для данного запроса нет записанного ответа, возвращается `404 Not Found` (`KeyNotFoundException`), а не случайные данные.
2. **Дедупликация:** Повторные запросы с идентичным телом или параметрами не дублируют код, а сливаются в один стабильный case в `switch`.
3. **Compile-Time защита (C# Stubs):** При удалении или переименовании поля в DTO сгенерированные моки немедленно выдают ошибку компиляции на этапе сборки.

---

## 3. Что есть в решении (Компоненты)

Решение собрано на **.NET 10** (`net10.0`), полностью отформатировано и покрыто тестами:

| Проект | Назначение |
|---|---|
| [`src/ExternalService.Contracts`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/ExternalService.Contracts) | Имитация NuGet-пакета внешней команды: интерфейс `IWeatherApi` с аннотациями RestEase (`[Get]`, `[Post]`, `[Path]`, `[Body]`) и DTO-модели. |
| [`src/ExternalService.Stubs`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/ExternalService.Stubs) | Автономный проект стабов: ASP.NET Core приложение с версионируемыми C# контроллерами. Имеет **ноль зависимостей** от `TypedMock.Core`. |
| [`src/TypedMock.Core`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/TypedMock.Core) | Ядро движка записи: обратный прокси, сканер контрактов, матчер роутов, валидатор схем в реальном времени, генератор C# стабов и in-memory тестовый хост (`StubServerHost`). |
| [`src/TypedMock.Server`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/TypedMock.Server) | Standalone CLI-приложение: запуск прокси-рекордера (`typedmock`) и скаффолдинг новых проектов стабов (`typedmock init`). Поддерживает endpoint диагностики `/__typedmock/status`. |
| [`src/SampleExternalService`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/SampleExternalService) | Реальный внешний веб-сервис для записи трафика. |
| [`src/SampleClientApp`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/src/SampleClientApp) | Потребительский микросервис, вызывающий внешнее API через `RestClient.For<IWeatherApi>()`. |
| [`tests/TypedMock.Tests`](file:///c:/Users/serge/RiderProjects/MockingProxyProofOfConcept/tests/TypedMock.Tests) | 44 интеграционных и модульных теста, подтверждающих все сценарии (запись, генерация C# контроллеров, in-memory хостинг, обнаружение дрифта схемы, дедупликация, multi-upstream и скаффолдинг). |

---

## 4. Что мы получаем на выходе (Артефакты)

### Главный артефакт: Сгенерированный C# контроллер-стаб (Zero-JSON подход)
TypedMock переводит записанные HTTP-сессии напрямую в чистый, типизированный C# код с детерминированным pattern-matching:

```csharp
namespace Generated.Stubs;

[ApiController]
public partial class WeatherApiStubController : ControllerBase, IWeatherApi
{
    // 1. GET без параметров: возвращает зафиксированный типизированный список
    [HttpGet("weatherforecast")]
    public virtual async Task<List<WeatherForecastDto>> GetForecastsAsync()
    {
        var custom = await OnGetForecastsAsync();
        if (custom != null) return custom;

        return new List<WeatherForecastDto>
        {
            new WeatherForecastDto { City = "London", TemperatureC = 12, Summary = "Chilly" },
            new WeatherForecastDto { City = "Paris", TemperatureC = 18, Summary = "Mild" }
        };
    }

    // 2. GET с Path-параметром: сопоставление по параметрам маршрута
    [HttpGet("weatherforecast/{city}")]
    public virtual async Task<WeatherForecastDto> GetForecastByCityAsync([FromRoute(Name = "city")] string city)
    {
        var custom = await OnGetForecastByCityAsync(city);
        if (custom != null) return custom;

        return (city?.ToLowerInvariant()) switch
        {
            "london" => new WeatherForecastDto { City = "London", TemperatureC = 12, Summary = "Chilly" },
            "paris" => new WeatherForecastDto { City = "Paris", TemperatureC = 18, Summary = "Mild" },
            _ => throw new KeyNotFoundException($"No recorded stub for city '{city}'.")
        };
    }

    // 3. POST с телом запроса: Property Pattern Matching по входящему DTO
    [HttpPost("weatherforecast")]
    public virtual async Task<WeatherForecastDto> CreateForecastAsync([FromBody] CreateForecastRequest request)
    {
        var custom = await OnCreateForecastAsync(request);
        if (custom != null) return custom;

        return request switch
        {
            { City: "Berlin", TemperatureC: 23, Summary: "Sunny and clear" } =>
                new WeatherForecastDto
                {
                    City = "Berlin",
                    TemperatureC = 23,
                    Summary = "Sunny and clear",
                    Date = DateOnly.Parse("2026-11-11", CultureInfo.InvariantCulture)
                },
            { City: "Rome", TemperatureC: 28, Summary: "Hot" } =>
                new WeatherForecastDto
                {
                    City = "Rome",
                    TemperatureC = 28,
                    Summary = "Hot",
                    Date = DateOnly.Parse("2026-11-11", CultureInfo.InvariantCulture)
                },
            _ => throw new KeyNotFoundException($"No recorded stub for request '{request}'.")
        };
    }

    // Точки расширения для кастомных сценариев и fault injection в автотестах:
    protected virtual Task<WeatherForecastDto?> OnGetForecastByCityAsync(string city) => Task.FromResult<WeatherForecastDto?>(null);
    protected virtual Task<WeatherForecastDto?> OnCreateForecastAsync(CreateForecastRequest request) => Task.FromResult<WeatherForecastDto?>(null);
}
```

### Диагностика стенда в реальном времени
Эндпоинт `GET /__typedmock/status` возвращает текущее состояние:
- Адрес по умолчанию (`defaultTargetUrl`).
- Таблица multi-upstream маршрутизации (`upstreams`).
- Зарегистрированные интерфейсы и конечные точки.
- Число записанных взаимодействий.

---

## 5. Пошаговое руководство: Что запустить и как проверить (Live Demo)

Для демонстрации коллегам достаточно пройти следующие шаги в терминале.

### Шаг 0: Запуск всех тестов
Убеждаемся, что все 44 теста проходят:
```bash
dotnet test
```
*Результат: 44 passed.* Тесты демонстрируют сетевое проксирование, динамическую загрузку сборок в рантайме (`DynamicContractLoader`), Multi-Upstream маршрутизацию, пакетную генерацию контроллеров и скаффолдинг проектов.

---

### Шаг 1: Скаффолдинг проекта стабов (`typedmock init`)
Создаем новый автономный проект стабов:
```bash
dotnet run --project src/TypedMock.Server -- init \
  --name=MyService.Stubs \
  --contracts=src/ExternalService.Contracts/ExternalService.Contracts.csproj \
  --out-dir=./src
```
Команда генерирует готовый проект `MyService.Stubs` с `.csproj`, `Program.cs`, `Dockerfile` и папкой `Controllers/`.

---

### Шаг 2: Запуск живого внешнего сервиса (Upstream)
В первом окне терминала поднимаем образцовый сервис погоды:
```bash
dotnet run --project src/SampleExternalService -- 5001
```
Сервис слушает на `http://localhost:5001`.

---

### Шаг 3: Запуск TypedMock (Запись трафика)
Во втором окне терминала запускаем TypedMock-прокси:
```bash
dotnet run --project src/TypedMock.Server -- \
  --port=5050 \
  --target=http://localhost:5001 \
  --contracts=src/ExternalService.Contracts/bin/Debug/net10.0/ExternalService.Contracts.dll \
  --out-dir=./src/ExternalService.Stubs/Controllers
```
TypedMock слушает на `http://localhost:5050` и прозрачно проксирует запросы на порт `5001`.

Проверяем диагностику:
```bash
curl http://localhost:5050/__typedmock/status
```

---

### Шаг 4: Клиент делает запросы через TypedMock
В третьем окне терминала запускаем клиентское приложение, указав адрес прокси `http://localhost:5050`:
```bash
dotnet run --project src/SampleClientApp -- http://localhost:5050
```
**Что происходит:**
1. Клиент запрашивает данные о погоде (`London`, `Paris` и т.д.).
2. TypedMock перехватывает запрос, сопоставляет его с `IWeatherApi.GetForecastByCityAsync`.
3. Пересылает запрос в настоящий сервис на порту 5001.
4. Получает ответ, **валидирует его через DTO** (защита от runtime schema drift), отдает клиенту и сохраняет взаимодействие.
5. Нажмите `Ctrl+C` во втором терминале — сервер завершит работу и автоматически сгенерирует строго типизированный C# контроллер в `./src/ExternalService.Stubs/Controllers/WeatherApiStubController.cs`.

---

### Шаг 5: Автономный запуск стабов (Replay)
1. Остановите реальный сервис в первом терминале (`Ctrl+C`). Теперь внешнего API нет!
2. Запустите хост стабов напрямую — без `TypedMock.Core`:
```bash
dotnet run --project src/ExternalService.Stubs -- 5050
```
3. Клиентские приложения или автотесты продолжают успешно работать против сгенерированного контроллера!

---

### Шаг 6: Переключение сценариев «на лету» (для QA и тестовых стендов)
Клиент может запросить специфический сценарий с помощью HTTP-заголовка:
```bash
curl -H "X-Mock-Scenario: StormWarning" http://localhost:5050/weatherforecast/London
```
Или через query-параметр:
```bash
curl "http://localhost:5050/weatherforecast/London?__scenario=StormWarning"
```
Хост стабов автоматически помещает имя сценария в `HttpContext.Items["TypedMock.Scenario"]` для обработки в контроллере.

---

### Шаг 7: Демонстрация защиты от Schema Drift
В TypedMock защита от устаревания работает на двух уровнях:
1. **Compile-Time Drift (в сгенерированных C# стабах):**
Если команда сервиса-зависимости изменила свой NuGet-пакет контракта (удалила метод, переименовала свойство в DTO), проект со стабами просто **не скомпилируется** (`CS0535` / `CS1061`). Ошибка выявляется мгновенно на `dotnet build` до любого запуска тестов!
2. **Runtime Drift (в Record-прокси):**
Если внешний сервис вернет ответ, нарушающий типы полей в DTO (например, `temperatureC: "NotANumber"`), прокси немедленно выбросит `ContractValidationException`:
> *"Schema drift detected! Response payload does not match contract DTO WeatherForecastDto."*

---

### Шаг 8: Использование в C# автотестах (In-Memory Stub Hosting)
В тестах контроллеры поднимаются легковесно через `StubServerHost`:

```csharp
[Fact]
public async Task MyIntegrationTest()
{
    // Поднимаем сгенерированный стаб-контроллер в памяти:
    await using var stubHost = await StubServerHost.StartAsync<WeatherApiStubController>(port: 0);
    var url = StubServerHost.GetServerUrl(stubHost);

    // Тестируемый сервис указывает на адрес стаба:
    var client = RestClient.For<IWeatherApi>(url);
    var result = await client.GetForecastByCityAsync("London");

    result.City.Should().Be("London");
    result.TemperatureC.Should().Be(12);
}
```

Для нестандартных сценариев достаточно переопределить virtual-хук:
```csharp
[ApiController]
public class ChaosWeatherStubController : WeatherApiStubController
{
    protected override Task<WeatherForecastDto?> OnGetForecastByCityAsync(string city)
    {
        if (city == "ErrorCity")
        {
            throw new HttpRequestException("Station down", null, HttpStatusCode.ServiceUnavailable);
        }
        return base.OnGetForecastByCityAsync(city);
    }
}
```

---

## 6. В чем профит для команды (Аргументы для обсуждения)

### Сравнительный анализ подходов

| Критерий | Тестирование на живых стендах | WireMock / Mountebank | TypedMock (Наше решение) |
|---|---|---|---|
| **Зависимость от сети / других команд** | Высокая (падения, нестабильность) | Нет (локально) | Нет (локально) |
| **Скорость выполнения тестов** | Медленно (сетевые задержки стендов) | Быстро | Максимально быстро (Kestrel in-process) |
| **Защита от изменения схемы (Schema Drift)** | ❌ Нет (узнаем только при падении) | ❌ Нет (тихий рассинхрон с JSON) |  **Да (Runtime DTO validation + Compile-time stubs)** |
| **Поддержка при рефакторинге DTO** | Ручной поиск поломок | Ручная правка сотен JSON файлов |  **IDE / компилятор подсказывают ошибки** |
| **Автономность стендов** | Привязка к внешнему стенду | Нужен WireMock-сервер |  **Чистый ASP.NET Core хост без сторонних зависимостей** |
| **Порог входа для .NET разработчика** | Низкий | Средний (учить синтаксис маппингов) |  **Нулевой (привычный C#, RestEase и DTO)** |

---

## 7. Вопросы, которые могут задать коллеги (FAQ)

### Вопрос 1: «А если у нас не RestEase, а Refit или HttpClient?»
- `TypedMock.Core` построен модульно. Сканер интерфейсов (`RestEaseContractScanner`) легко расширяется на Refit или ASP.NET Core интерфейсы.

### Вопрос 2: «Как это деплоить на общие тестовые стенды?»
- Сгенерированный проект со стабами собирается в стандартный Docker-образ (`docker build -t external-stubs -f src/ExternalService.Stubs/Dockerfile .`).
- На стенде мок-контейнер разворачивается рядом с сервисом.
- Тест-раннеры при необходимости передают заголовок `X-Mock-Scenario: <Name>` в запросах.

### Вопрос 3: «Зачем два отдельных сервера — рекордер и хост стабов?»
- **Рекордер (`TypedMock.Server`)** — это инструмент разработки (CLI). Ему нужны тяжелые механизмы: сканирование сборок, реверс-проксирование, валидация DTO, генерация исходного кода.
- **Хост стабов (`ExternalService.Stubs`)** — это легковесный, простой ASP.NET Core сервис без внешних библиотек. Он быстро стартует, потребляет минимум ресурсов и дает 100% гарантию сборки в CI/CD.

---

## 8. Итог

Proof-of-Concept доказывает, что в .NET-микросервисах можно избавиться от головной боли с ручными JSON-моками и нестабильными внешними зависимостями, объединив **проксирование реального трафика** со **строгой типизацией C# контрактов** и **автономными сгенерированными стабами**.
