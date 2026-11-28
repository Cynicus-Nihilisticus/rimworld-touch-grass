# ApiCheck

Answers one question after every RimWorld update: **does the compiled mod still
fit the installed game?** It reads the mod DLL and the game's assemblies as
plain files (it never loads or runs either) and needs only Python 3.8+.

```powershell
python -I tools\ApiCheck\apicheck.py `
  --dll 1.7\Assemblies\TouchGrass.dll `
  --game "C:\Program Files (x86)\Steam\steamapps\common\RimWorld" `
  --harmony "C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll"
```

`Build.ps1` runs it automatically after compiling (skip with `-SkipApiCheck`).

It checks that

1. every type and member the mod uses from RimWorld, Unity and Harmony still
   exists with the same signature;
2. every override still matches a base member, and no new abstract member was
   added to a class the mod extends;
3. every `[HarmonyPatch]` target still exists and the parameters Harmony
   injects by name (`leftX`, `curBaseY`, ...) are still on it;
4. members reached by reflection (`REFLECTION_TARGETS` in `apicheck.py`) still
   exist;
5. (warning) none of the Unity API used is a logging stub or throws
   `NotImplementedException` in 1.7's Unity compatibility layer.

Exit status is 0 when nothing needs fixing and 1 otherwise. A pass proves the
API still lines up; it cannot prove behaviour, so still try the mod in game.

When the mod starts reaching a new private member by reflection, add it to
`REFLECTION_TARGETS`.
