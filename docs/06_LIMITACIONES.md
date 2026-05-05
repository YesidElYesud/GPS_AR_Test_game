# SATCS — Limitaciones y Consideraciones

---

## Plataforma

### WebGL es obligatorio
El proyecto está diseñado exclusivamente para WebGL. No funciona como aplicación nativa (APK, iOS, escritorio). Todo el código respeta esta restricción.

### HTTPS requerido para GPS y giroscopio
Los navegadores modernos (Chrome, Safari, Firefox) solo permiten acceso a GPS y giroscopio desde páginas servidas bajo **HTTPS**. Bajo HTTP (incluso localhost) estos sensores no están disponibles.

- Para desarrollo local: usar un servidor con certificado autofirmado o `localhost` (Chrome a veces permite localhost sin HTTPS).
- Para producción: el servidor de hosting debe tener HTTPS activo.

### iOS requiere permiso explícito de usuario
En iPhone/iPad, el acceso al giroscopio requiere que el usuario presione un botón que llame a `requestPermission()`. El botón `permissionGrantButton` en el HUD gestiona este flujo. Sin este paso, el giroscopio siempre devuelve datos vacíos en iOS.

---

## GPS

### No funciona en el editor de Unity
El plugin `ARSensors.jslib` solo funciona compilado en WebGL. En el editor, `GPSManager.IsAvailable` siempre devuelve `false`. Para probar en editor, activar `forceJoystick = true` en `ARCameraController` y usar WASD.

### Primera lectura como origen
La primera coordenada GPS que llega se guarda como el punto de origen del jugador. Si el GPS tarda en obtener señal o llega con error, el origen puede quedar mal posicionado. Usar el botón **Recalibrar** para reiniciarlo.

### Precisión del GPS en interiores
El GPS de teléfonos móviles tiene precisión de ±3–15 metros en exteriores. En interiores la señal se degrada notablemente. El juego está diseñado para usarse al aire libre.

### Escala GPS ↔ Unity
El campo `gpsToUnityScale` en `ARCameraController` convierte metros reales a unidades Unity. Si el jugador se mueve demasiado rápido o lento respecto al mundo 3D, ajustar este valor.

---

## Video (CinematicManager)

### WebGL: solo URL, no VideoClip
En builds WebGL, el `VideoPlayer` no puede usar `VideoClip` importados. Solo puede reproducir videos por URL (streaming). Los videos deben estar en `Assets/StreamingAssets/Videos/` y el campo `cinematicUrl` debe apuntar a ellos.

### Editor Linux: mp4 puede fallar
El editor de Unity en Linux tiene soporte limitado para decodificación de mp4 en tiempo de edición. Si el video no se importa como `VideoClip`, convertirlo a `.webm` con:
```bash
ffmpeg -i video.mp4 -c:v libvpx-vp9 -crf 30 -b:v 0 video.webm
```
El build WebGL final sí reproduce mp4 correctamente (el navegador maneja el codec).

### Sin soporte de DRM
Los videos se sirven directamente desde StreamingAssets sin cifrado. No usar videos con restricciones de derechos de autor.

---

## CharacterController y Terreno

### El terreno debe tener Collider
Sin un Collider en el terreno, el jugador cae indefinidamente por efecto de la gravedad. Verificar:
- **Terrain** de Unity: ya incluye collider. No hace falta nada.
- **Meshes importados (FBX)**: agregar `Mesh Collider` o activar `Generate Mesh Colliders` en Import Settings.

### Configuración del CharacterController en Main Camera
Valores incorrectos pueden hacer que el jugador quede enterrado en el suelo o que flote:

| Campo | Valor correcto | Problema si está mal |
|---|---|---|
| Height | 1.7 | Muy bajo: la cámara queda al ras del suelo. Muy alto: el jugador "se eleva" |
| Center Y | -0.85 | Si no es `-height/2`, la cámara no queda a la altura de los ojos |
| Step Offset | 0.3 | Muy alto: sube paredes. Muy bajo: se traba en escalones pequeños |

### Sin soporte de salto
El sistema no implementa salto. La gravedad solo mantiene al jugador sobre el suelo. Esta es una decisión de diseño intencional para simplificar la navegación.

---

## Audio

### AudioStageManager necesita clips asignados
Si un `stageAudios[n].clip` queda en null, esa etapa tendrá silencio. Esto es válido y no genera errores.

### Crossfade puede sonar raro con clips muy cortos
Si un clip de audio dura menos que el `fadeTime`, el crossfade puede quedar incompleto. Usar clips de al menos 5 segundos en loop.

---

