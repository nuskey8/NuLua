# NuLua
 
Unified Lua5.x/LuaJIT/Luau bindings for .NET and Unity

[![NuGet](https://img.shields.io/nuget/v/NuLua.svg)](https://www.nuget.org/packages/NuLua)
[![Releases](https://img.shields.io/github/release/nuskey8/NuLua.svg)](https://github.com/nuskey8/NuLua/releases)
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

![Hero Image](assets/hero.png)

[English](README.md) | 日本語

## 概要

NuLua("new Lua"と同じように発音します)は.NET / Unity向けの新しいLuaライブラリです。C#でLuaを扱うための共通の抽象化と、各ランタイムのバインディング及び高レベルAPIを提供します。

> [!CAUTION]
> このライブラリは現在プレビュー版として提供されています。現在はWindows/macOS/Linuxのみをサポートしていますが、将来的にはiOS/Android/Webもサポートされる予定です。

## 特徴

- C#側でのヒープアロケーションを最小限に抑えた高速な設計
- モダンで扱いやすいAPI設計
- Lua5.1/5.2/5.3/5.4/5.5及びLuaJIT/Luauからバックエンドを選択可能
- async/awaitに対応

## インストール

NuLuaを利用するには.NET Standard2.1以上が必要です。

全てのパッケージはNuGetで配布されています。NuLuaを利用するにはコアパッケージに加え、利用するランタイムのパッケージを追加でインストールする必要があります。

```bash
dotnet package add NuLua
dotnet package add NuLua.Lua54 # Lua 5.4を利用する場合
```

## パッケージ構成

| パッケージ   | 最新バージョン                                                |
| ------------ | ------------------------------------------------------------- |
| NuLua        | ![NuGet Version](https://img.shields.io/nuget/v/NuLua)        |
| NuLua.Lua51  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Lua51)  |
| NuLua.Lua52  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Lua52)  |
| NuLua.Lua53  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Lua53)  |
| NuLua.Lua54  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Lua54)  |
| NuLua.Lua55  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Lua55)  |
| NuLua.LuaJit | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.LuaJit) |
| NuLua.Luau   | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Luau)   |

低レベルなバインディングAPIおよびビルド済みのネイティブバイナリも個別に追加することが可能です。これは各パッケージの依存に含まれるため、通常ユーザーが手動でインストールする必要はありません。

