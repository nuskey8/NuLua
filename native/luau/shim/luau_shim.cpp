#include "luau_shim.h"

#include <cstdlib>
#include <cstring>

#include "Luau/Common.h"

#include "lua.h"
#include "lualib.h"

#include "../../shim/nulua_shim.c"

template<typename T>
static Luau::FValue<T>* findFlag(const char* name) {
    if (name == nullptr)
        return nullptr;
    for (auto* flag = Luau::FValue<T>::list; flag; flag = flag->next)
        if (std::strcmp(flag->name, name) == 0)
            return flag;
    return nullptr;
}

extern "C" int luau_setbooleanflag(const char* name, int enabled) {
    if (auto* flag = findFlag<bool>(name)) {
        flag->value = enabled != 0;
        return 1;
    }
    return 0;
}

extern "C" int luau_getbooleanflag(const char* name, int* enabled) {
    if (auto* flag = findFlag<bool>(name); flag && enabled) {
        *enabled = flag->value ? 1 : 0;
        return 1;
    }
    return 0;
}

extern "C" int luau_setintegerflag(const char* name, int value) {
    if (auto* flag = findFlag<int>(name)) {
        flag->value = value;
        return 1;
    }
    return 0;
}

extern "C" int luau_getintegerflag(const char* name, int* value) {
    if (auto* flag = findFlag<int>(name); flag && value) {
        *value = flag->value;
        return 1;
    }
    return 0;
}

extern "C" void luau_free(void* p) {
    std::free(p);
}

extern "C" int luau_error(void* L, const char* msg) {
    auto* state = static_cast<lua_State*>(L);
    lua_pushstring(state, msg);
    lua_error(state);
    // Unreachable: lua_error longjmps. Keep a return for type-correctness.
    return 0;
}

static void nulua_interrupt(lua_State* L, int gc) {
    if (gc >= 0) // Raising an error during GC is not supported.
        return;
    auto callback = reinterpret_cast<luau_interrupt_callback>(lua_callbacks(L)->userdata);
    if (callback && callback(L)) {
        lua_pushliteral(L, "host execution interrupt");
        lua_error(L); // The managed callback has already returned.
    }
}

extern "C" void luau_setinterrupt(void* state, luau_interrupt_callback callback) {
    auto* cb = lua_callbacks(static_cast<lua_State*>(state));
    cb->userdata = reinterpret_cast<void*>(callback);
    cb->interrupt = callback ? nulua_interrupt : nullptr;
}

#include "ldo.h"
#include <limits>
#include <new>

struct NuLuaMemory {
    size_t used = 0;
    size_t limit = 0;
    bool refused = false;
    lua_CFunction sync = nullptr;
    lua_CFunction async = nullptr;
};

static void* nulua_alloc(void* ud, void* ptr, size_t oldsize, size_t newsize) {
    auto* memory = static_cast<NuLuaMemory*>(ud);
    if (!ptr) oldsize = 0;
    if (newsize == 0) {
        std::free(ptr);
        memory->used -= oldsize;
        return nullptr;
    }
    if (newsize > oldsize) {
        size_t growth = newsize - oldsize;
        if (growth > std::numeric_limits<size_t>::max() - memory->used ||
            (memory->limit && (memory->used > memory->limit || growth > memory->limit - memory->used)))
        {
            memory->refused = true;
            return nullptr;
        }
    }
    void* result = std::realloc(ptr, newsize);
    if (result) memory->used = memory->used - oldsize + newsize;
    else memory->refused = true;
    return result;
}

static NuLuaMemory* nulua_memory(lua_State* L) {
    void* ud = nullptr;
    return lua_getallocf(L, &ud) == nulua_alloc ? static_cast<NuLuaMemory*>(ud) : nullptr;
}

extern "C" void* luau_newtrackedstate() {
    auto* memory = new (std::nothrow) NuLuaMemory;
    if (!memory) return nullptr;
    lua_State* L = lua_newstate(nulua_alloc, memory);
    if (!L) delete memory;
    return L;
}

