# Proposal: resource controls for server-side embedding

Status: implementation in progress. Sandbox initialization and Luau execution interruption are implemented; memory controls below remain a proposal.

Related issues: [sandbox initialization #19](https://github.com/nuskey8/NuLua/issues/19), [Luau interruption #21](https://github.com/nuskey8/NuLua/issues/21), and [VM memory budgets #22](https://github.com/nuskey8/NuLua/issues/22).

## Motivation

A server embedding Lua needs to bound VM allocations and interrupt scripts that do not yield or call host functions. Luau also needs a high-level way to install libraries and host functions before freezing the shared environment.

NuLua already exposes GC measurements and debug hooks, but these do not provide an allocator budget or a supported Luau periodic execution interrupt. Its async cancellation checks occur between coroutine resumes, so they cannot stop a non-yielding loop inside a resume.

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

// Proposed addition to ILuaState:
// ILuaMemoryControl Memory { get; }
```

```csharp
using var state = LuauState.Create();
state.OpenLibraries();
state.Memory.SetLimit(64 * 1024 * 1024);
```

Proposed contract:

- The limit covers allocator-accounted VM memory, including initialization and all coroutines belonging to that VM. It is not a per-thread or per-sandbox budget.
- The allocator tracks usage from state creation, even when no limit is configured.
- A negative limit is rejected. Zero removes the limit. Unsupported allocator control raises `NotSupportedException` rather than silently ignoring the limit.
- Lowering the limit below current usage does not reclaim memory or immediately fail; subsequent allocation growth is rejected until usage is below the limit. Frees and shrinking reallocations remain possible.
- A refused allocation surfaces through a recoverable Lua error at a supported protected boundary. Existing `LuaException` memory-error codes can be used initially; a dedicated exception type is an open API question.
- Managed host allocations and standalone compiler allocations are outside this budget. This does not promise a process RSS ceiling.

Implementation should use a tracking allocator for owned states. Native allocator bookkeeping is preferred to calling managed code on every allocation. Accounting must handle reallocations, failed allocations, initialization failures, and disposal. Allocator capability, particularly across LuaJIT configurations, must be checked rather than assumed.

Crucially, allocation-capable API calls need protected error boundaries. A counter alone is insufficient: chunk loading, callbacks, result references, and cleanup must remain safe at the limit. [mlua's allocator](https://github.com/mlua-rs/mlua/blob/v0.12.2/src/memory.rs) temporarily relaxes limits for selected internal operations used to establish protected calls. Any similar allowance must be small, documented, and tested.

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

// Proposed addition to LuauState:
// void SetInterrupt(Func<LuauState, LuaVmAction>? callback);
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

Implement separate changes after agreement on each API:

1. Sandbox initialization and environment semantics for #19.
2. Native callback/error handling and Luau interrupts for #21. Follow with standard Lua execution hooks and any common cancellation helpers.
3. Allocator accounting, protected allocation paths, and memory control for #22.

Changes to generated bindings/state implementations must come from native headers, binding generation, and Scriban templates; generated files must not be edited directly. Native changes require rebuilt runtime binaries for supported platforms.

Validation should include:

- Memory failure during execution, chunk loading, callback setup, and result-reference creation; correct accounting after free/reallocation; limit changes and disposal; coroutine-shared accounting.
- Pure-Luau loops, nested/protected calls, callback exceptions, recursion, replacement/removal, coroutine propagation, and supported/unsupported yield points. Guard runaway or fatal native cases with subprocess timeouts.
- Library immutability, callable installed host functions, isolated sandbox globals, repeat enable/disable, and live/cached environments.
- State reuse and cleanup after errors, supported backend differences, and server Linux x64/arm64 execution as well as Windows/macOS coverage.

These tests are proposed acceptance criteria, not tests run by this documentation-only draft. Separate VMs and external process limits remain useful where strict tenant isolation is required.
