# IL2CPP

Code runs against generated interop assemblies.

- Game types keep their names; BCL types used by the game are under `Il2CppSystem` (`Il2CppSystem.Collections.Generic.List<T>`).
- Strings are managed `string`. Fields are properties; private members are public.
- Casts: `obj.TryCast<T>()` (null if not a `T`) or `obj.Cast<T>()`. C# casts only see the wrapper type.
- Destroyed Unity objects compare equal to `null`.
- Arrays: `Length`, indexer, `foreach`, LINQ. `Il2CppSystem` lists and dictionaries: `Count`, indexer, `foreach`; no LINQ.
- `Type` parameters: `Il2CppInterop.Runtime.Il2CppType.Of<T>()`.
- Delegates: cast a lambda, e.g. `(UnityEngine.Events.UnityAction)(() => ...)`, or `Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>(d)`.
- Engine APIs only on the main thread.
