# Builds per-flavor Lua shared libraries that are shipped directly to
# src/NuLua.Runtime.{Display}/runtimes/{rid}/native/ and loaded by .NET P/Invoke.
#
# Required inputs from the caller:
#   RID           target RID, e.g. linux-arm64, osx-x64, win-x64
#   TARGET_OS     macos | linux | windows | posix
#   CC, AR, RANLIB
#   CXX           (luau only) g++/clang++
# Optional:
#   CFLAGS_EXTRA   appended to CFLAGS (e.g. "-arch arm64" on macOS multi-arch builds)
#   CXXFLAGS_EXTRA appended to CXXFLAGS (luau)
#   LDFLAGS_EXTRA  appended to LDFLAGS
#   HOST_CC        (luajit only) host compiler for buildvm; defaults to cc
#   CROSS          (luajit only) cross-prefix for TARGET_CC, e.g. aarch64-linux-gnu-
#
# Output: $(BUILD)/$(FLAVOR)/$(RID)/$(DYLIB_NAME)
#   linux  -> lib{flavor}.so
#   macos  -> lib{flavor}.dylib
#   windows-> {flavor}.dll

SUBMOD := ../submodules
BUILD  := build

C_FLAVORS   := lua51 lua52 lua53 lua54 lua55
ALL_FLAVORS := $(C_FLAVORS) luau luajit
NULUA_SHIM  := shim/nulua_shim.c

SKIP_lua51 := lua.c luac.c print.c
SKIP_lua52 := lua.c luac.c
SKIP_lua53 := lua.c luac.c
SKIP_lua54 := lua.c luac.c ltests.c onelua.c
SKIP_lua55 := lua.c luac.c ltests.c onelua.c

PLATFORM_DEFINE := $(if $(filter macos,$(TARGET_OS)),-DLUA_USE_MACOSX,\
                  $(if $(filter linux,$(TARGET_OS)),-DLUA_USE_LINUX,\
                  $(if $(filter windows,$(TARGET_OS)),-DLUA_BUILD_AS_DLL,-DLUA_USE_POSIX)))
NULUA_API_DEFINE := $(if $(filter windows,$(TARGET_OS)),-DNULUA_SHIM_API='extern __declspec(dllexport)',)

CFLAGS  := -O2 -fPIC -w $(PLATFORM_DEFINE) $(NULUA_API_DEFINE) $(CFLAGS_EXTRA)
LDFLAGS := $(LDFLAGS_EXTRA)

# Shared-library link flags + filename per OS.
ifeq ($(TARGET_OS),macos)
  SHARED_FLAGS  := -dynamiclib
  PLATFORM_LIBS := -lm
  dylib_name     = lib$1.dylib
else ifeq ($(TARGET_OS),linux)
  SHARED_FLAGS  := -shared
  PLATFORM_LIBS := -lm -ldl
  dylib_name     = lib$1.so
else ifeq ($(TARGET_OS),windows)
  SHARED_FLAGS  := -shared
  PLATFORM_LIBS :=
  dylib_name     = $1.dll
else
  SHARED_FLAGS  := -shared
  PLATFORM_LIBS := -lm
  dylib_name     = lib$1.so
endif

# -----------------------------------------------------------------------------
# lua51..lua55 (flat C sources)
# -----------------------------------------------------------------------------

