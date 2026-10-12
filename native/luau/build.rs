use std::path::{Path, PathBuf};

mod feature_flags;

const FLAVOR: &str = "luau";
const CSHARP_NAMESPACE: &str = "NuLua.Interop.Luau";
const CSHARP_CLASS: &str = "NativeMethods";
const CSHARP_PROJECT: &str = "NuLua.Interop.Luau";

fn main() {
    let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let workspace_root = manifest_dir
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf();
    let vm_include = workspace_root
        .join("submodules")
        .join("luau")
        .join("VM")
        .join("include");
    let compiler_include = workspace_root
        .join("submodules")
        .join("luau")
        .join("Compiler")
        .join("include");
    let shim_include = manifest_dir.join("shim");
    let common_shim_include = workspace_root.join("native").join("shim");
    let out_dir = PathBuf::from(std::env::var("OUT_DIR").unwrap());

    let bindings_rs = out_dir.join(format!("{FLAVOR}.rs"));
    generate_rust_bindings(&vm_include, &compiler_include, &shim_include, &bindings_rs);

    let cs_out = workspace_root
        .join("src")
        .join(CSHARP_PROJECT)
        .join("Generated")
        .join(format!("{CSHARP_CLASS}.g.cs"));
    let ffi_rs = out_dir.join(format!("{FLAVOR}_ffi.rs"));
    generate_csharp_bindings(&bindings_rs, &ffi_rs, &cs_out);
    feature_flags::generate(&workspace_root);

    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed={}", vm_include.display());
    println!("cargo:rerun-if-changed={}", compiler_include.display());
    println!("cargo:rerun-if-changed={}", shim_include.display());
    println!("cargo:rerun-if-changed={}", common_shim_include.display());
}

fn generate_rust_bindings(
    vm_include: &Path,
    compiler_include: &Path,
    shim_include: &Path,
    out: &Path,
) {
    let bindings = bindgen::Builder::default()
        .header(vm_include.join("lua.h").to_string_lossy())
        .header(vm_include.join("lualib.h").to_string_lossy())
        .header(compiler_include.join("luacode.h").to_string_lossy())
        .header(shim_include.join("luau_shim.h").to_string_lossy())
        .clang_arg(format!("-I{}", vm_include.display()))
        .clang_arg(format!("-I{}", compiler_include.display()))
        .clang_arg(format!("-I{}", shim_include.display()))
        // Luau headers use C++ extern "C" guards but compile cleanly as C.
        .clang_arg("-x")
        .clang_arg("c")
        .allowlist_function("lua_.*")
        .allowlist_function("luaL_.*")
        .allowlist_function("luaopen_.*")
        .allowlist_function("luau_.*")
        .allowlist_function("nulua_.*")
        .allowlist_type("lua_.*")
        .allowlist_type("luaL_.*")
        .allowlist_var("LUA_.*")
        .allowlist_var("LUAL_.*")
        .layout_tests(false)
        .generate()
        .expect("bindgen failed");
    bindings.write_to_file(out).expect("write bindings.rs");
}

fn generate_csharp_bindings(bindings_rs: &Path, ffi_rs: &Path, out_cs: &Path) {
    std::fs::create_dir_all(out_cs.parent().unwrap()).expect("mkdir Generated/");
    csbindgen::Builder::default()
        .input_bindgen_file(bindings_rs)
        .rust_file_header(format!("use crate::{FLAVOR}::*;"))
        .csharp_class_accessibility("public")
        .csharp_file_header("using NuLua.Polyfills;")
        .csharp_dll_name(FLAVOR)
        .csharp_namespace(CSHARP_NAMESPACE)
        .csharp_class_name(CSHARP_CLASS)
        .csharp_use_function_pointer(false)
        .csharp_entry_point_prefix("")
        .csharp_generate_const_filter(|name| name.starts_with("LUA_"))
        .generate_to_file(ffi_rs.to_str().unwrap(), out_cs.to_str().unwrap())
        .expect("csbindgen failed");
}
