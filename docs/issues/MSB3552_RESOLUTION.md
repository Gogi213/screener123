# Решение MSB3552 Build Error

**Дата:** 2025-11-22  
**Статус:** ✅ **РЕШЕНО!**

---

## Root Cause

**Проблема:** Директория с именем `C:\visual projects\arb1\collections\logs` существовала внутри `/home/giorgioisitalii/screener123/collections/src/SpreadAggregator.Presentation/`.

**Механизм ошибки:**
1. MSBuild выполняет glob expansion для `**/*.resx`
2. Рекурсивно обходит все поддиректории  
3. Находит папку с именем `C:\visual projects\arb1\collections\logs`
4. Интерпретирует это как Windows путь
5. Пытается зайти по пути `/home/.../Presentation/C:/visual projects/...`
6. Не находит → `DirectoryNotFoundException`
7. Fallback: `**/*.resx` интерпретируется как literal filename → MSB3552

---

## Решение

```bash
rm -rf 'collections/src/SpreadAggregator.Presentation/C:\visual projects\arb1\collections\logs'
```

**Результат:** Build succeeded in 8.2s ✅

---

## Как это произошло

Вероятно, проект изначально разрабатывался на Windows в папке `C:\visual projects\arb1\`. При миграции на Linux:
- Какой-то процесс создал папку с **именем** Windows пути вместо того, чтобы использовать Linux путь
- Git сохранил эту папку (так как в Linux `\` и `:` - допустимые символы в именах)
- MSBuild на Linux не ожидал такого и сломался

---

## Preventive Measures

1. **.gitignore:**  
   Добавить все absolute paths в `.gitignore`
   
2. **Проверять имена папок:**  
   ```bash
   find . -name "*:*" -o -name "*\\*"
   ```

3. **Clean после миграции:**  
   ```bash
   dotnet clean
   rm -rf **/bin **/obj
   ```

---

## Sequential Thinking Breakdown

1. ✅ Создал чистый проект → собрался → SDK не сломан
2. ✅ Протестировал все проекты по отдельности → Presentation падает
3. ✅ Diagnostic build (`-v:diagnostic`) → нашел Windows путь в логах
4. ✅ `ls -la Presentation/` → нашел папку с Windows именем
5. ✅ Удалил папку → BUILD SUCCESS

**Вывод:** Твоя критика была АБСОЛЮТНО ПРАВИЛЬНОЙ. Проблема была не в SDK.