extern "C" void luau_closetrackedstate(void* state) {
    auto* L = static_cast<lua_State*>(state);
    auto* memory = nulua_memory(L);
    // Cleanup must be able to allocate VM-internal bookkeeping if needed.
    if (memory) memory->limit = 0;
    lua_close(L);
    delete memory;
}

extern "C" int luau_memoryinfo(void* state, size_t* used, size_t* limit) {
    auto* memory = nulua_memory(static_cast<lua_State*>(state));
    if (!memory) return 0;
    *used = memory->used;
    *limit = memory->limit;
    return 1;
}

extern "C" size_t luau_setmemorylimit(void* state, size_t limit) {
    auto* memory = nulua_memory(static_cast<lua_State*>(state));
    if (!memory) return 0;
    size_t previous = memory->limit;
    memory->limit = limit;
    return previous;
}

static int nulua_hostcall(lua_State* L, bool async) {
    auto* memory = nulua_memory(L);
    int result = (async ? memory->async : memory->sync)(L);
    if (result == -2) {
        lua_pushliteral(L, "host callback failed");
        lua_error(L);
    }
    return result;
}
static int nulua_synccall(lua_State* L) { return nulua_hostcall(L, false); }
static int nulua_asynccall(lua_State* L) { return nulua_hostcall(L, true); }

// Values mirror MemoryOp in LuauState.Memory.cs.
enum NuLuaOperationCode {
    OpPushString = 0,
    OpNewTable = 1,
    OpNewThread = 2,
    OpNewUserData = 3,
    OpNewBuffer = 4,
    OpRawSet = 5,
    OpNewMetatable = 6,
    OpReference = 7,
    OpOpenLibraries = 8,
    OpOpenLibrary = 9,
    OpSandbox = 10,
    OpSandboxThread = 11,
    OpNewFunction = 12,
    OpToString = 13,
    OpGarbageCollect = 14,
    OpGetGlobal = 15,
    OpSetGlobal = 16,
    OpGetTable = 17,
    OpSetTable = 18,
    OpGetField = 19,
    OpSetField = 20,
    OpConcat = 21,
    OpCompare = 22,
    OpLength = 23,
    OpNext = 24,
    OpReserved = 25,
    OpCall = 26,
    OpResume = 27,
    OpBreakpoint = 28,
    OpGetInfo = 29,
    OpGetLocal = 30,
    OpSetLocal = 31,
    OpGetUpvalue = 32,
    OpSetUpvalue = 33,
    OpGetArgument = 34,
};

