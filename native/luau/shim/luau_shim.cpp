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
