# Local patches to the vendored AdMob addon

Poing Studios AdMob plugin **v5.0.0**, installed from
<https://github.com/poingstudios/godot-admob-plugin/releases/tag/v5.0.0>
(`poing-godot-admob-v5.0.0.zip`, sha256 `93e9aaa8422b00f783c6b8c06a9e2ae3a81db071515517bb061bbb2d202d5fdf`).
Android build template `android-template-v4.7.0.zip`, sha256
`0d504a40b92db1abfdf839724c3a962a56569def93dbc608c9537fa38710e65a`, extracted into `android/`.

**Re-apply everything below after upgrading the plugin, or the build breaks.**

## 1. `csharp/src/mock/MockMobileAdsPlugin.cs` — `using Array = Godot.Collections.Array;`

`Godot.NET.Sdk` enables implicit usings, so `global using System;` is in effect and the bare
`Array` parameter on `set_request_configuration` is ambiguous with `System.Array`. Upstream does
not build with implicit usings on, so it never sees this. Disabling implicit usings project-wide
was rejected: our own scripts rely on them in 30-odd places, so the blast radius is far larger
than one alias.

## 2. `NoBoxHead.csproj` — `csharp/sample/**` removed from compilation

Same root cause (`Timer` ambiguous with `System.Threading.Timer`), but this one is the addon's
demo scene. Godot compiles every `.cs` under the project root into a single assembly, so the
sample would otherwise both fail to build and ship inside the game. Nothing in `src/` references
it.
