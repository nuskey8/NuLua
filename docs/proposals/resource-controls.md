# Proposal: resource controls for server-side embedding

Status: draft for API discussion. The signatures below are proposals; this PR does not add public APIs or runtime behavior.

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

For Luau, `SetInterrupt` installs the VM-wide periodic `interrupt` callback, not `debuginterrupt`. Null removes the callback. All coroutines share the registration. Returning `Continue` allows execution; throwing requests an error through the protected native boundary. `Yield` requests suspension only where the backend permits it. We should agree on unsupported-yield behavior and verify Lua/LuaJIT differences before exposing that enum value on every backend; continue/error-only is a possible first increment.

Illustrative deadline policy:

```csharp
using var state = LuauState.Create();
var timer = System.Diagnostics.Stopwatch.StartNew();

state.SetInterrupt(_ =>
{
    if (timer.Elapsed >= TimeSpan.FromMilliseconds(100))
        throw new TimeoutException("Script deadline exceeded.");

    return LuaVmAction.Continue;
});

state.DoString(source);
```

Native callbacks must catch managed exceptions, retain them, return from the managed callback, and only then raise the Lua error from native code. No Lua longjmp/C++ exception should deliberately cross a managed callback frame. Define how retained exceptions become managed failures, including when Lua catches an error with `pcall`; clearing pending exception state and allowing later state reuse require tests.

Callbacks need rooted lifetime management, recursion protection, safe replacement/removal, and cleanup before VM destruction. Luau GC interrupts must not use an error-raising path. See [mlua's interrupt implementation](https://github.com/mlua-rs/mlua/blob/v0.12.2/src/state.rs#L793).

The callback checks a host policy, such as a monotonic deadline or an atomic cancellation flag. Interrupt frequency is backend-dependent. This is cooperative interruption of VM execution, not preemption of an arbitrary blocking host callback or an exact instruction/time budget. Ordinary Lua errors can be caught by script code; repeatedly checking an expired deadline is not by itself a proof of unconditional termination. Termination guarantees under protected calls need adversarial validation.

Automatic integration with `DoStringAsync` cancellation is deferred. It needs a separate ownership/composition contract so cancellation does not overwrite a user-installed callback.

## Luau sandbox initialization

```csharp
// Proposed addition to LuauState:
// void Sandbox(bool enabled);

using var state = LuauState.Create();
state.OpenLibraries();
state.RegisterFunction("lookup", Lookup);
state.Sandbox(true);

using var worker = state.CreateSandboxThread();
```

Enabling sandbox mode after initialization freezes shared libraries/globals and establishes writable local globals that read from the shared environment. Existing `CreateSandbox()` remains available for compatibility.

To follow [mlua's sandbox API](https://docs.rs/mlua/0.12.2/mlua/struct.Lua.html#method.sandbox), disabling would restore the original global environment and discard local global overrides. It would not undo arbitrary mutations or revoke references to objects already obtained by scripts. Whether reversible mode is necessary, or a one-way `Sandbox()` is preferable, is a maintainer decision. Repeated calls, calls on child states, existing sandbox threads, and cached module environments need defined behavior.

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