| パッケージ           | 最新バージョン                                                        |
| -------------------- | --------------------------------------------------------------------- |
| NuLua.Runtime.Lua51  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Lua51)  |
| NuLua.Runtime.Lua52  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Lua52)  |
| NuLua.Runtime.Lua53  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Lua53)  |
| NuLua.Runtime.Lua54  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Lua54)  |
| NuLua.Runtime.Lua55  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Lua55)  |
| NuLua.Runtime.LuaJit | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.LuaJit) |
| NuLua.Runtime.Luau   | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Runtime.Luau)   |
| NuLua.Interop.Lua51  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Lua51)  |
| NuLua.Interop.Lua52  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Lua52)  |
| NuLua.Interop.Lua53  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Lua53)  |
| NuLua.Interop.Lua54  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Lua54)  |
| NuLua.Interop.Lua55  | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Lua55)  |
| NuLua.Interop.LuaJit | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.LuaJit) |
| NuLua.Interop.Luau   | ![NuGet Version](https://img.shields.io/nuget/v/NuLua.Interop.Luau)   |

## プラットフォーム

| プラットフォーム | アーキテクチャ | 対応 |
| ---------------- | -------------- | ---- |
| Windows          | x64            | ✅    |
|                  | arm64          | ✅    |
| macOS            | x64            | ✅    |
|                  | arm64          | ✅    |
| Linux            | x64            | ✅    |
|                  | arm64          | ✅    |
| iOS              |                | 🚧    |
| Android          |                | 🚧    |
| Web              |                | 🚧    |

## クイックスタート

```cs
using NuLua;
using NuLua.Lua54;

using var state = Lua54State.Create();
state.OpenLibraries();

var results = state.DoString("return 1 + 2");
Console.WriteLine(results[0]); // 3
```

> [!WARNING]
> `ILuaState`はスレッドセーフではありません。同時に複数のスレッドからアクセスしないでください。

## LuaValue

NuLuaではLua内部の値は`LuaValue`構造体として表現されます。これは`Read<T>()`で読み取ることが可能です。

```cs
LuaValue value = state.DoString("return 42")[0];
Console.WriteLine(value.Type);        // Number
Console.WriteLine(value.Read<int>()); // 42
```

Lua-C#間の型の対応を以下に示します。

| Lua             | C#                        |
| --------------- | ------------------------- |
| `nil`           | `LuaValue.Nil`            |
| `boolean`       | `bool`                    |
| `lightuserdata` | `IntPtr`                  |
| `number`        | `double`, `float`         |
| `string`        | `string`                  |
| `table`         | `LuaTable`                |
| `function`      | `LuaFunction`             |
| `userdata`      | `T, LuaUserData`          |
| `thread`        | `ILuaState`               |
| `vector` (Luau) | `System.Numerics.Vector3` |
| `buffer` (Luau) | `LuauBuffer`              |

C#側から`LuaValue`を作成する際には、変換可能な型の場合であれば暗黙的に`LuaValue`に変換されます。

```cs
LuaValue value;
value = 1.2;                 // double   ->  LuaValue
value = "foo";               // string   ->  LuaValue
value = state.CreateTable(); // LuaTable ->  LuaValue
```

## ILuaState

NuLuaはLuaランタイムの抽象化として`ILuaState`を提供しています。

```cs
ILuaState lua55 = Lua55State.Create();
ILuaState luaJit = LuaJitState.Create();
ILuaState luau = LuauState.Create();
```

これによりバージョン間の差異を考慮することなく、バックエンドとなるランタイムの差し替えが容易に行えるようになっています。

## ライブラリ

`OpenLibraries()`を呼び出すことで標準ライブラリを追加できます。個別のライブラリを選択して追加することも可能です。

```cs
state.OpenLibraries();

state.OpenBaseLibrary();
state.OpenPackageLibrary();
state.OpenTableLibrary();
state.OpenStringLibrary();
state.OpenMathLibrary();
state.OpenCoroutineLibrary();
state.OpenIoLibrary();
state.OpenOsLibrary();
state.OpenUtf8Library();
```

## グローバル環境

インデクサを用いてLuaのグローバル環境にアクセスすることが可能です。

```cs
state.DoString("""
    foo = 10
    bar = "hello"
    """);

Console.WriteLine(state["foo"]); // 10
Console.WriteLine(state["bar"]); // hello

state["foo"] = 20;
state["bar"] = "world";

state.DoString("""
    print(foo) -- 20
    print(bar) -- bar
    """);
```

## 関数

Luaの関数は`LuaFunction`で表現されます。

### C#からLuaの関数を呼ぶ

```cs
state.DoString("""
    function add(a, b)
        return a + b
    end
    """);

LuaFunction addFunction = state["add"].Read<LuaFunction>();

var results = addFunction.Invoke(1, 2);
Console.WriteLine(results[0]); // 3
```

### LuaからC#の関数を呼ぶ

```cs
var addFunction = state.CreateFunction((state, args) => 
{
    // 引数の読み取り
    var a = args[0].Read<double>();
    var b = args[1].Read<double>();

    // 戻り値をスタックにPush
    state.Push(a + b);

    return 1; // 戻り値の数を返す
});

state["add"] = addFunction;

var results = state.DoString("""
    return add(1, 2)
    """);
Console.WriteLine(results[0]); // 3
```

関数をグローバル環境に登録する場合は`RegisterFunction()`を呼び出す方が効率的です。

```cs
state.RegisterFunction("foo", (state, args) => { ... });
```

## LuaTable

Luaのテーブルは`LuaTable`で表現されます。

```cs
var table1 = state.CreateTable();
table1[0] = "foo";
table1["a"] = "bar";

state["table1"] = table1;

var table2 = state.DoString("return { a: 10 }")[0].Read<LuaTable>();
Console.WriteLine(table2["a"]); // 10
```

## UserData

C#の構造体をUserDataとしてLuaに渡すことが可能です。UserDataとして使う構造体はunmanagedである(参照を含まない)必要があります。

UserDataを作成するには`state.CreateUserData<T>()`を利用します。戻り値の`LuaUserData`はUserDataのポインタやサイズなどの情報を保持するハンドルです。

```cs
LuaUserData userdata = state.CreateUserData<Example>(new()
{
    Foo = 5,
    Bar = 1.5,
});

struct Example
{
    public int Foo;
    public double Bar;
}
```

UserDataを表す`LuaValue`は直接`Read<T>()`で読み取ることが可能です。

```cs
var value = state["example"]; // userdata
var example = value.Read<Example>();
```

## スレッド / コルーチン

Luaのスレッドは`ILuaState`で表現されます。

`state.CreateThread()`を用いてグローバル環境を共有するスレッドを作成できます。これは独立したLuaスクリプトを複数実行する際に便利です。

```cs
var thread = state.CreateThread();
thread.DoString("return 1 + 2");
```

またLuaのコルーチンを`ILuaState`として取得し、C#側で操作することも可能です。

```lua
-- coroutine.lua

local co = coroutine.create(function()
    for i = 1, 10 do
        print(i)
        coroutine.yield()
    end
end)

return co
```

```cs
state.OpenCoroutineLibrary();

var bytes = File.ReadAllBytes("coroutine.lua");
var results = state.DoString(bytes);
var co = results[0].Read<ILuaState>();

for (int i = 0; i < 10; i++)
{
    var resumeResults = co.Resume(state);

    // coroutine.resume()と同様、成功時は最初の要素にtrue、それ以降に関数の戻り値を返す
    // 1, 2, 3, 4, ...
    Console.WriteLine(resumeResults[1]);
}
```

## モジュール解決

`LuaModuleLoader`を用いてLuaのモジュール解決をカスタム実装に置き換えることが可能です。組み込みのLoaderとして`FileSystemModuleLoader`と`InMemoryModuleLoader`が用意されています。

```cs
state.OpenPackageLibrary();

state.UseModuleLoader(new FileSystemModuleLoader("path/to/lua/modules"));
state.UseModuleLoader(new InMemoryModuleLoader(new Dictionary<string, string>
{
    ["foo"] = "return 42",
    ["bar"] = "return 'hello'",
}));
```

> [!NOTE]
> `UseModuleLoader()`を呼び出す前に`OpenPackageLibrary()`を呼び出す必要があります。Luauでは`OpenPackageLibrary()`が存在しないため、`require()`を置き換える形で動作します。

## 非同期API

Luaスクリプト自体の実行は常に同期的に終了しますが、Lua側に渡すC#関数は非同期にすることが可能です。

```cs
state.RegisterFunction("wait", async (state, args) => 
{
    var sec = args[0].Read<double>();
    await Task.Delay(TimeSpan.FromSeconds(sec));
    return 0;
});
```

非同期関数の呼び出しを含むLuaスクリプトを実行する場合、呼び出し元も非同期APIを利用する必要があります。

```cs
await state.DoStringAsync("""
    wait(2)
    print("delayed")
    """);

// これは実行時エラー
state.DoString("""
    wait(2)
    print("delayed")
    """);
```

> [!NOTE]
> metamethodに非同期関数を設定することはできません。これは常に同期関数である必要があります。

## Debug

`ILuaState.Debug`を通じてLuaのデバッグAPIにアクセスできます。

### スタック情報の取得

`GetStackDepth()`でコールスタックの深さを取得できます。`TryGetStackInfo()`で指定した階層のスタック情報を取得できます。

```cs
int depth = state.Debug.GetStackDepth();

if (state.Debug.TryGetStackInfo(0, LuaDebugInfoFields.All, out var info))
{
    Console.WriteLine(info.Name);
    Console.WriteLine(info.Source);
    Console.WriteLine(info.CurrentLine);
}
```

### ローカル変数/Upvalue

`GetLocal()`/`SetLocal()`や`GetUpvalue()`/`SetUpvalue()`でローカル変数・Upvalueの値を取得・変更できます。

```cs
// スタックに関数を積んでから
var name = state.Debug.GetUpvalue(-1, 1);
```

### フック

`SetHook()`を用いて関数呼び出しや行の実行ごとにコールバックを設定できます。

```cs
state.SetHook((s, ev, line) =>
{
    Console.WriteLine($"{ev}: {line}");
}, LuaHookMask.Line, 0);
```

フックを解除するには`null`と`LuaHookMask.None`を指定します。

```cs
state.SetHook(null, LuaHookMask.None, 0);
```

> [!NOTE]
> `SetHook()`はLuauではサポートされていません。LuauのDebug APIを利用してください。

## Garbage Collection

`ILuaState.GarbageCollection`を通じてLuaのGCを制御できます。

```cs
var before = state.GarbageCollection.GetByteCount();
state.GarbageCollection.Collect();
var after = state.GarbageCollection.GetByteCount();

// GCのステップ実行
bool finished = state.GarbageCollection.Step(1);

// GCの停止と再開
state.GarbageCollection.Stop();
Console.WriteLine(state.GarbageCollection.IsRunning()); // False
state.GarbageCollection.Restart();
```

> [!NOTE]
> `IsRunning()`はLua 5.1ではサポートされていません。

## Low-level API

`ILuaState`の低レベルAPIを呼び出すことで、スタック操作を直接行うことも可能です。

```cs
state.Push(1);
state.Push(2);
state.Arith(LuaArithOp.Add);
var result = state.ToNumber(-1);
Console.WriteLine(result); // 3

state.Push("foo");
state.Push("bar");
state.Concat(2);
var strResult = state.ToString(-1);
Console.WriteLine(strResult); // foobar
```

## LuaJIT

`NuLua.LuaJit`にはLuaJIT独自の機能に対応したAPIが追加で用意されています。

### ライブラリ

`LuaJitState`ではLuaJITの拡張ライブラリを利用可能です。

```cs
using NuLua;
using NuLua.LuaJit;

using var state = LuaJitState.Create();

state.OpenFfiLibrary();
state.OpenBitLibrary();
state.OpenJitLibrary();
```

### TrySetJitMode

`TrySetJitMode()`を用いてJITコンパイラのモードを設定することが可能です。

```cs
state.TrySetJitMode(0, LuaJitFlags.Engine | LuaJitFlags.Off);
```

## Luau

`NuLua.Luau`にはLuau独自の機能に対応したAPIが追加で用意されています。

### ライブラリ

`LuauState`ではLuauの拡張ライブラリを利用可能です。

```cs
using NuLua;
using NuLua.Luau;

using var state = LuauState.Create();
state.OpenBufferLibrary();
state.OpenVectorLibrary();
```

### LuauFeatureFlags

LuauにはいくつかのFeature Flagsが用意されています。これを有効化するには、アプリケーションの起動時に`LuauFeatureFlags`を設定してください。

```cs
LuauFeatureFlags.SetBoolean(LuauFeatureFlags.LuauConst2, true);
LuauFeatureFlags.SetBoolean(LuauFeatureFlags.LuauExportValueSyntax, true);
LuauFeatureFlags.SetInteger(LuauFeatureFlags.LuauRecursionLimit, 1000);

bool constEnabled = LuauFeatureFlags.GetBoolean(LuauFeatureFlags.LuauConst2);
int recursionLimit = LuauFeatureFlags.GetInteger(LuauFeatureFlags.LuauRecursionLimit);
```


### LuauBuffer

Luauの`buffer`型は`LuauBuffer`で表現されます。

```cs
state.OpenBufferLibrary();

var results = state.DoString("return buffer.fromstring('hello')");
var buffer = results[0].Read<LuauBuffer>();

Console.WriteLine(Encoding.UTF8.GetString(buffer.AsSpan())); // hello
```

C#側でbufferを作成することも可能です。

```cs
var buffer = state.CreateBuffer(10);

var span = buffer.AsSpan();
span[0] = (byte)'1';
span[1] = (byte)'2';
span[2] = (byte)'3';
span[3] = (byte)'4';
span[4] = (byte)'5';
"hello"u8.CopyTo(span[5..]);

state["b"] = buffer;
var results = state.DoString("return buffer.tostring(b)");
Console.WriteLine(results[0]); // 12345hello
```

### LuauCompiler

`LuauState`では`TryDump()`及び`Dump()`はサポートされていません。

```cs
using var state = LuauState.Create();
state.Dump(index, strip); // NotSupoprtedException
```

Luauコードをバイトコードに変換したい場合、代わりに`LuauCompiler`が利用できます。

```cs
byte[] bytecode = LuauCompiler.Compile("return 1 + 2");
```

### Sandbox

Luauにはスレッドをサンドボックス化するAPIが用意されています。これは`CreateSandbox()`及び`CreateSandboxThread()`で利用できます。

```cs
using var state = LuauState.CreateSandbox();
var thread = state.CreateSandboxThread();
```

### Debug

Luauでは`UpvalueId()`、`UpvalueJoin()`および`SetHook()`が利用できません。代替として、Luau独自のDebug APIが利用できます。

#### 引数の取得

`GetArgument()`は指定した階層の関数呼び出しの`n`番目の引数をスタックに積みます。戻り値は積んだ値の個数です。

```cs
int pushed = state.Debug.GetArgument(1, 1);
```

#### シングルステップ

`SetSingleStep()`を`true`に設定すると、実行する命令ごとに`SetDebugStepCallback()`で登録したコールバックが呼ばれます。これを利用するにはデバッグ情報が有効になっている必要があります。

```cs
state.Debug.SetSingleStep(true);
state.Debug.SetDebugStepCallback((s, ev, line) =>
{
    Console.WriteLine($"step: {line}");
});

// コールバックを解除するにはnullを渡します
state.Debug.SetDebugStepCallback(null);
state.Debug.SetSingleStep(false);
```

#### ブレークポイント

`SetBreakpoint()`を使用すると、スタック上の関数にブレークポイントを設定できます。`funcIndex`は関数が積まれているスタックインデックス、`line`は行番号、`enabled`は有効/無効を指定します。

```cs
state.Debug.SetBreakpoint(-1, 10, true);
```

`SetDebugBreakCallback()`にはBREAK命令に到達した際に呼ばれるコールバックを登録できます。

```cs
state.Debug.SetDebugBreakCallback((s, ev, line) =>
{
    Console.WriteLine($"break: {line}");
});
```

#### デバッグトレース

`GetDebugTrace()`は現在のコールスタックを文字列として取得します。

```cs
string trace = state.Debug.GetDebugTrace();
Console.WriteLine(trace);
```

#### カバレッジ

`GetCoverage()`は指定した関数の実行回数を行単位で収集できます。

```cs
state.Debug.GetCoverage(-1, entry =>
{
    Console.WriteLine($"function: {entry.Function}, line: {entry.LineDefined}");
    for (int i = 0; i < entry.Hits.Length; i++)
    {
        Console.WriteLine($"  line {entry.LineDefined + i}: {entry.Hits[i]}");
    }
});
```

#### スレッド中断コールバック

`SetDebugInterruptCallback()`には、他のスレッドの実行が中断された際に呼ばれるコールバックを登録できます。

```cs
state.Debug.SetDebugInterruptCallback((s, ev, line) =>
{
    Console.WriteLine("interrupted");
});
```

## Unity

TODO

## ライセンス

このライブラリは[MIT License](LICENSE)の下で公開されています。
