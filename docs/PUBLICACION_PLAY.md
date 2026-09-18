# Publicar No Box Head en Google Play

Estado de la app en Play Console: **borrador**, paquete `com.dalogon.noboxhead`.

Todo lo que puede estar hecho desde el repositorio, lo está. Se verificó lanzando un export real
de AAB: la cadena completa —SDK de Android, gradle, plugin de AdMob, publish de .NET, iconos,
permisos— se ejecuta entera y falla en un único punto:

```
ERROR: No se pudo encontrar el almacén de claves de lanzamiento, no se puede exportar.
```

Es decir: **el keystore es el único bloqueo técnico que queda.**

---

## 1. Keystore de firma — lo generas tú, en un minuto

Es tu credencial y la contraseña no debe pasar por ningún chat, así que la creas tú. Con
**Play App Signing** (obligatorio para apps nuevas) esta es solo tu **clave de subida**: Google
guarda la clave de firma real. Si la pierdes, se restablece desde Play Console; no es el fin de
la app, pero es un trámite de días, así que haz copia.

En PowerShell (usa el mismo JDK que Godot):

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\Keys" | Out-Null
& "C:\Program Files\Android\openjdk\jdk-21.0.8\bin\keytool.exe" -genkeypair -v `
  -keystore "$env:USERPROFILE\Keys\noboxhead-upload.keystore" `
  -alias noboxhead -keyalg RSA -keysize 2048 -validity 10000
```

keytool pide la contraseña sin mostrarla en pantalla, y luego tu nombre, organización y país.
Este JDK crea el almacén en formato PKCS12, donde la contraseña del almacén y la de la clave son
la misma, así que solo te la pide una vez.

- Guárdalo **fuera del repositorio** (la ruta de arriba ya lo hace).
- Evita caracteres especiales en la contraseña; letras y números dan menos problemas en gradle.
- Haz una **copia de seguridad** del `.keystore` y de la contraseña en un sitio distinto del PC.

### Cómo se la das a Godot

**Proyecto → Exportar → preset Android → Keystore → Release**:

| Campo | Valor |
|---|---|
| Release | `C:\Users\Helen\Keys\noboxhead-upload.keystore` |
| Release User | `noboxhead` |
| Release Password | la tuya |

Es seguro rellenarlo ahí: Godot 4.7 guarda estos tres campos en `.godot/export_credentials.cfg`,
que está en el `.gitignore`, **no** en `export_presets.cfg`. Verificado: `export_presets.cfg` no
contiene ninguna clave `keystore/`.

Al crear tu primera versión en Play Console, acepta que **Google gestione la clave de firma**
(es la opción por defecto). Eso es lo que convierte a este keystore en clave de subida
recuperable.

---

## 2. Exportar

Hay dos presets configurados:

| Preset | Formato | Para qué |
|---|---|---|
| `Android APK (testing)` | APK | Instalar en tu móvil por USB y probar |
| `Android AAB (Play Store)` | AAB | Lo que sube a Play (Play no acepta APK desde 2021) |

```bash
# con las variables de entorno puestas
Godot_v4.7-stable_mono_win64_console.exe --headless --path . --export-release "Android AAB (Play Store)" "%USERPROFILE%\Downloads\no-box-head.aab"
```

**Cierra el editor de Godot antes de exportar desde consola.** Godot reescribe
`export_presets.cfg` al cerrarse y puede deshacer cambios hechos a mano.

---

## 3. Datos para el formulario de Seguridad de los datos

Verificado contra el código, no supuesto: no hay analítica, ni telemetría, ni informes de fallos,
ni ningún servidor propio. Lo único que sale del dispositivo lo genera AdMob.

| Pregunta de Play | Respuesta | Por qué |
|---|---|---|
| ¿Recoge o comparte datos de usuario? | **Sí** | Por AdMob, no por nosotros |
| Tipo de dato | **ID de dispositivo u otros ID** → Advertising ID | Lo recoge AdMob |
| ¿También ubicación? | **Ubicación aproximada** | AdMob la deduce de la IP |
| Finalidad | **Publicidad o marketing** | |
| ¿Se comparte con terceros? | **Sí** (Google) | |
| ¿Es obligatorio? | **Sí** | Los anuncios financian la app |
| ¿Cifrado en tránsito? | **Sí** | AdMob usa HTTPS |
| ¿Se puede solicitar la eliminación? | **No** | No hay cuenta ni dato que borrar |
| ¿Hay cuentas de usuario? | **No** | |
| ¿Permite que los usuarios se comuniquen entre sí? | **No** | El multijugador no tiene chat |

Las preferencias (`user://settings.cfg`) **no se declaran**: nunca salen del dispositivo.

