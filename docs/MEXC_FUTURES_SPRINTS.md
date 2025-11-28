# MEXC FUTURES INTEGRATION - SPRINT PLAN

**Цель:** Добавить поддержку фьючерсов MEXC с сохранением возможности работы со спотом

**Статус:** Планирование
**Дата создания:** 2025-11-28
**Принципы:** GEMINI_DEV.md (Evidence-Based, Sequential Thinking, Simple Architecture)

---

## SPRINT 0: Исследование и валидация

**Цель:** Подтвердить техническую возможность и проверить версию библиотеки

### Задачи:

#### 0.1 Проверка версии Mexc.Net
- [ ] Найти `.csproj` файл проекта Infrastructure
- [ ] Проверить версию пакета `JK.Mexc.Net` (требуется >= 3.4.0 для FuturesApi)
- [ ] Обновить до последней версии, если требуется

**Критерий успеха:** Mexc.Net >= 3.4.0 установлен

#### 0.2 Исследование FuturesApi
- [ ] Создать тестовый файл для проверки доступных API:
  ```csharp
  var client = new MexcRestClient();
  var futuresSymbols = await client.FuturesApi.ExchangeData.GetSymbolsAsync();
  ```
- [ ] Проверить структуру данных:
  - Формат символов (BTCUSDT, BTC_USDT, BTC-USDT?)
  - Формат тикеров (Volume24h, PriceChange)
  - Структура WebSocket событий

**Критерий успеха:** Понимание структуры FuturesApi, тестовый запрос успешен

#### 0.3 Анализ различий Spot vs Futures
- [ ] Составить таблицу различий:
  - Названия символов
  - Поля тикеров
  - Параметры подписки WebSocket
  - Ограничения (chunk size, rate limits)

**Критерий успеха:** Документация различий создана

**Оценка:** 2-3 часа
**Риски:**
- Версия библиотеки может быть устаревшей
- FuturesApi может иметь ограничения, не указанные в документации

---

## SPRINT 1: Создание MexcFuturesExchangeClient

**Цель:** Реализовать класс для работы с фьючерсами

### Задачи:

#### 1.1 Создание базовой структуры
- [ ] Создать файл `MexcFuturesExchangeClient.cs` в `Infrastructure/Services/Exchanges/`
- [ ] Скопировать структуру из `MexcExchangeClient.cs`
- [ ] Переименовать класс и namespace
- [ ] Изменить `ExchangeName => "MexcFutures"`

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/MexcFuturesExchangeClient.cs`

```csharp
namespace SpreadAggregator.Infrastructure.Services.Exchanges;

public class MexcFuturesExchangeClient : ExchangeClientBase<MexcRestClient, MexcSocketClient>
{
    public override string ExchangeName => "MexcFutures";
    protected override int ChunkSize => 6; // Может отличаться для Futures
    protected override bool SupportsTradesStream => true;

    protected override MexcRestClient CreateRestClient() => new();
    protected override MexcSocketClient CreateSocketClient() => new();

