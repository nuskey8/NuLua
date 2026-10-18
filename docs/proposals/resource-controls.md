# Proposal: resource controls for server-side embedding

Status: draft implementation. Sandbox initialization, Luau execution interrupts, and the common memory-control API with Luau allocator budgets are implemented. Standard Lua execution hooks and allocator limits remain future increments.

Related issues: [sandbox initialization #19](https://github.com/nuskey8/NuLua/issues/19), [Luau interruption #21](https://github.com/nuskey8/NuLua/issues/21), and [VM memory budgets #22](https://github.com/nuskey8/NuLua/issues/22).

## Motivation

A server embedding Lua needs to bound VM allocations and interrupt scripts that do not yield or call host functions. Luau also needs a high-level way to install libraries and host functions before freezing the shared environment.

Before this change, NuLua exposed GC measurements and debug hooks without allocator budgets or a supported Luau periodic execution interrupt. Its async cancellation checks occur between coroutine resumes, so they cannot stop a non-yielding loop inside a resume.

The proposed direction follows [mlua 0.12.2](https://docs.rs/mlua/0.12.2/mlua/struct.Lua.html): share memory-control semantics across backends, expose execution hooks and Luau interrupts separately, and keep sandbox configuration Luau-specific. Backend capability differences remain explicit.

## Common memory control

```csharp
public interface ILuaMemoryControl
{
    long UsedBytes { get; }
    bool SupportsLimit { get; }

    // Returns the previous limit. Zero means unlimited.
    long SetLimit(long bytes);
}

// Available on ILuaState:
// ILuaMemoryControl Memory { get; }
```

```csharp
using var state = LuauState.Create();
state.OpenLibraries();
state.Memory.SetLimit(64 * 1024 * 1024);
```

Implemented memory contract:

- The limit covers allocator-accounted VM memory, including initialization and all coroutines belonging to that VM. It is not a per-thread or per-sandbox budget.
- The allocator tracks usage from state creation, even when no limit is configured.
- A negative limit is rejected. Zero removes the limit. Unsupported allocator control raises `NotSupportedException` rather than silently ignoring the limit.
- Lowering the limit below current usage does not reclaim memory or immediately fail; subsequent allocation growth is rejected until usage is below the limit. Frees and shrinking reallocations remain possible.
- A refused allocation surfaces through a recoverable Lua error at a supported protected boundary. Existing `LuaException` memory-error codes can be used initially; a dedicated exception type is an open API question.
- Managed host allocations and standalone compiler allocations are outside this budget. This does not promise a process RSS ceiling.

Luau-owned states use a native tracking allocator from creation, with no managed
callback per allocation. Usage includes allocator-requested VM pages and may differ
from `GarbageCollection.GetByteCount()` because of Luau's page allocator. The
budget and counter are shared by the main state and every coroutine. Lua/LuaJIT
currently report GC-accounted usage and `SupportsLimit == false`; setting any limit
on those backends throws. Older Luau binaries use the existing allocator, report
unsupported limits, and keep the existing high-level APIs available.

Luau allocation-capable host operations run within native protection, including
library setup, table/string/userdata/buffer allocation, loading, reference growth,
function registration, table access, and debug inspection. Managed function failures
are retained; a native trampoline raises the VM error after the managed callback
returns. Refused allocations use `LuaException` with the VM memory-error status.
No temporary allocation allowance is used during normal operations; disposal removes
the limit before closing the VM so cleanup can complete.

Allocator limits cannot currently be combined with Luau debug callbacks or coverage
collection. Both registration orders are rejected rather than exposing a managed
callback to native unwinding. The periodic execution interrupt is supported with
limits. Raw native pointer calls bypass these protected host wrappers and are the
caller's responsibility. Configuration and execution on the same VM must be serialized.

## Execution control

```csharp
public enum LuaVmAction
{
    Continue,
    Yield
}

public delegate LuaVmAction LuaExecutionHook(
    ILuaState state,
    LuaHookEvent hookEvent,
    int currentLine);

// Proposed additions to ILuaDebug for standard Lua/LuaJIT:
// void SetExecutionHook(LuaExecutionHook? callback, LuaHookMask mask, int count);
// void SetGlobalExecutionHook(LuaExecutionHook? callback, LuaHookMask mask, int count);

// Implemented on LuauState (continue/error only):
// void SetInterrupt(Action? callback);
```

The existing void-returning debug-hook API remains available. Because a native Lua thread has one hook slot, observational and execution hooks must have an explicit coexistence contract. Proposed initial rule: installing one replaces the other on the affected thread, and removal clears that registration. Global registrations supply the default for new NuLua-created threads; per-thread registrations override it. Lua-created coroutine inheritance and propagation to already-existing threads need a precise contract before implementation.

Luau implements `SetInterrupt(Action? callback)`: return normally to continue,
throw to interrupt, or pass null to remove the VM-wide registration. Registration
through any NuLua-created thread replaces the callback for all existing and new
coroutines, including script-created coroutines. Yield is deliberately deferred.
The callback checks host state only; accessing or disposing the VM from it throws.
GC interrupts do not invoke managed code. NuLua reserves `lua_Callbacks.userdata`
and the periodic `interrupt` slot while installed; do not overwrite them through
raw native access. Debug interrupt registration is independent.

```csharp
var timer = System.Diagnostics.Stopwatch.StartNew();
state.SetInterrupt(() =>
{
    if (timer.Elapsed >= TimeSpan.FromMilliseconds(100))
        throw new TimeoutException("Script deadline exceeded.");
});
state.DoString(source);
```

Managed failures are retained, the managed callback returns, and the native
trampoline then raises a Lua error. The next host boundary rethrows the original
managed exception, even if script code caught the Lua error. The pending failure
is then cleared so the state can be reused. Registration is rooted until removed
or the main state is disposed. Callback frequency is VM-dependent; host callbacks
are not preempted. Adversarial loops that repeatedly catch errors with `pcall`
are not guaranteed to terminate; use external process limits for that guarantee.

Native shim changes require rebuilding runtime binaries. macOS arm64 is rebuilt
and tested locally; other platform binaries need the native build workflow before
this feature is available there. Old binaries report `NotSupportedException` on
registration. Existing APIs remain usable with those binaries.

Automatic integration with `DoStringAsync` cancellation is deferred. It needs a separate ownership/composition contract so cancellation does not overwrite a user-installed callback.

## Luau sandbox initialization

```csharp
using var state = LuauState.Create();
state.OpenLibraries();
state.RegisterFunction("lookup", Lookup);
state.Sandbox();
using var worker = state.CreateSandboxThread();
```

`LuauState.Sandbox()` permanently freezes the main state's shared global table,
library tables, and builtin metatables covered by Luau's `luaL_sandbox`.
Install libraries and host functions first, then freeze, then create sandbox
threads. Each worker has writable local globals that read from the frozen
shared globals. Tables are frozen shallowly; this does not recursively freeze
arbitrary host object graphs or restrict the capabilities of host functions.

Repeated calls are safe. Child states are rejected, and disposed states throw.
The main state's globals remain read-only: execute scripts that assign globals
in sandbox threads. Existing threads, previously loaded chunks, and cached
module environments are not isolated retroactively. There is no disable mode;
create a new VM to rebuild the shared environment. Existing `CreateSandbox()`
retains its immediate-freeze behavior and delegates to `Sandbox()`.

## Implementation and validation sequence

The implementation is split into one commit per feature:

1. Sandbox initialization and environment semantics for #19.
2. Native callback/error handling and Luau interrupts for #21. Follow with standard Lua execution hooks and any common cancellation helpers.
3. Common memory accounting and Luau allocator budgets with protected allocation paths for #22. Standard Lua/LuaJIT budgets remain unsupported.

Changes to generated bindings/state implementations must come from native headers, binding generation, and Scriban templates; generated files must not be edited directly. Native changes require rebuilt runtime binaries for supported platforms.

Validation should include:

- Memory failure during execution, chunk loading, callback setup, and result-reference creation; correct accounting after free/reallocation; limit changes and disposal; coroutine-shared accounting.
- Pure-Luau loops, nested/protected calls, callback exceptions, recursion, replacement/removal, coroutine propagation, and supported/unsupported yield points. Guard runaway or fatal native cases with subprocess timeouts.
- Library immutability, callable installed host functions, isolated sandbox globals, repeat enable/disable, and live/cached environments.
- State reuse and cleanup after errors, supported backend differences, and server Linux x64/arm64 execution as well as Windows/macOS coverage.

Implemented regressions cover initialization after host setup, library immutability,
worker globals, invalid sandbox receivers, pure loops, caught interrupt failures,
coroutine sharing, replacement/removal, callback reentrancy, shared memory accounting,
limit changes, GC reclamation, allocation refusal in execution and host callbacks,
loading/reference growth, debug incompatibility, and cleanup at the limit. The broader
backend/platform and hostile-termination criteria above remain future validation.

Local validation: 515 high-level tests passed, the Luau .NET Standard 2.1 build
passed, and an original-binary compatibility probe passed. Validation uses macOS
arm64 and an isolated .NET 10 copy (the installed SDK)
with repository target frameworks unchanged. Native failure tests run under a
subprocess timeout. Cross-platform native binaries must be rebuilt by the native
workflow before interruption or allocator budgets are available on those platforms.
Separate VMs and external process limits remain useful for strict tenant isolation.
