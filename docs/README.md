# 📚 Documentation Index

**MEXC Trade Screener - Documentation Hub**

---

## 🚀 Quick Navigation

### **For NEW Chat Session:**
1. Start with → **[QUICK_START.md](QUICK_START.md)** ⚡
2. Need context → **[SPRINT_CONTEXT.md](SPRINT_CONTEXT.md)** 📋
3. Technical deep-dive → **[ARCHITECTURE.md](ARCHITECTURE.md)** 🏗️

### **For Development:**
- Development principles → **[GEMINI_DEV.md](GEMINI_DEV.md)** 🧠
- Project structure → **[project_structure.txt](project_structure.txt)** 📂

---

## 📄 File Descriptions

### **QUICK_START.md** ⚡
**When:** Starting work in new chat  
**Contains:**
- Current sprint tasks (SPRINT-3)
- Exact code changes needed
- File locations and line numbers
- Testing checklist
- Common issues & solutions

**Read time:** 3 minutes

---

### **SPRINT_CONTEXT.md** 📋
**When:** Need full project context  
**Contains:**
- What's been done (SPRINT-1, SPRINT-2)
- What's pending (SPRINT-3, SPRINT-4)
- Detailed implementation notes
- Architecture decisions
- Performance metrics
- Known issues

**Read time:** 10 minutes

---

### **ARCHITECTURE.md** 🏗️
**When:** Understanding system design  
**Contains:**
- System architecture diagram
- Component responsibilities
- Algorithm explanations
- Data structures
- WebSocket message formats
- Performance optimizations
- Scalability analysis

**Read time:** 15 minutes

---

### **GEMINI_DEV.md** 🧠
**When:** Understanding development philosophy  
**Contains:**
- Evidence-based development principles
- Sequential thinking approach
- Performance criticality
- Minimal entity proliferation
- Clear documentation standards

**Read time:** 5 minutes

---

## 🎯 Current Status

**Sprint:** SPRINT-2 Complete, SPRINT-3 Pending  
**Goal:** Implement simple sorting by `trades/3m` with TOP-50 rendering  
**ETA:** 1-2 hours

**Key Changes Needed:**
1. Server: Sort by `trades3m` instead of `compositeScore`
2. Client: Render only top-50 charts
3. Client: Display `/3m` instead of `/1m`
4. Client: Respect Speed Sort toggle

---

## 📊 Quick Stats

**Implemented:**
- ✅ Rolling window metrics (1m, 2m, 3m)
- ✅ Advanced benchmarks (acceleration, patterns, imbalance)
- ✅ WebSocket broadcasting (every 2 seconds)
- ✅ 2000+ symbols monitored in real-time

**Pending:**
- 🔨 Simple sorting by trades/3m
- 🔨 TOP-50 chart rendering
- 🔨 Benchmark indicators on cards

---

## 🔗 Important Git Info

**Last stable commit:** `59204ea`  
**Restore screener.js:**
```bash
git checkout 59204ea -- src/SpreadAggregator.Presentation/wwwroot/js/screener.js
```

---

## 💡 Pro Tips

1. **Always start with QUICK_START.md** in new chat
2. **Reference ARCHITECTURE.md** for technical questions
3. **Use SPRINT_CONTEXT.md** for full session history
4. **Follow GEMINI_DEV.md** principles when coding

---

## 📞 Contact

**Project:** MEXC Trade Screener  
**Version:** 2.0 (SPRINT-2 complete)  
**Last Updated:** 2025-11-25 07:07 UTC+4

---

**Ready to code? Start with [QUICK_START.md](QUICK_START.md)! 🚀**
