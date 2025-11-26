# GEMINI_DEV - Development Protocol

**Version:** 2.0  
**Last Updated:** 2025-11-26

---

## 🎯 Core Principle

**Build systems that work, not systems that look good on paper.**

Evidence-based development. Minimize complexity. Validate against reality (codebase), not assumptions.

---

## 🔴 CRITICAL: Validation Protocol

### **MANDATORY VALIDATION STEPS**

When validating ANY code change:

1. **Read Actual File** - use `view_file` tool
2. **Verify Line-by-Line** - check what's REALLY there
3. **Never Trust Mental Model** - even your own recent changes
4. **Source of Truth = Codebase** - not docs, not memory, not plans

**ORDER (STRICT):**
```
Write code → Apply changes → Read ACTUAL file → Validate logic → Verdict
```

**FORBIDDEN:**
- ❌ Validate based on plan/documentation
- ❌ Validate based on memory of what you wrote
- ❌ Assume code is correct without reading file
- ❌ Trust diff output without viewing full file context

**ALLOWED:**
- ✅ `view_file` → analyze → validate
- ✅ `grep_search` for cross-references
- ✅ `view_code_item` for specific methods
- ✅ Admit "I need to check the file first"

**EXAMPLE:**
```
❌ BAD:  "I added reconnectAttempt variable, so it should be at line 32"
✅ GOOD: view_file(screener.js, lines 30-35) → verify variable exists → validate logic
```

---

## 📏 Entity Minimalism

### **High-Cost Entities (MINIMIZE)**

Require strong justification + alternatives analysis:

- New NuGet package
- New project in solution  
- New external service (database, API)
- New background process/thread
- New network dependency

**Rule:** Before adding high-cost entity, answer:
1. Why is it needed? (real problem)
2. What are alternatives? (simpler solutions)
3. What is the cost? (complexity, maintenance)

### **Low-Cost Entities (OK)**

Add freely if improves code quality:

- New method in existing class
- New variable
- New CSS class / JS function
- Helper functions
- Comments

**Examples:**
- ✅ Add `reconnectAttempt` variable → Low-cost, clear purpose
- ❌ Add Redis for simple in-memory cache → High-cost, overkill for MVP
- ✅ Add `BinanceSpotFilter` class → Low/Medium cost, single responsibility
- ❌ Add QuestDB without benchmarking first → High-cost, unproven need

---

## 🧠 Sequential Thinking - When to Use

### **USE Sequential Thinking For:**

- **Planning new features** - architecture, approach
- **Validating complex logic** - edge cases, race conditions
- **Critical decisions** - technology choice, major refactor
- **Post-mortem analysis** - understanding bugs

**Complexity threshold:** >10 lines OR non-obvious logic OR critical path

### **SKIP Sequential Thinking For:**

- Simple changes (<5 lines, trivial logic)
- Renaming variables
- Fixing typos
- Adding comments
- Adjusting constants (e.g., point size 3 → 4)

**Rule:** If the change is obvious and risk is low → just do it.

---

## 📝 Documentation Minimalism

### **Write Docs ONLY IF Useful**

**Write:**
- ✅ `SPRINT_CONTEXT.md` - resume for new chat
- ✅ `QUICK_START.md` - how to run
- ✅ `ARCHITECTURE.md` - high-level decisions (WHY, not HOW)
- ✅ `CHANGELOG.md` - version history
- ✅ README - project overview

**DON'T Write:**
- ❌ Duplicate of code logic
- ❌ "How it works" if code is self-explanatory
- ❌ Detailed implementation notes (code documents itself)
- ❌ Theoretical design docs nobody reads

### **Code as Documentation**

- Good naming (variables, functions, classes)
- Comments for **WHY**, not **WHAT**
- Structured code (clear separation of concerns)
- Examples in README for complex usage

**Principle:** If you need docs to understand code → refactor code first.

---

## 📊 Performance & Monitoring

### **Development (MVP/Screener)**

- `Console.WriteLine` - sufficient ✅
- Task Manager - RAM/CPU checks ✅
- Periodic manual checks - acceptable ✅

**No complex monitoring needed** for single-instance tools.

### **Production (Multi-Instance/Critical)**

- Structured logging (Serilog)
- Prometheus/Grafana
- Alerts (CPU, RAM, errors)

### **Rule: Monitoring Must Be Used**

