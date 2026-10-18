#pragma once
#include <stddef.h>

#include "../../shim/nulua_shim.h"

// can be used to reconfigure visibility/exports for public APIs
#ifndef LUAU_SHIM_API
#define LUAU_SHIM_API extern
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Frees a buffer returned by luau_compile using the same allocator the
// dylib was built with. Required because the host runtime (e.g. .NET) may
// not share a CRT with the dylib (notably on Windows).
LUAU_SHIM_API void luau_free(void* p);

// Sets a process-wide boolean feature flag. Returns 1 if found, otherwise 0.
// Configure flags before using the compiler or VM; concurrent access is unsafe.
LUAU_SHIM_API int luau_setbooleanflag(const char* name, int enabled);
LUAU_SHIM_API int luau_getbooleanflag(const char* name, int* enabled);
LUAU_SHIM_API int luau_setintegerflag(const char* name, int value);
LUAU_SHIM_API int luau_getintegerflag(const char* name, int* value);

// Pushes the given NUL-terminated message and raises a Lua error. Mirrors
// the common `lua_pushstring(L, msg); lua_error(L);` idiom. We accept the
// lua_State as an opaque void* to keep this header free of Lua includes.
// The underlying VM routines never return — we declare the signature as
// returning `int` so the binding generator can surface it and so call
// sites can use it from a Lua C function as a tail expression.
LUAU_SHIM_API int luau_error(void* L, const char* msg);


// NuLua owns lua_Callbacks.userdata while an interrupt is installed.
// Returns nonzero to request an error after the managed frame returns.
typedef int (*luau_interrupt_callback)(void* state);
LUAU_SHIM_API void luau_setinterrupt(void* state, luau_interrupt_callback callback);

LUAU_SHIM_API void* luau_newtrackedstate(void);
LUAU_SHIM_API void luau_closetrackedstate(void* state);
LUAU_SHIM_API int luau_memoryinfo(void* state, size_t* used, size_t* limit);
LUAU_SHIM_API size_t luau_setmemorylimit(void* state, size_t limit);
// Protected host operations: integer codes are defined in LuauState.Memory.cs.
LUAU_SHIM_API int luau_memoryoperation(void* state, int operation, int a, int b,
    const void* data, size_t size, void** pointerResult, int* integerResult);
LUAU_SHIM_API int luau_pload(void* state, const char* name, const char* data, size_t size);

#ifdef __cplusplus
}
#endif