    // ... остальные методы
}
```

**Критерий успеха:** Класс создан, компилируется

#### 1.2 Реализация GetSymbolsAsync()
- [ ] Заменить `SpotApi` на `FuturesApi`:
  ```csharp
  var symbolsData = await _restClient.FuturesApi.ExchangeData.GetSymbolsAsync();
  ```
- [ ] Проверить формат возвращаемых данных
- [ ] Адаптировать маппинг в `SymbolInfo` (PriceStep, QuantityStep)

**Критерий успеха:** Метод возвращает список фьючерсных символов

#### 1.3 Реализация GetTickersAsync()
- [ ] Заменить на FuturesApi:
  ```csharp
  var tickersResult = await _restClient.FuturesApi.ExchangeData.GetTickersAsync();
  ```
- [ ] Проверить формат `PriceChange` (процент или decimal)
- [ ] Адаптировать маппинг `Volume24h`, `PriceChangePercent24h`

**Критерий успеха:** Метод возвращает тикеры для фьючерсов

#### 1.4 Создание MexcFuturesSocketApiAdapter
- [ ] Создать внутренний класс `MexcFuturesSocketApiAdapter`
- [ ] Реализовать `IExchangeSocketApi`
- [ ] Заменить `IMexcSocketClientSpotApi` на Futures API:
  ```csharp
  private readonly IMexcSocketClientFuturesApi _futuresApi;

  public MexcFuturesSocketApiAdapter(IMexcSocketClientFuturesApi futuresApi)
  {
      _futuresApi = futuresApi;
  }
  ```
- [ ] Реализовать `SubscribeToTradeUpdatesAsync` для фьючерсов
- [ ] Проверить параметры (interval, формат данных)

**Критерий успеха:** WebSocket подписка на фьючерсы работает

#### 1.5 Реализация GetOrderbookForSymbolsAsync()
- [ ] Заменить на FuturesApi:
  ```csharp
  var orderbookResult = await _restClient.FuturesApi.ExchangeData.GetOrderBookAsync(symbol, 5);
  ```
- [ ] Проверить формат ответа (Bids/Asks)
- [ ] Сохранить логику задержки (await Task.Delay(10))

**Критерий успеха:** Метод возвращает orderbook для фьючерсов

**Оценка:** 4-5 часов
**Риски:**
- Формат данных FuturesApi может существенно отличаться от SpotApi
- ChunkSize для Futures может быть другим (нужно тестирование)

---

## SPRINT 2: Интеграция в DI и конфигурацию

**Цель:** Зарегистрировать новый клиент и настроить управление биржами

### Задачи:

#### 2.1 Регистрация в Program.cs
- [ ] Открыть `Program.cs:96`
- [ ] Добавить регистрацию MexcFuturesExchangeClient:
  ```csharp
  // Register all exchange clients
  // MEXC SPOT: Spot market (можно отключить в appsettings.json)
  services.AddSingleton<IExchangeClient, MexcExchangeClient>();

  // MEXC FUTURES: Futures market (можно отключить в appsettings.json)
  services.AddSingleton<IExchangeClient, MexcFuturesExchangeClient>();
  ```

**Критерий успеха:** Оба клиента зарегистрированы в DI

#### 2.2 Конфигурация appsettings.json
- [ ] Открыть `appsettings.json:27-35`
- [ ] Добавить секцию для MexcFutures:
  ```json
  "ExchangeSettings": {
    "Exchanges": {
      "Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      },
      "MexcFutures": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      }
    }
  }
  ```

**Критерий успеха:** Конфигурация позволяет управлять обеими биржами

#### 2.3 Документация управления
- [ ] Создать `docs/EXCHANGE_MANAGEMENT.md` с инструкциями:
  - Как отключить спот (закомментировать "Mexc")
  - Как подключить фьючерсы (добавить "MexcFutures")
  - Как использовать оба одновременно
  - Примеры конфигураций для разных сценариев

**Критерий успеха:** Документация создана и понятна

#### 2.4 Проверка OrchestrationService
- [ ] Убедиться, что `OrchestrationService.cs:96-106` корректно загружает обе биржи
- [ ] Проверить, что BinanceSpotFilter применяется только к "Mexc" (не к "MexcFutures")
- [ ] Проверить, что orderbook refresh (строки 236-307) работает для обеих бирж

**Критерий успеха:** OrchestrationService поддерживает обе биржи без изменений

**Оценка:** 2-3 часа
**Риски:**
- BinanceSpotFilter может некорректно применяться к фьючерсам
- Могут потребоваться отдельные настройки для Futures (другие лимиты volume)

---

## SPRINT 3: Тестирование и валидация

**Цель:** Убедиться, что система работает корректно во всех режимах

### Задачи:

#### 3.1 Unit тесты
- [ ] Создать `MexcFuturesExchangeClientTests.cs`
- [ ] Тесты:
  - GetSymbolsAsync возвращает фьючерсы
  - GetTickersAsync возвращает корректные данные
  - GetOrderbookForSymbolsAsync работает
  - ExchangeName == "MexcFutures"

**Критерий успеха:** Все unit тесты проходят

#### 3.2 Интеграционное тестирование - Только Spot
- [ ] Конфигурация: только `"Mexc"` в appsettings.json
- [ ] Запустить приложение
- [ ] Проверить:
  - WebSocket подключается
  - Трейды получаются
  - Frontend отображает данные
  - Логи не содержат ошибок

**Критерий успеха:** Спот работает как раньше, без регрессий

#### 3.3 Интеграционное тестирование - Только Futures
- [ ] Конфигурация: только `"MexcFutures"` в appsettings.json
- [ ] Запустить приложение
- [ ] Проверить:
  - WebSocket подключается к фьючерсам
  - Трейды получаются
  - Символы отображаются корректно (BTC-USDT vs BTCUSDT)
  - Frontend работает без ошибок

**Критерий успеха:** Фьючерсы работают корректно

#### 3.4 Интеграционное тестирование - Spot + Futures
- [ ] Конфигурация: обе секции в appsettings.json
- [ ] Запустить приложение
- [ ] Проверить:
  - Оба WebSocket подключения активны
  - Трейды приходят от обеих бирж
  - Символы не дублируются в UI
  - Нет конфликтов в данных
  - Логи показывают обе биржи активными

**Критерий успеха:** Обе биржи работают одновременно без конфликтов

#### 3.5 Нагрузочное тестирование
- [ ] Измерить метрики GEMINI_DEV.md:
  - CPU usage (должен быть низким)
  - RAM usage
  - Network traffic (должен быть -98% через aggregation)
  - Latency (~200ms)
- [ ] Сравнить с метриками только Spot режима
- [ ] Убедиться, что добавление Futures не ухудшает производительность

**Критерий успеха:** Performance Standards соблюдены (GEMINI_DEV.md раздел 3)

#### 3.6 Health Check тестирование
- [ ] Проверить endpoint `/health`:
  - Только Mexc: должен показывать 1 биржу
  - Только MexcFutures: должен показывать 1 биржу
  - Обе: должен показывать 2 биржи
- [ ] Проверить статусы: running, stopped, failed

**Критерий успеха:** Health checks корректны для всех режимов

**Оценка:** 3-4 часа
**Риски:**
- Могут быть конфликты при одновременной работе Spot + Futures
- Performance может деградировать при двойной нагрузке

---

## SPRINT 4: Документация и финализация

**Цель:** Задокументировать изменения и обновить документацию проекта

### Задачи:

#### 4.1 Обновление ARCHITECTURE.md
- [ ] Добавить раздел "Exchange Support":
  - MEXC Spot (MexcExchangeClient)
  - MEXC Futures (MexcFuturesExchangeClient)
  - Принцип работы ExchangeClientBase
- [ ] Добавить диаграмму архитектуры (опционально)

**Критерий успеха:** Архитектура задокументирована

#### 4.2 Создание EXCHANGE_MANAGEMENT.md
- [ ] Инструкции по управлению биржами
- [ ] Примеры конфигураций:
  ```json
  // Только Spot
  "Exchanges": { "Mexc": { ... } }

  // Только Futures
  "Exchanges": { "MexcFutures": { ... } }

  // Оба
  "Exchanges": { "Mexc": { ... }, "MexcFutures": { ... } }
  ```
- [ ] Troubleshooting: частые проблемы и решения

**Критерий успеха:** Инструкции понятны для нового разработчика

#### 4.3 Обновление CHANGELOG.md
- [ ] Создать файл, если не существует
- [ ] Добавить запись:
  ```markdown
  ## [MEXC Futures Support] - 2025-11-28

  ### Added
  - MexcFuturesExchangeClient для поддержки фьючерсов
  - Возможность управления Spot/Futures через appsettings.json
  - Одновременная работа Spot + Futures

  ### Changed
  - Program.cs: регистрация MexcFuturesExchangeClient
  - appsettings.json: добавлена секция MexcFutures

  ### Technical Details
  - Использует FuturesApi из Mexc.Net (>= 3.4.0)
  - Архитектура: наследование от ExchangeClientBase
  - Performance: сохранены стандарты GEMINI_DEV.md
  ```

**Критерий успеха:** CHANGELOG обновлен

#### 4.4 Обновление GEMINI_DEV.md (опционально)
- [ ] Добавить пример использования multiple exchanges
- [ ] Обновить раздел "System Architecture" (2.1):
  ```markdown
  ### 2.1 Backend (ASP.NET Core)
  - **OrchestrationService:** Manages exchange subscriptions (Spot, Futures)
  - **ExchangeClientBase:** Base class for exchange implementations
    - MexcExchangeClient (Spot)
    - MexcFuturesExchangeClient (Futures)
  ```

**Критерий успеха:** GEMINI_DEV.md отражает новую архитектуру

#### 4.5 README в Frontend (index.html)
- [ ] Обновить комментарий в `index.html:7`:
  ```html
  <title>MEXC Trade Screener Pro (Spot & Futures)</title>
  ```
- [ ] Добавить индикатор биржи в UI (опционально):
  - Показывать [SPOT] или [FUTURES] рядом с символом
  - Добавить фильтр по типу биржи

**Критерий успеха:** Frontend отражает поддержку обеих бирж

#### 4.6 Code Review Checklist
- [ ] Проверить соответствие GEMINI_DEV.md:
  - Evidence-Based Development (тесты есть)
  - Measure First (performance измерен)
  - No Over-Engineering (простое решение)
  - Server-Side Aggregation (не сломан)
- [ ] Проверить code style (похож на MexcExchangeClient)
- [ ] Проверить отсутствие дублирования кода
- [ ] Проверить обработку ошибок (fail fast)

**Критерий успеха:** Code review пройден

**Оценка:** 2-3 часа
**Риски:** Нет критических рисков

---

## ИТОГОВАЯ ТАБЛИЦА

| Sprint | Задача | Оценка | Статус |
|--------|--------|--------|--------|
| 0 | Исследование и валидация | 2-3 ч | ⏳ Не начато |
| 1 | Создание MexcFuturesExchangeClient | 4-5 ч | ⏳ Не начато |
| 2 | Интеграция в DI и конфигурацию | 2-3 ч | ⏳ Не начато |
| 3 | Тестирование и валидация | 3-4 ч | ⏳ Не начато |
| 4 | Документация и финализация | 2-3 ч | ⏳ Не начато |

**ИТОГО:** 13-18 часов чистого времени разработки

---

## КРИТЕРИИ ПРИЕМКИ (Definition of Done)

✅ MexcFuturesExchangeClient создан и работает
✅ Возможность отключить Spot через appsettings.json
✅ Возможность подключить Futures через appsettings.json
✅ Возможность работать с обеими биржами одновременно
✅ Все тесты проходят (unit + integration)
✅ Performance стандарты соблюдены (GEMINI_DEV.md)
✅ Документация обновлена (ARCHITECTURE, CHANGELOG, EXCHANGE_MANAGEMENT)
✅ Code review пройден
✅ Нет регрессий в существующем функционале Spot

---

## РИСКИ И МИТИГАЦИЯ

| Риск | Вероятность | Воздействие | Митигация |
|------|-------------|-------------|-----------|
| Mexc.Net < 3.4.0 | Средняя | Высокое | Sprint 0: проверка версии, обновление |
| FuturesApi формат данных отличается | Высокая | Среднее | Sprint 0: исследование структуры |
| Конфликты при Spot+Futures | Низкая | Среднее | Sprint 3: тщательное тестирование |
| Performance деградация | Низкая | Высокое | Sprint 3: нагрузочное тестирование |
| BinanceSpotFilter конфликтует | Средняя | Низкое | Sprint 2: проверка логики фильтрации |

---

## СЛЕДУЮЩИЕ ШАГИ

1. **Обсудить спринты** с командой/владельцем продукта
2. **Утвердить приоритеты** (все спринты или по частям?)
3. **Начать Sprint 0** после утверждения
4. **Еженедельные ревью** прогресса по спринтам

---

**Автор:** Claude (GEMINI_DEV роль)
**Дата:** 2025-11-28
**Версия:** 1.0
