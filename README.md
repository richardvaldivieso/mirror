# Mirror

A tiny, fast C# object-mapper that favors **clear conventions** and **simple configuration**.  
Mirror compiles per-type mapping delegates using **expression trees** (no runtime per-call reflection), supports **nested objects**, **collections**, and common type conversions (e.g., `int? → int`, `enum ⇄ string`, `Nullable<Enum>` with null safety).

Mirror offers **two ways** to configure mappings:

1) **Manual configuration** via `MirrorConfig` (explicit and code-centric)  
2) **Registry configuration** via small `IConfiguration<TSource, TDest>` classes + `MirrorRegistry` (convenient one-liners like `dto.MirrorCopy()`)

---

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Core Concepts](#core-concepts)
- [Approach A: Manual configuration (`MirrorConfig`)](#approach-a-manual-configuration-mirrorconfig)
  - [Quick start](#quick-start)
  - [API surface](#api-surface)
  - [Examples](#examples)
- [Approach B: Registry configuration (`IConfiguration` + `MirrorRegistry`)](#approach-b-registry-configuration-iconfiguration--mirrorregistry)
  - [Quick start](#quick-start-1)
  - [Registry API](#registry-api)
  - [Examples](#examples-1)
- [Behavior & Rules](#behavior--rules)
- [Performance](#performance)
- [Thread Safety](#thread-safety)
- [Error Handling](#error-handling)
- [What Mirror Does / Does Not](#what-mirror-does--does-not)
- [FAQ](#faq)
- [Roadmap](#roadmap)
- [License](#license)

---

## Features

- **Convention-based mapping**: copies members with the **same name** when the types are assignable or convertible.
- **Overrides where you need them**:
  - `Ignore(...)` to skip destination members
  - `ForMember(...)` to set a destination member from an expression
- **Nested mapping**: automatically maps nested complex objects using registered type pairs.
- **Collections**: maps `IEnumerable<S>` → `IEnumerable<D>` and materializes `List<D>` when the destination expects it.
- **Common conversions**:
  - `int ↔ int?` (null → default for non-nullable)
  - `enum ⇄ string` (case-insensitive parse for strings; null strings handled)
  - `string → Nullable<Enum>` (null stays null)
- **Two configuration styles**:
  - **Manual** (explicit, programmatic) with `MirrorConfig`
  - **Registry** (declarative, class-based) with `IConfiguration<TSource,TDest>` + `MirrorRegistry`
- **Ergonomic copy**:
  - `Copy<TSource, TDest>(source)` – strongest compile-time safety
  - `Copy<TDest>(object)` – specify only the destination type
  - `obj.MirrorCopy()` – registry pattern: infer destination type from registered config
- **No source generators required** (but future-friendly)

---

## Installation

This library is designed as a **drop-in** set of `.cs` files:

- `MirrorConfig.cs` (configuration registry for plans)
- `MirrorMap.cs` (per-map rules: ignores, resolvers; holds back-reference to config)
- `MirrorEngine.cs` (runtime mapper that builds/compiles delegates and executes maps)
- `MirrorExtensions.cs` (e.g., `Reflect()` for reverse maps, convenience extensions)
- *(optional)* `MirrorRegistry.cs` + `MirrorRegistryExtensions.cs` + your `IConfiguration<TSource,TDest>` classes

> .NET: tested with **.NET 8**; should work on .NET 6+.

---

## Core Concepts

- **Plan**: A mapping plan ties `(SourceType, DestType)` to rules (ignores/resolvers). Plans live in `MirrorConfig`.
- **Build**: At runtime, Mirror builds **compiled delegates** (via expressions) for each `(source, dest)` used. These are cached.
- **Manual vs Registry**: You can either declare maps directly with `MirrorConfig` (Approach A), or expose small configuration classes the `MirrorRegistry` consumes (Approach B). Both share the same engine.

---

## Approach A: Manual configuration (`MirrorConfig`)

### Quick start

```csharp
// 1) Define your types
public enum Role { User, Admin }

public sealed class UserDto { public string FirstName { get; set; } public string LastName { get; set; } public int Age { get; set; } public Role Role { get; set; } }
public sealed class User
{
    public string FirstName { get; set; }
    public string LastName  { get; set; }
    public int? Age         { get; set; }  // nullable in dest
    public string Role      { get; set; }  // enum -> string
    public string FullName  { get; set; }  // custom resolver
    public string SecretNote { get; set; } // ignored
}

// 2) Configure maps
var cfg = new MirrorConfig();
cfg.CreateMirror<UserDto, User>(m =>
{
    m.Ignore(d => d.SecretNote);
    m.ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName);
});

// Optional: element maps for nested objects, collections, etc.
// cfg.CreateMirror<AddressDto, Address>();

// Optional: enable reverse with Reflect()
// cfg.CreateMirror<UserDto, User>(...).Reflect();

// 3) Build engine (thread-safe, app-wide singleton is common)
var mirror = cfg.Build();

// 4) Use it
var dto = new UserDto { FirstName = "Ana", LastName = "V", Age = 29, Role = Role.Admin };
var userA = mirror.Copy<UserDto, User>(dto); // fully generic
var userB = mirror.Copy<User>(dto);          // destination-only generic
```

### API surface

- **`MirrorConfig`**
  - `CreateMirror<TSource, TDest>()`
  - `CreateMirror<TSource, TDest>(Action<MirrorMap<TSource, TDest>> configure)`
  - `Build(): MirrorEngine`
  - *(internal)* `Register(...)`, `TryGetPlan(...)` – used by engine and extensions.

- **`MirrorMap<TSource, TDest>`**
  - `Ignore(destMember)` – skip writing to a destination member
  - `ForMember(destMember, resolver)` – set a destination member via `Expression<Func<TSource, TMember>>`
  - *(has internal `Parent` back-reference to its `MirrorConfig`)*
  - **`Reflect()`** *(extension method)* – register reverse map (`TDest → TSource`), carrying over ignores.

- **`MirrorEngine`**
  - `Copy<TSource, TDest>(TSource source)`
  - `Copy<TDest>(object source)` – infers source type at runtime
  - `CopyMany<TSource, TDest>(IEnumerable<TSource> source)`

### Examples

#### Nested object + collection
```csharp
public sealed class ChildDto { public string Name { get; set; } }
public sealed class ParentDto { public List<ChildDto> Kids { get; set; } }

public sealed class Child { public string Name { get; set; } }
public sealed class Parent { public List<Child> Kids { get; set; } }

var cfg = new MirrorConfig();
cfg.CreateMirror<ChildDto, Child>();
cfg.CreateMirror<ParentDto, Parent>();

var mirror = cfg.Build();

var parent = mirror.Copy<Parent>(new ParentDto {
  Kids = new() { new ChildDto { Name = "Ava" }, new ChildDto { Name = "Milo" } }
});
// parent.Kids is List<Child> with "Ava","Milo"
```

#### In an ASP.NET Core controller
```csharp
[HttpPost("/api/users")]
public IActionResult Create([FromBody] UserDto dto, [FromServices] IMirror mirror)
{
    var user = mirror.Copy<User>(dto);
    Console.WriteLine($"save {user.FullName} ({user.Role}), age={user.Age}");
    return Ok(new { message = "saved", user });
}
```

---

## Approach B: Registry configuration (`IConfiguration` + `MirrorRegistry`)

This style lets you define **small config classes** that describe a pair, and then call **`MirrorCopy()`** directly on objects with **no generics** and **no engine parameter**.

### Quick start

1) **Create config classes**

```csharp
public interface IConfiguration<TSource, TDest>
{
    void Configure(MirrorMap<TSource, TDest> map);
    bool Reflect { get; } // auto-register reverse
}

public sealed class UserConfiguration : IConfiguration<UserDto, User>
{
    public bool Reflect => true; // also register User -> UserDto
    public void Configure(MirrorMap<UserDto, User> map)
    {
        map.Ignore(d => d.SecretNote)
           .ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName);
    }
}

public sealed class AddressConfiguration : IConfiguration<AddressDto, Address>
{
    public bool Reflect => true;
    public void Configure(MirrorMap<AddressDto, Address> map) { }
}
```

2) **Wire the registry at startup**

```csharp
MirrorRegistry.Start(cfg =>
{
    cfg.Add<UserDto, User, UserConfiguration>();
    cfg.Add<AddressDto, Address, AddressConfiguration>();
});
```

3) **Use the one-liner anywhere**

```csharp
var dto  = new UserDto { FirstName="Ana", LastName="V", Age=29, Role=Role.Admin };
var user = (User)dto.MirrorCopy();     // forward, inferred from registry
var dto2 = (UserDto)user.MirrorCopy(); // reverse, because Reflect=true
```

### Registry API

- **`MirrorRegistry.Start(Action<MirrorConfig> configure)`**  
  Creates a `MirrorConfig`, lets you `Add<TSource,TDest,TConfig>()` pairs, builds an internal `MirrorEngine`.

- **`MirrorConfig.Add<TSource,TDest,TConfig>()`**  
  Registers the pair using the supplied configuration class; if `Reflect==true`, registers the reverse.

- **`object MirrorRegistry.CopyAuto(object source)`** *(internal)*  
  Used by the extension to pick forward or reverse destination type based on the source runtime type.

- **`object MirrorCopy(this object source)`** *(extension)*  
  One-liner you call on any registered type; returns `object` (cast to the expected type).

> The registry keeps two internal maps: **forward** (`source → dest`) and **reverse** (`dest → source`) to support `MirrorCopy()` from either side.

### Examples

#### Minimal DTO ↔ entity

```csharp
var saved = (User) new UserDto { FirstName="Pat", LastName="Lee" }.MirrorCopy();
// same for reverse:
var dto = (UserDto) saved.MirrorCopy();
```

#### Using both APIs together

The registry is optional sugar. You can still inject/use `IMirror`:

```csharp
var mirror = new MirrorConfig()
    .CreateMirror<UserDto, User>(m => m.ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName))
    .Build();

var userA = mirror.Copy<User>(dto);     // engine path
var userB = (User)dto.MirrorCopy();     // registry path
```

---

## Behavior & Rules

- **Member matching**: by **exact name** (case-sensitive) for public instance properties/fields. Destination must be settable.
- **Missing sources**: if a destination member has no matching source and no resolver, it’s skipped (no error).
- **Ignores**: `Ignore(d => d.Prop)` skips that destination member.
- **Resolvers**: `ForMember(d => d.Prop, s => expr)` can use any expression over `TSource` (closures captured as constants).
- **Constructors**: Destination is created with parameterless constructor if available; otherwise Mirror chooses a “best” constructor with default values for its parameters, then assigns members after construction. It does **not** (yet) bind constructor arguments from source.
- **Collections**: `IEnumerable<S> → IEnumerable<D>` is projected via element map. If destination type is `List<D>`, Mirror materializes the sequence into a `List<D>`.
- **Enums & strings**:
  - `enum → string` uses `ToString()`.
  - `string → enum` uses `Enum.Parse(enumType, string, ignoreCase: true)` with **null-safe** handling.
  - `string → Nullable<Enum>`: `null` stays `null`.
- **Nullables**:
  - `T? → T`: if `null`, destination gets `default(T)`; otherwise value is assigned/converted.
  - `T → T?`: value is wrapped.
- **Reverse maps**: Use `.Reflect()` on the forward map or set `Reflect => true` in registry configuration to register the reverse pair. Resolvers are **not auto-inverted**.

---

## Performance

- Each `(source, dest)` is compiled **once** into a `Func<TSource, MirrorEngine, TDest>` and cached.
- `Copy<TDest>(object)` uses a tiny **adapter cache** per type pair to avoid `DynamicInvoke`.
- Steady-state mappings avoid reflection on the hot path.

---

## Thread Safety

- The engine’s delegate cache and adapters use `ConcurrentDictionary`. Once built, the engine is **safe to use concurrently** across threads.
- Configure maps **once at startup**, build a single `MirrorEngine`, and register it as a singleton if using DI.

---

## Error Handling

- **No plan found** (manual mode): Mirror falls back to **convention-only** mapping for that pair (no ignores/resolvers).
- **No registry entry** (registry mode): `MirrorCopy()` throws `InvalidOperationException` with a clear message.
- **Constructor issues**: If a destination type has **no usable constructor**, Mirror throws an `InvalidOperationException`.

---

## What Mirror Does / Does Not

### Does
- Copy properties/fields by **name** with sensible conversions (including enums/strings and nullables).
- Map **nested objects** and **collections** (when element maps exist).
- Allow **overrides** via `Ignore` and `ForMember`.
- Let you choose between **explicit** (engine) and **convenience** (registry) workflows.

### Does Not (by design)
- Bind destination constructors from source members (no constructor parameter mapping yet).
- Auto-invert custom `ForMember` resolvers for reverse maps.
- Rename members via global naming strategies out of the box (you can add a naming policy later).
- Perform validation of unmapped members (you can add diagnostics if you want stricter behavior).
- Generate code at compile time (no source generator in this version).

---

## FAQ

**Q: Do I need both the manual and registry approaches?**  
A: No. Use the manual approach if you prefer explicit control (especially in libraries). Use the registry approach for ergonomic app code (`obj.MirrorCopy()`).

**Q: Do I need to create maps for nested element types?**  
A: Yes. If `Parent` contains `List<Child>`, and your source has `List<ChildDto>`, you should register `ChildDto → Child`.

**Q: Can I only specify the destination type when copying?**  
A: Yes: `mirror.Copy<TDest>(source)` infers the source at runtime and uses the same compiled map.

**Q: How do I register reverse maps?**  
A: Call `.Reflect()` off a forward map (`CreateMirror<A,B>(...).Reflect()`), or set `Reflect => true` in your `IConfiguration<A,B>` class for the registry approach.

**Q: Will `ForMember` closures work with services (like a clock)?**  
A: Yes. Captured values are embedded as constants in the expression tree used to compile the mapping.

---

## Roadmap

- Constructor parameter binding (map source members into destination ctors).
- Global naming policies (e.g., snake_case ↔ PascalCase).
- Pluggable value converters (global and per-member).
- Optional diagnostics/validation modes (warn/error on unmapped members).
- Roslyn source generator (optional compile-time maps).

---

## License

MIT

---

### Appendix: Minimal Controller Example

```csharp
// Program.cs
var cfg = new MirrorConfig();
cfg.CreateMirror<UserDto, User>(m =>
{
    m.Ignore(d => d.SecretNote);
    m.ForMember(d => d.FullName, s => s.FirstName + " " + s.LastName);
});
var mirror = cfg.Build();

builder.Services.AddSingleton<IMirror>(mirror);
builder.Services.AddControllers();

// UsersController.cs
[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IMirror _mirror;
    public UsersController(IMirror mirror) => _mirror = mirror;

    [HttpPost]
    public IActionResult Create([FromBody] UserDto dto)
    {
        var user = _mirror.Copy<User>(dto);
        Console.WriteLine($"save: {user.FullName} ({user.Role}) age={user.Age}");
        return Ok(user);
    }
}
```