## Sistemas implementados (antes pendientes del GDD)

Los siguientes sistemas ya están disponibles y listos para configurar en el Inspector:

| Sistema | Script | Estado |
|---|---|---|
| Vista dron Etapa5 | `AerialViewController` | ✅ Implementado — asignar `stageConfigs` y `pivotTarget` |
| HUD nivel de riesgo | `RiskLevelIndicator` | ✅ Implementado — incluye texto de recomendación por nivel |
| Efectos visuales por etapa | `VisualEffectsStageController` | ✅ Implementado — skybox, niebla, partículas, post-processing |
| Viento en vegetación | `GrassWindController` + `TreeWindController` | ✅ Implementado — presets automáticos por etapa |
| Río dinámico | `WaterFlowController` + `WaterLevelController` | ✅ Implementado — color y nivel de agua por etapa |
| Escombros en el río | `RiverDebrisController` | ✅ Implementado — rotación y tambaleo Perlin |
| Alarma en postes | `AlarmPoleController` | ✅ Implementado — audio 3D espacial con sirena |
| Charcos pulsantes | `PulsingPlane` | ✅ Implementado — activo solo si el jugador está cerca y N2–N4 |
| Lluvia con impactos | `RainParticleController` + `RainGroundSplash` | ✅ Implementado |
| Vista panorámica | `SceneOverviewController` + `CinematicSequencer` | ✅ Implementado |
| NPC caminante | `NPCWaypointWalker` | ✅ Implementado |
| Botón de hotspot HUD | `HotspotPromptButton` | ✅ Implementado |
| Slides educativos | `InfoSlidePanel` | ✅ Implementado |

## Sistemas pendientes del GDD

| Sistema | Descripción |
|---|---|
| **HotspotUIPanel enriquecido** | El InfoPanel actual es básico (título + texto + ícono). El GDD define un panel con imagen de cabecera, scroll y nivel de riesgo integrado |
| **SIRMED Integration** | Autenticación vía JWT desde iframe, reporte de progreso a la API. Documentado en `08_SIRMED_INTEGRACION.md` pero el GameObject `SIRMEDIntegration` aún no existe |
| **Múltiples barrios** | Solo `CASAS_OLAYA` está construido. `La Cantera Sur` y `San Antonio de Prado` están en el GDD pero sin escena |

---

## Limitaciones de diseño

### Un solo diálogo correcto por hotspot
Cada `NpcDialogueData` tiene exactamente una opción `isCorrect = true`. El sistema no soporta diálogos con múltiples respuestas correctas.

### El jugador no puede retroceder de etapa
`StageManager.NextStage()` solo avanza. No hay `PreviousStage()`. Si se necesita ir atrás, usar `GoToStage(Stage)` directamente desde código.

### Hotspots en rango al inicio
Si un hotspot tiene `requiredStage = -1` (siempre visible) y el jugador aparece dentro de su radio, `DispatchAction()` se llama en el primer frame. Solución: posicionar el punto de inicio del jugador fuera de todos los radios de hotspot.

### GPS y joystick no se combinan
El sistema es GPS **o** joystick. Si GPS está disponible y `forceJoystick = false`, el joystick táctil no tiene efecto sobre el movimiento. Sin embargo, WASD sí funciona siempre (se combina con joystick, no con GPS).

---

## Preguntas frecuentes

**¿Por qué el giroscopio no responde en iOS?**
Presionar el botón de permiso en el HUD (`permissionGrantButton`). iOS requiere que el usuario lo acepte explícitamente.

**¿Por qué el jugador cae al vacío?**
El terreno no tiene Collider, o el jugador inicia por debajo del terreno. Agregar Mesh Collider al terreno y posicionar la Main Camera en Y = 1.7 sobre el suelo.

**¿Por qué el video no reproduce en WebGL?**
Verificar que el archivo esté en `Assets/StreamingAssets/Videos/` y que `cinematicUrl` en HotspotData tenga el path correcto (ej: `StreamingAssets/Videos/clip.mp4`). El servidor de WebGL debe servir el archivo con el MIME type correcto (`video/mp4`).

**¿Por qué los hotspots no aparecen?**
Verificar el campo `requiredStage` en HotspotData. Si es `1` y el juego está en `Etapa2`, el hotspot estará inactivo. Usar `-1` para hotspots siempre visibles.

**¿Por qué el GPS posiciona mal al jugador?**
La primera coordenada que llega se usa como origen. Si el jugador estaba moviéndose o el GPS aún no había fijado señal, el origen puede ser incorrecto. Presionar **Recalibrar** para reiniciarlo.
