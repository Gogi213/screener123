# Collections Project - Change Log

## 2025-11-22

### ✅ Resolved: MSB3552 Build Error

**Issue:** `dotnet build` failed with `error MSB3552: Resource file "**/*.resx" cannot be found`

**Root Cause:** Directory named `C:\visual projects\arb1\collections\logs` in Presentation folder (Windows migration artifact)

**Fix:** 
```bash
rm -rf 'collections/src/SpreadAggregator.Presentation/C:\visual projects\arb1\collections\logs'
```

**Status:** ✅ Build now works successfully (8.2s)

**Details:** See [`docs/issues/BUILD_ISSUES.md`](../docs/issues/BUILD_ISSUES.md)

---

### Modified Files

#### `/collections/src/SpreadAggregator.Application/SpreadAggregator.Application.csproj`
- Removed reference to `trader/src/Core/TraderBot.Core.csproj`

#### `/collections/src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj`
- Removed references to trader projects (Core, GateIo, Bybit)

#### `/collections/src/SpreadAggregator.Presentation/appsettings.json`
- Updated paths for Linux compatibility
- Changed Kestrel listening to `0.0.0.0:5000` (external access)
- Updated WebSocket endpoint to `0.0.0.0:8181`

#### `/collections/src/SpreadAggregator.Presentation/Program.cs`
- Fixed PerformanceMonitor path for Linux (`/home/giorgioisitalii/screener123/collections/logs/performance`)

---

### Added Files

- `collections/README.md` - Project overview and quick start
- `docs/issues/BUILD_ISSUES.md` - Build issues changelog
- `docs/issues/MSB3552_RESOLUTION.md` - Detailed resolution doc

---

### Deployment Status

**Server:** Google Cloud Linux instance-20251122-182421  
**IP:** 10.146.0.4  
**Process:** PID 66644  
**Endpoints:**
- HTTP: `http://10.146.0.4:5000`
- WebSocket: `ws://10.146.0.4:8181`

**Log:** `/home/giorgioisitalii/screener123/collections/logs/app.log`

---

## Earlier Changes

*(No tracked changes before 2025-11-22)*