- If you don't look at metrics → remove monitoring
- If monitoring is broken → fix or remove
- Simple working > Complex broken

**Example from project:**
- SimpleMonitor добавили, но console не обновлялся
- Реально смотрели Task Manager
- Lesson: Working simple wins over broken fancy

---

## 🔬 Evidence-Based Development

### **Measured Problems Only**

Before solving a problem:

1. **Reproduce** - can you trigger it consistently?
2. **Measure** - CPU spike? RAM leak? Latency?
3. **Evidence** - logs, metrics, profiler output
4. **Hypothesis** - what's the root cause?
5. **Fix** - implement solution
6. **Verify** - measure again, confirm fix

**FORBIDDEN:**
- ❌ "This might be slow" without profiling
- ❌ "Users probably want X" without asking
- ❌ "Scaling to 10k symbols" without current need

**ALLOWED:**
- ✅ "CPU is 80%, profiler shows X function" → optimize
- ✅ "User reported freeze, reproduced in 3 scenarios" → fix
- ✅ "Memory grows 10MB/hour, leak detected" → fix

---

## 🚫 No Over-Engineering

### **YAGNI Examples (from project)**

**Rejected (correctly):**
- QuestDB migration → no evidence RAM is problem
- Structured logging → single-instance tool, console.log sufficient
- REST API for historical trades → charts fill in 30 sec, acceptable
- Prometheus metrics → overkill for MVP
- UI status indicator for reconnect → works silently, acceptable

**Accepted (correctly):**
- WebSocket reconnection → measured problem (restart kills client)
- Health monitoring → visible issue detection
- Major exchanges filter → clear user value

### **Decision Framework**

Ask:
1. Is there a **measured problem** this solves?
2. Is this the **simplest solution**?
3. Will you **actually use** this feature?
4. What's the **cost** (complexity, maintenance)?

**If answer to 1-3 is "no" → don't build it.**

---

## 🛠️ Practical Workflow

### **1. Understand the Problem**

- What's broken? (specific, measurable)
- Can you reproduce?
- What's the impact?

### **2. Plan (Sequential Thinking if complex)**

- Simple solution first
- Alternatives considered
- Complexity assessment

### **3. Implement**

- Write code
- Apply changes
- **READ ACTUAL FILE** (validation protocol)

### **4. Validate**

- Against **codebase** (not mental model)
- Line-by-line check
- Logic validation (sequential thinking if needed)

### **5. Test**

- Manual test (main scenario)
- Edge cases if critical
- Performance check if relevant

### **6. Document (if needed)**

- Update SPRINT_CONTEXT if major change
- Update CHANGELOG
- Skip detailed docs (code documents itself)

---

## ✅ Checklist для Changes

Before committing code:

- [ ] Read actual file (view_file) - validation protocol
- [ ] Verified logic against real codebase
- [ ] Tested manually (main scenario)
- [ ] Performance acceptable (if relevant)
- [ ] No new high-cost entities without justification
- [ ] Docs updated (if needed - SPRINT_CONTEXT, CHANGELOG)
- [ ] Code is self-documenting (good naming, clear structure)

---

## 🎓 Lessons from Project

### **What Worked ✅**

1. Minimal approach (WebSocket reconnect = 15 lines)
2. Consilium validation (reject over-engineering)
3. Code-first, docs-later
4. Performance focus (2% CPU, 60 MB RAM)
5. Simple solutions (exponential backoff > complex state machine)

### **What Didn't ❌**

1. SimpleMonitor не использовали (broken display)
2. Тесты удалили (не нужны для screener)
3. QuestDB обсуждали долго (rejected, правильно)
4. Документация местами устаревала (sync issue)

### **Key Takeaway**

**Working simple code > Perfect complex architecture**

Ship features that solve real problems. Iterate based on actual usage, not theoretical scenarios.

---

## 📜 Summary

1. **Validate against codebase** - not mental model
2. **Minimize high-cost entities** - justify before adding
3. **Sequential thinking for complex** - skip for trivial
4. **Docs only if useful** - code documents itself
5. **Measured problems only** - evidence-based
6. **No over-engineering** - YAGNI principle
7. **Simple working > Complex broken** - pragmatism wins

**Core Philosophy:** Build what's needed, when it's needed, as simply as possible. Validate ruthlessly against reality.