flavor_srcs = $(filter-out $(addprefix $(SUBMOD)/$1/,$(SKIP_$1)),$(wildcard $(SUBMOD)/$1/*.c))
flavor_objs = $(patsubst $(SUBMOD)/$1/%.c,$(BUILD)/$1/$(RID)/%.o,$(call flavor_srcs,$1)) $(BUILD)/$1/$(RID)/shim/nulua_shim.o

define flavor_rules
$(BUILD)/$1/$(RID)/%.o: $(SUBMOD)/$1/%.c
	@mkdir -p $$(@D)
	$$(CC) $$(CFLAGS) -I$(SUBMOD)/$1 -c $$< -o $$@

$(BUILD)/$1/$(RID)/shim/nulua_shim.o: $(NULUA_SHIM)
	@mkdir -p $$(@D)
	$$(CC) $$(CFLAGS) -I$(SUBMOD)/$1 -Ishim -c $$< -o $$@

$(BUILD)/$1/$(RID)/$(call dylib_name,$1): $(call flavor_objs,$1)
	$$(CC) $(SHARED_FLAGS) $$(LDFLAGS) -o $$@ $$^ $(PLATFORM_LIBS)

$1: $(BUILD)/$1/$(RID)/$(call dylib_name,$1)
.PHONY: $1
endef

$(foreach f,$(C_FLAVORS),$(eval $(call flavor_rules,$f)))

# -----------------------------------------------------------------------------
# luau (C++ VM + Compiler compiled directly from submodules/luau/*/src/*.cpp)
#
# We co-locate the VM and Compiler (with its Ast/Bytecode/Common dependencies)
# in a single shared library so that both `lua_*` / `luau_*` runtime symbols
# and `luau_compile` are exported from the produced dylib. LUA_API / LUACODE_API
# are configured for the SHARED build used by Luau's own CMakeLists.
# -----------------------------------------------------------------------------

LUAU_VM_SRC       := $(SUBMOD)/luau/VM/src
LUAU_COMPILER_SRC := $(SUBMOD)/luau/Compiler/src
LUAU_AST_SRC      := $(SUBMOD)/luau/Ast/src
LUAU_BYTECODE_SRC := $(SUBMOD)/luau/Bytecode/src
LUAU_COMMON_SRC   := $(SUBMOD)/luau/Common/src
LUAU_SHIM_SRC     := luau/shim

LUAU_INCS := \
  -I$(SUBMOD)/luau/VM/include \
  -I$(LUAU_VM_SRC) \
  -I$(SUBMOD)/luau/Compiler/include \
  -I$(LUAU_COMPILER_SRC) \
  -I$(SUBMOD)/luau/Ast/include \
  -I$(SUBMOD)/luau/Bytecode/include \
  -I$(SUBMOD)/luau/Common/include \
  -I$(LUAU_SHIM_SRC)

LUAU_VM_SRCS       := $(wildcard $(LUAU_VM_SRC)/*.cpp)
LUAU_COMPILER_SRCS := $(wildcard $(LUAU_COMPILER_SRC)/*.cpp)
LUAU_AST_SRCS      := $(wildcard $(LUAU_AST_SRC)/*.cpp)
LUAU_BYTECODE_SRCS := $(wildcard $(LUAU_BYTECODE_SRC)/*.cpp)
LUAU_COMMON_SRCS   := $(wildcard $(LUAU_COMMON_SRC)/*.cpp)
LUAU_SHIM_SRCS     := $(wildcard $(LUAU_SHIM_SRC)/*.cpp)

LUAU_OBJS := \
  $(patsubst $(LUAU_VM_SRC)/%.cpp,$(BUILD)/luau/$(RID)/vm/%.o,$(LUAU_VM_SRCS)) \
  $(patsubst $(LUAU_COMPILER_SRC)/%.cpp,$(BUILD)/luau/$(RID)/compiler/%.o,$(LUAU_COMPILER_SRCS)) \
  $(patsubst $(LUAU_AST_SRC)/%.cpp,$(BUILD)/luau/$(RID)/ast/%.o,$(LUAU_AST_SRCS)) \
  $(patsubst $(LUAU_BYTECODE_SRC)/%.cpp,$(BUILD)/luau/$(RID)/bytecode/%.o,$(LUAU_BYTECODE_SRCS)) \
  $(patsubst $(LUAU_COMMON_SRC)/%.cpp,$(BUILD)/luau/$(RID)/common/%.o,$(LUAU_COMMON_SRCS)) \
  $(patsubst $(LUAU_SHIM_SRC)/%.cpp,$(BUILD)/luau/$(RID)/shim/%.o,$(LUAU_SHIM_SRCS))

ifeq ($(TARGET_OS),windows)
  LUAU_API_DEFS := \
    -DLUA_API='extern "C" __declspec(dllexport)' \
    -DLUACODE_API='extern "C" __declspec(dllexport)' \
    -DLUAU_SHIM_API='extern "C" __declspec(dllexport)' \
    -DNULUA_SHIM_API='extern "C" __declspec(dllexport)'
else
  LUAU_API_DEFS := \
    -DLUA_API='extern "C" __attribute__((visibility("default")))' \
    -DLUACODE_API='extern "C" __attribute__((visibility("default")))' \
    -DLUAU_SHIM_API='extern "C" __attribute__((visibility("default")))'
endif

# On Windows, statically link the C++ runtime so the produced luau.dll has no
# external dependency on libc++.dll / libunwind.dll from the llvm-mingw toolchain.
ifeq ($(TARGET_OS),windows)
  LUAU_LDFLAGS_EXTRA := -static-libgcc -static-libstdc++ \
                        -Wl,-Bstatic -lc++ -lunwind -Wl,-Bdynamic
else
  LUAU_LDFLAGS_EXTRA :=
endif

CXXFLAGS_LUAU := -O2 -fPIC -w -std=c++17 -DNULUA_LUAU $(LUAU_INCS) $(LUAU_API_DEFS) $(CFLAGS_EXTRA) $(CXXFLAGS_EXTRA)

$(BUILD)/luau/$(RID)/vm/%.o: $(LUAU_VM_SRC)/%.cpp
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/compiler/%.o: $(LUAU_COMPILER_SRC)/%.cpp
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/ast/%.o: $(LUAU_AST_SRC)/%.cpp
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/bytecode/%.o: $(LUAU_BYTECODE_SRC)/%.cpp
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/common/%.o: $(LUAU_COMMON_SRC)/%.cpp
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/shim/%.o: $(LUAU_SHIM_SRC)/%.cpp $(LUAU_SHIM_SRC)/luau_shim.h $(NULUA_SHIM) shim/nulua_shim.h
	@mkdir -p $(@D)
	$(CXX) $(CXXFLAGS_LUAU) -c $< -o $@

$(BUILD)/luau/$(RID)/$(call dylib_name,luau): $(LUAU_OBJS)
	$(CXX) $(SHARED_FLAGS) $(LDFLAGS) -o $@ $^ $(PLATFORM_LIBS) $(LUAU_LDFLAGS_EXTRA)

luau: $(BUILD)/luau/$(RID)/$(call dylib_name,luau)
.PHONY: luau

# -----------------------------------------------------------------------------
# luajit (delegate to submodules/luajit/src/Makefile + rename artifact)
#
# LuaJIT's own Makefile handles buildvm, dynasm, and platform-specific link.
# We force `BUILDMODE=dynamic` so only the shared library is built, then copy
# the produced artifact into our build/ tree under the canonical {flavor} name.
# -----------------------------------------------------------------------------

LUAJIT_SRC := $(SUBMOD)/luajit/src
LUAJIT_OUT := $(BUILD)/luajit/$(RID)

LUAJIT_TARGET_SYS := $(strip $(if $(filter macos,$(TARGET_OS)),Darwin,\
                    $(if $(filter linux,$(TARGET_OS)),Linux,\
                    $(if $(filter windows,$(TARGET_OS)),Windows,Other))))

# LuaJIT prepends CROSS to CC. We pass a bare compiler name so the result is
# `<prefix>gcc` (Linux/Windows cross-toolchain layout). On macOS use clang
# explicitly: Apple's `gcc` is a clang shim but a local Homebrew GCC can
# shadow it and silently ignore `-arch`.
ifeq ($(TARGET_OS),macos)
  LUAJIT_CC := clang
else
  LUAJIT_CC := gcc
endif

# Produced-artifact path inside submodules/luajit/src/ (varies per platform).
ifeq ($(TARGET_OS),macos)
  LUAJIT_BUILT := $(LUAJIT_SRC)/libluajit.so
else ifeq ($(TARGET_OS),windows)
  LUAJIT_BUILT := $(LUAJIT_SRC)/luajit.dll
else
  LUAJIT_BUILT := $(LUAJIT_SRC)/libluajit.so
endif

LUAJIT_FORWARD := \
  BUILDMODE=dynamic \
  HOST_CC="$(or $(HOST_CC),cc)" \
  CROSS="$(CROSS)" \
  CC=$(LUAJIT_CC) \
  TARGET_SYS=$(LUAJIT_TARGET_SYS) \
  TARGET_FLAGS="$(CFLAGS_EXTRA)" \
  TARGET_DLLNAME=luajit.dll

LUAJIT_SHIM_O := $(LUAJIT_OUT)/nulua_shim.o

$(LUAJIT_SHIM_O): $(NULUA_SHIM)
	@mkdir -p $(@D)
	$(CC) $(CFLAGS) -I$(LUAJIT_SRC) -Ishim -c $< -o $@

# LuaJIT's Makefile aborts on macOS unless MACOSX_DEPLOYMENT_TARGET is exported.
# Provide a sensible default that the workflow can override.
ifeq ($(TARGET_OS),macos)
  export MACOSX_DEPLOYMENT_TARGET ?= 11.0
endif

luajit: $(LUAJIT_SHIM_O)
	@mkdir -p $(LUAJIT_OUT)
	$(MAKE) -C $(LUAJIT_SRC) $(LUAJIT_FORWARD) clean
	$(MAKE) -C $(LUAJIT_SRC) $(LUAJIT_FORWARD) TARGET_XLIBS="$(abspath $(LUAJIT_SHIM_O)) -lm"
	cp $(LUAJIT_BUILT) $(LUAJIT_OUT)/$(call dylib_name,luajit)
ifeq ($(TARGET_OS),macos)
	install_name_tool -id @rpath/libluajit.dylib $(LUAJIT_OUT)/libluajit.dylib
endif
.PHONY: luajit

# -----------------------------------------------------------------------------
# Aggregate targets
# -----------------------------------------------------------------------------

all: $(ALL_FLAVORS)
.PHONY: all

clean:
	rm -rf $(BUILD)
	-$(MAKE) -C $(LUAJIT_SRC) clean
.PHONY: clean
