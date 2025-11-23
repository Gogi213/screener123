# Build Issues - Changelog

## MSB3552: Resource file "**/*.resx" cannot be found

**Дата:** 2025-11-22  
**Статус:** ✅ **РЕШЕНО**

### Симптомы

```
error MSB3552: Resource file "**/*.resx" cannot be found.
/home/giorgioisitalii/.dotnet/sdk/9.0.308/Microsoft.Common.CurrentVersion.targets(3455,5)
```

Build падал для `SpreadAggregator.Presentation.csproj`, но успешно проходил для других проектов (Domain, Application, Infrastructure).

### Root Cause

Директория с именем `C:\visual projects\arb1\collections\logs` существовала внутри папки Presentation.

**Механизм ошибки:**
1. MSBuild выполняет glob expansion для `**/*.resx`
2. Рекурсивно обходит subdirectories
3. Находит папку `C:\visual projects\arb1\collections\logs`
4. Интерпретирует имя как Windows path
5. Пытается зайти: `/home/.../Presentation/C:/visual projects/...`
6. DirectoryNotFoundException
7. Fallback: рассматривает `**/*.resx` как literal filename → MSB3552

### Решение

```bash
rm -rf 'collections/src/SpreadAggregator.Presentation/C:\visual projects\arb1\collections\logs'
dotnet build collections/src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj
```

**Результат:** Build succeeded in 8.2s ✅

### Как избежать в будущем

1. **Проверка после миграции с Windows:**
   ```bash
   find . -name "*:*" -o -name "*\\*"
   ```

2. **Добавить в .gitignore:**
   ```gitignore
   # Windows absolute paths
   C:/*
   **/C:/*
   ```

3. **Clean после git pull:**
   ```bash
   dotnet clean
   rm -rf **/bin **/obj
   ```

### Ложные гипотезы (что НЕ помогло)

- ❌ Добавление Dummy.resx
- ❌ Отключение `EnableDefaultEmbeddedResourceItems`
- ❌ Downgrade на .NET 8
- ❌ Docker build (чистая среда)
- ❌ `dotnet publish` вместо `build`

**Ключевой эксперимент:** Создание чистого .NET проекта показал, что SDK работает корректно → проблема была project-specific.

### Diagnostic команды

Использовались для поиска root cause:

```bash
# Verbose build log
dotnet msbuild -v:diagnostic SpreadAggregator.Presentation.csproj

# Поиск Windows paths в логах
dotnet msbuild -v:diagnostic | grep -i "C:/"

# Проверка содержимого директории
ls -la collections/src/SpreadAggregator.Presentation/
```

### Связанные изменения

- Удалена ссылка на `trader` проекты из `Application.csproj` и `Presentation.csproj` (cleanup)
- Очищены `bin/` и `obj/` директории

---

## Другие известные проблемы

*(Пока нет)*

---

**Последнее обновление:** 2025-11-22