struct NuLuaOperation {
    int op, a, b;
    const void* data;
    size_t size;
    void* pointer = nullptr;
    int integer = 0;
};
static void nulua_memoryoperation(lua_State* L, void* ud) {
    auto& o = *static_cast<NuLuaOperation*>(ud);
    switch (o.op) {
    case OpPushString: lua_pushlstring(L, static_cast<const char*>(o.data), o.size); break;
    case OpNewTable: lua_createtable(L, o.a, o.b); break;
    case OpNewThread: o.pointer = lua_newthread(L); break;
    case OpNewUserData: o.pointer = lua_newuserdatatagged(L, o.size, 0); break;
    case OpNewBuffer: o.pointer = lua_newbuffer(L, o.size); break;
    case OpRawSet: lua_rawset(L, o.a); break;
    case OpNewMetatable: o.integer = luaL_newmetatable(L, static_cast<const char*>(o.data)); break;
    case OpReference: o.integer = lua_ref(L, o.a); break;
    case OpOpenLibraries: luaL_openlibs(L); break;
    case OpOpenLibrary: lua_pushcfunction(L, reinterpret_cast<lua_CFunction>(const_cast<void*>(o.data)), "library opener"); lua_call(L, 0, 0); break;
    case OpSandbox: luaL_sandbox(L); break;
    case OpSandboxThread: luaL_sandboxthread(L); break;
    case OpNewFunction: {
        auto* memory = nulua_memory(L);
        if (o.b) memory->async = reinterpret_cast<lua_CFunction>(const_cast<void*>(o.data));
        else memory->sync = reinterpret_cast<lua_CFunction>(const_cast<void*>(o.data));
        lua_pushcclosurek(L, o.b ? nulua_asynccall : nulua_synccall, nullptr, o.a, nullptr);
        break;
    }
    case OpToString: o.pointer = const_cast<char*>(lua_tolstring(L, o.a, &o.size)); break;
    case OpGarbageCollect: o.integer = lua_gc(L, o.a, o.b); break;
    // Existing shims also allocate while preparing their protected call.
    case OpGetGlobal: o.integer = nulua_pgetglobal(L, static_cast<const char*>(o.data)); break;
    case OpSetGlobal: o.integer = nulua_psetglobal(L, static_cast<const char*>(o.data)); break;
    case OpGetTable: o.integer = nulua_pgettable(L, o.a); break;
    case OpSetTable: o.integer = nulua_psettable(L, o.a); break;
    case OpGetField: o.integer = nulua_pgetfield(L, o.a, static_cast<const char*>(o.data)); break;
    case OpSetField: o.integer = nulua_psetfield(L, o.a, static_cast<const char*>(o.data)); break;
    case OpConcat: o.integer = nulua_pconcat(L, o.a); break;
    case OpCompare: o.integer = nulua_pcompare(L, o.a); break;
    case OpLength: o.integer = nulua_plen(L, o.a); break;
    case OpNext: o.integer = nulua_pnext(L, o.a, &o.b); break;
    case OpCall: o.integer = lua_pcall(L, o.a, o.b, 0); break;
    case OpResume: o.integer = lua_resume(L, static_cast<lua_State*>(const_cast<void*>(o.data)), o.a); break;
    case OpBreakpoint: o.integer = lua_breakpoint(L, o.a, o.b, int(o.size)); break;
    case OpGetInfo: o.integer = lua_getinfo(L, o.a, static_cast<const char*>(o.data), reinterpret_cast<lua_Debug*>(o.size)); break;
    case OpGetLocal: o.pointer = const_cast<char*>(lua_getlocal(L, o.a, o.b)); break;
    case OpSetLocal: o.pointer = const_cast<char*>(lua_setlocal(L, o.a, o.b)); break;
    case OpGetUpvalue: o.pointer = const_cast<char*>(lua_getupvalue(L, o.a, o.b)); break;
    case OpSetUpvalue: o.pointer = const_cast<char*>(lua_setupvalue(L, o.a, o.b)); break;
    case OpGetArgument: o.integer = lua_getargument(L, o.a, o.b); break;
    default: lua_pushliteral(L, "unknown host operation"); lua_error(L);
    }
}
extern "C" int luau_memoryoperation(void* state, int operation, int a, int b,
    const void* data, size_t size, void** pointerResult, int* integerResult) {
    auto* L = static_cast<lua_State*>(state);
    NuLuaOperation o{operation, a, b, data, size};
    int status = luaD_pcall(L, nulua_memoryoperation, &o, savestack(L, L->top), 0);
    if (pointerResult) *pointerResult = o.pointer;
    if (integerResult) *integerResult = operation == OpNext ? o.b : o.integer;
    if (status == 0 && ((operation >= OpGetGlobal && operation <= OpNext) || operation == OpCall || operation == OpResume)) status = o.integer;
    return status;
}

extern "C" int luau_pload(void* state, const char* name, const char* data, size_t size) {
    auto* L = static_cast<lua_State*>(state);
    auto* memory = nulua_memory(L);
    if (memory) memory->refused = false;
    struct Load { const char* name; const char* data; size_t size; int status; } load{name, data, size, 0};
    int status = luaD_pcall(L, [](lua_State* L, void* ud) {
        auto& load = *static_cast<Load*>(ud);
        load.status = luau_load(L, load.name, load.data, load.size, 0);
    }, &load, savestack(L, L->top), 0);
    return status ? status : load.status ? (memory && memory->refused ? LUA_ERRMEM : LUA_ERRSYNTAX) : LUA_OK;
}