---

## 4. Datos para el cuestionario de clasificación por edades (IARC)

Responde tú, pero esto es lo que el juego contiene realmente:

- **Violencia:** sí. Se dispara a zombis, demonios y jefes. Hay sangre: charcos y salpicaduras,
  con paleta de color configurable (rojo por defecto).
- **Armas:** pistola, escopeta, subfusil, lanzagranadas, lanzacohetes, lanzallamas, railgun,
  minas, motosierra, cuchillo, torretas y barriles.
- **Sin** contenido sexual, lenguaje soez, sustancias, apuestas ni compras integradas.
- **Sin** contenido generado por usuarios ni comunicación entre jugadores.
- **Con publicidad:** sí. Hay que marcarlo, y también en la ficha de la app.

Un arena shooter con zombis y sangre normalmente sale **Teen / PEGI 12–16**. No lo declares como
apto para menores de 13: obligaría a reconfigurar AdMob (COPPA/TFUA) y a reescribir la política
de privacidad.

---

## 5. Cuando ya tengas las claves de AdMob

Hoy el juego usa las **unidades de prueba públicas de Google**, que sirven anuncios reales y se
pueden pulsar sin riesgo. Para pasar a producción:

1. **App ID** → Ajustes del Proyecto → `admob/general/android/app_id`
   (los ajustes aparecen la primera vez que abras el proyecto en el editor con el plugin activo).
2. Los **tres ad unit IDs** → autoload `AdManager`: `RealBanner`, `RealInterstitial`,
   `RealRewarded`.
3. Poner **`IsReal = true`** en el mismo autoload.

> **Antes de probar las unidades reales, registra tu móvil como dispositivo de prueba en AdMob**
> (por su advertising ID). Con el dispositivo registrado, tus unidades reales devuelven anuncios
> de prueba y puedes pulsarlos sin riesgo. Pulsar anuncios reales propios —aunque sea una vez, y
> aunque sea sin querer— es la causa más común de suspensión de cuenta de AdMob.

Verás también **"ad serving limited"** en unidades reales mientras la app no esté publicada y
verificada. Es normal, no significa que la integración esté rota.

---

## 6. Lista final antes de subir

- [ ] Keystore generado, **respaldado fuera del PC** y fuera del repositorio
- [ ] Variables de entorno `GODOT_ANDROID_KEYSTORE_RELEASE_*` puestas
- [ ] Política de privacidad publicada en una URL pública (ver `docs/PRIVACY_POLICY.md`)
- [ ] App ID y los 3 ad unit IDs reales puestos, `IsReal = true`
- [ ] Móvil registrado como dispositivo de prueba en AdMob
- [ ] Formulario de Seguridad de los datos completado (sección 3)
- [ ] Cuestionario de clasificación por edades completado (sección 4)
- [ ] Ficha de Play: capturas, icono, descripción corta y larga
- [ ] AAB firmado, exportado y probado en un dispositivo real antes de subirlo
- [ ] Probado el flujo completo de anuncios en el móvil: revivir, intersticial y banner

---

## Notas técnicas

**Arquitecturas.** Solo `arm64-v8a`. Cubre todos los dispositivos modernos y reduce el tamaño;
deja fuera hardware de 32 bits, que ya es residual. Si quieres cubrirlo, activa `armeabi-v7a` en
el preset.

**Librerías nativas de AdMob.** Están en `addons/admob/android/bin/` y **se versionan a
propósito** (552 KB). El plugin las ignora por defecto en su propio `.gitignore`, pero un clon sin
ellas exportaría sin los anuncios sin avisar.

**Parches locales al plugin.** Ver `addons/admob/LOCAL_PATCHES.md`. Hay que reaplicarlos si
actualizas el plugin, o la compilación se rompe.

**Permiso `AD_ID`.** Verificado: el `.aar` de AdMob lo declara y se fusiona en el manifest final.
El SDK que usa el plugin es `com.google.android.libraries.ads.mobile.sdk:ads-mobile-sdk:1.2.1`,
resuelto por gradle al compilar.
