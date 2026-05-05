# SATCS — Documentación de Scripts

Referencia rápida de cada script: qué hace, qué campos configurar en el Inspector y qué métodos son útiles.

---

## GPSManager

**Archivo:** `Assets/Scripts/Gpsmanager.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Recibe las coordenadas GPS del navegador y las convierte en una posición 3D relativa al punto de inicio.

### Propiedades públicas

| Propiedad | Tipo | Descripción |
|---|---|---|
| `IsAvailable` | bool | GPS disponible en el dispositivo/navegador |
| `HasOrigin` | bool | Ya se recibió la primera coordenada (punto de origen) |
| `DisplacementMeters` | Vector2 | Desplazamiento en metros desde el origen. X = Este, Y = Norte |

### Notas
- En el editor Unity siempre devuelve `IsAvailable = false`. Activar `forceJoystick` en `ARCameraController` para probar sin GPS.
- La primera coordenada recibida se guarda como origen (el jugador "nace" ahí). Las siguientes calculan el desplazamiento.

---

## GyroscopeManager

**Archivo:** `Assets/Scripts/Gyroscopemanager.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Convierte los datos del giroscopio del navegador en una rotación Unity (Quaternion).

### Propiedades y métodos públicos

| Miembro | Descripción |
|---|---|
| `IsAvailable` | Giroscopio disponible |
| `DeviceRotation` | Quaternion suavizado listo para asignar a la cámara |
| `Recalibrate()` | Toma una nueva calibración automática en el próximo frame |
| `SetOffset(pitch, roll, yaw)` | Offset manual. Persiste en PlayerPrefs |

---

## ARCameraController

**Archivo:** `Assets/Scripts/Arcameracontroller.cs`
**Tipo:** Componente en Main Camera

Controla el movimiento y la rotación del jugador. Usa `CharacterController` para seguir el terreno.

### Inspector

| Campo | Tipo | Descripción |
|---|---|---|
| `joystickController` | JoystickController | Asignar JoystickPanel. Se auto-detecta si queda vacío |
| `arObject` | GameObject | Objeto 3D colocado frente al jugador al iniciar |
| `gpsToUnityScale` | float | Escala metros GPS → unidades Unity (default 1) |
| `joystickSpeed` | float | Velocidad con joystick (default 5) |
| `wasdSpeed` | float | Velocidad con teclado WASD (default 5) |
| `mouseLookSensitivity` | float | Sensibilidad del mouse en PC (default 3) |
| `forceJoystick` | bool | Ignora GPS y usa joystick/WASD. Útil en editor |
| `gravity` | float | Gravedad aplicada (default -20) |

### Métodos públicos

| Método | Descripción |
|---|---|
| `SetInputBlocked(bool)` | Bloquea/desbloquea movimiento y rotación |
| `SetForceJoystick(bool)` | Activa/desactiva modo joystick manualmente |
| `Recalibrate()` | Resetea GPS, giroscopio y reposiciona el arObject |

### CharacterController (componente adicional requerido)
Configurar en el mismo GameObject (Main Camera):

| Campo | Valor | Por qué |
|---|---|---|
| Height | 1.7 | Altura del jugador |
| Center Y | -0.85 | Pies a 1.7u bajo la cámara (ojos) |
| Slope Limit | 45 | Sube pendientes de hasta 45° |
| Step Offset | 0.3 | Sube escalones de 30cm |

---

## JoystickController

**Archivo:** `Assets/Scripts/Joystickcontroller.cs`
**Tipo:** Componente UI

Joystick táctil en pantalla. Activo automáticamente cuando no hay GPS.

### Inspector

| Campo | Descripción |
|---|---|
| `knob` | RectTransform del círculo interior del joystick |
| `knobRadius` | Tamaño del área de movimiento (px) |
| `deadZone` | Zona muerta para evitar drift (0–0.3) |

### Propiedad pública
- `InputDirection` — `Vector2` normalizado. X = horizontal, Y = adelante/atrás.

---

## StageManager

**Archivo:** `Assets/Scripts/StageManager.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Núcleo de la progresión. Controla en qué etapa está el jugador.

### Etapas (enum Stage)

```
Intro=0  Etapa1=1  Etapa2=2  Etapa3=3  Etapa4=4  Etapa5=5
```

### Inspector

| Campo | Descripción |
|---|---|
| `stageConfigs[6]` | Una entrada por etapa. Define qué GameObjects activar/desactivar |
| `cameraController` | Main Camera. Se auto-detecta si queda vacío |
| `startStage` | Etapa inicial al presionar Play (normalmente Intro) |

### Evento público
```csharp
StageManager.Instance.OnStageChanged += (anterior, nueva) => { ... };
```

### Métodos públicos

| Método | Descripción |
|---|---|
| `NextStage()` | Avanza a la siguiente etapa |
| `GoToStage(Stage)` | Salta directamente a una etapa |
| `SetPlayerInputBlocked(bool)` | Bloquea/desbloquea el movimiento del jugador |

---

## HotspotData

**Archivo:** `Assets/Scripts/Hotspotdata.cs`
**Tipo:** ScriptableObject

Crear desde: **Assets > Create > AR > Hotspot Data**

Contiene todo el contenido de un punto de interés. No tiene lógica.

### Campos

| Campo | Tipo | Descripción |
|---|---|---|
| `title` | string | Título visible en el panel |
| `description` | string | Texto informativo (para InfoPanel) |
| `icon` | Sprite | Ícono opcional |
| `actionType` | enum | Qué ocurre al activarlo (ver abajo) |
| `cinematicClip` | VideoClip | Video para editor/standalone |
| `cinematicUrl` | string | URL para WebGL (ej: `StreamingAssets/Videos/clip.mp4`) |
| `cinematicAdvancesStage` | bool | El video avanza etapa al terminar |
| `dialogueData` | NpcDialogueData | Datos del diálogo (NpcConversation / SiataCall) |
| `triggerRadius` | float | Radio de activación en unidades Unity |
| `allowClick` | bool | También activa con tap/clic |
| `requiredStage` | int | -1 = siempre visible. 0–5 = etapa específica |
| `isBlinking` | bool | Efecto de pulso visual en el objeto 3D |
| `blinkSpeed` | float | Velocidad del pulso (Hz) |

### actionType

| Valor | Qué ocurre |
|---|---|
| `InfoPanel` | Muestra panel de texto simple |
| `Cinematic` | Reproduce video fullscreen |
| `NpcConversation` | Abre diálogo con NPC y opciones |
| `SiataCall` | Simula llamada al SIATA |

---

## HotspotController

**Archivo:** `Assets/Scripts/Hotspotcontroller.cs`
**Tipo:** Componente · RequireComponent(Collider)

Se adjunta a cada objeto 3D que es un punto de interés en la escena.

### Inspector

| Campo | Descripción |
|---|---|
| `data` | HotspotData (ScriptableObject) con el contenido |
| `uiPanel` | HotspotUIPanel de la escena. Se auto-detecta si queda vacío |

### Método público
- `ClosePanel()` — cierra el panel abierto por este hotspot (llamado por los paneles al cerrarse).

---

## NpcDialogueData

**Archivo:** `Assets/Scripts/NpcDialogueData.cs`
**Tipo:** ScriptableObject

Crear desde: **Assets > Create > AR > NPC Dialogue Data**

### Campos

| Campo | Tipo | Descripción |
|---|---|---|
| `npcName` | string | Nombre del NPC |
| `npcPhoto` | Sprite | Foto del NPC |
| `npcText` | string | Pregunta o mensaje del NPC |
| `options[]` | DialogueOption[] | Opciones de respuesta |
| `advancesStageOnCorrect` | bool | Responder bien avanza la etapa |
| `correctAnswerDelay` | float | Pausa antes de avanzar (segundos) |

### DialogueOption

| Campo | Descripción |
|---|---|
| `optionText` | Texto del botón |
| `isCorrect` | Marcar solo en la respuesta correcta |
| `feedbackText` | Texto pedagógico mostrado tras seleccionar |

---

## NpcDialoguePanel

**Archivo:** `Assets/Scripts/NpcDialoguePanel.cs`
**Tipo:** Singleton UI · inicia inactivo

### Inspector

| Campo | Apunta a |
|---|---|
| `npcPhoto` | Image del NPC |
| `npcNameText` | TextMeshProUGUI del nombre |
| `dialogueText` | TextMeshProUGUI del texto |
| `choicePanel` | MultipleChoicePanel (hijo del panel) |

### API pública
```csharp
NpcDialoguePanel.Instance.Show(NpcDialogueData data, HotspotController source);
NpcDialoguePanel.Instance.Hide();
```

---

## SiataCallPanel

**Archivo:** `Assets/Scripts/SiataCallPanel.cs`
**Tipo:** Singleton UI · inicia inactivo

Igual que NpcDialoguePanel pero con UI de llamada telefónica: encabezado de llamada, animación "Llamando…→Conectado" y botón Colgar.

### Inspector

| Campo | Apunta a |
|---|---|
| `callerPhoto` | Image del caller |
| `callerNameText` | TextMeshProUGUI del nombre |
| `callStatusText` | TextMeshProUGUI "Llamando…" / "Conectado" |
| `dialogueText` | TextMeshProUGUI del mensaje |
| `choicePanel` | MultipleChoicePanel (hijo del panel) |
| `hangUpButton` | Button "Colgar" |
| `connectingDelay` | Segundos de animación "Llamando…" |

---

## CinematicManager

**Archivo:** `Assets/Scripts/CinematicManager.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Reproductor de video fullscreen. Se activa cuando un hotspot tiene `actionType = Cinematic`.

### Inspector

| Campo | Apunta a |
|---|---|
| `cinematicPanel` | GameObject del panel (inactivo por defecto) |
| `videoDisplay` | RawImage donde se renderiza el video |
| `loadingText` | TextMeshProUGUI "Cargando video…" |
| `skipButton` | Button "Saltar" |
| `videoPlayer` | VideoPlayer del mismo GameObject |
| `skipIfNoContent` | Si no hay clip/URL, cierra sin error |

### Videos en WebGL
- Colocar el archivo `.mp4` en `Assets/StreamingAssets/Videos/`
- En el HotspotData, asignar `cinematicUrl = "StreamingAssets/Videos/miVideo.mp4"`
- En editor Linux, usar formato `.webm` para preview

---

## CriticalModePanel

**Archivo:** `Assets/Scripts/CriticalModePanel.cs`
**Tipo:** Singleton UI · inicia inactivo

Modal automático que aparece al entrar a Etapa3 o Etapa4.

### Inspector

| Campo | Descripción |
|---|---|
| `stageContents[]` | Contenido por etapa (stage, title, description, icon) |
| `alertIcon` | Image del ícono de alerta |
| `titleText` | TextMeshProUGUI del título |
| `descriptionText` | TextMeshProUGUI de la descripción |
| `continueButton` | Button "Entendido" |
| `showDelay` | Segundos de espera antes de aparecer (default 1) |

Por defecto ya viene configurado para Etapa3 y Etapa4 con textos de alerta.

---

## AudioStageManager

**Archivo:** `Assets/Scripts/AudioStageManager.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Gestiona el audio ambiental con crossfade entre etapas.

### Inspector

| Campo | Descripción |
|---|---|
| `stageAudios[6]` | Una entrada por etapa: clip, volume, fadeTime |
| `masterVolume` | Volumen maestro global (0–1) |
| `sourceA/B/SFX` | AudioSources (se crean automáticamente si quedan vacíos) |

### API pública

```csharp
// Sonido puntual (trueno, alerta)
AudioStageManager.Instance.PlaySFX(AudioClip clip, float volume = 1f);

// Silenciar durante cinemática
AudioStageManager.Instance.MuteAmbient(float fadeDuration = 0.5f);
AudioStageManager.Instance.RestoreVolume(float fadeDuration = 0.5f);
```

---

## SensorCalibrationPanel

**Archivo:** `Assets/Scripts/SensorCalibrationPanel.cs`
**Tipo:** Singleton UI · inicia inactivo

3 sliders para ajustar el giroscopio manualmente. Los valores persisten entre sesiones (PlayerPrefs).

### API pública
```csharp
SensorCalibrationPanel.Instance.Show();
```

---

## UIManager

**Archivo:** `Assets/Scripts/Uimanager.cs`
**Tipo:** Componente en HUD

HUD principal con estado de sensores y controles.

### Inspector

| Campo | Apunta a |
|---|---|
| `joystickPanel` | JoystickPanel |
| `gpsStatusText` | TextMeshProUGUI GPS |
| `gyroStatusText` | TextMeshProUGUI giroscopio |
| `displacementText` | TextMeshProUGUI posición |
| `modeText` | TextMeshProUGUI modo actual |
| `toggleJoystickButton` | Button toggle joystick |
| `recalibrateButton` | Button recalibrar |
| `permissionGrantButton` | Button permiso iOS |
| `calibrationPanelButton` | Button abrir calibración |
| `cameraController` | Main Camera |

---

## RiskLevelIndicator

**Archivo:** `Assets/Scripts/RiskLevelIndicator.cs`
**Tipo:** Singleton UI · inicia inactivo

HUD persistente que muestra el nivel de riesgo actual (N1–N4) con color, ícono pulsante y texto de recomendación. Se actualiza desde `StageManager.OnStageChanged` (nivel por defecto de cada etapa) o desde `HotspotController` (sobreescritura puntual). Se oculta automáticamente en nivel `None`.

### Niveles (enum RiskLevel)

```
None=0  N1=1  N2=2  N3=3  N4=4
```

Colores: N1=amarillo, N2=naranja, N3=rojo, N4=rojo oscuro. N3/N4 activan pulso visual.

### Inspector

| Campo | Tipo | Descripción |
|---|---|---|
| `indicatorRoot` | GameObject | Raíz del widget (se activa/desactiva según nivel) |
| `iconImage` | Image | Ícono de alerta que pulsa en N3/N4 |
| `levelText` | TextMeshProUGUI | Texto "N1", "N2", etc. |
| `fondoNotificacion` | Image | Fondo del panel de recomendación (mismo color del nivel, semi-transparente) |
| `textNotificacion` | TextMeshProUGUI | Texto de recomendación por nivel |
| `notificacionAlpha` | float | Transparencia del fondo (0–1, default 0.80) |
| `notifTextN1/N2/N3/N4` | string | Textos de recomendación para cada nivel |

### API pública

```csharp
RiskLevelIndicator.Instance.SetLevel(RiskLevel level);
RiskLevelIndicator.Instance.CurrentLevel; // propiedad de lectura
```

---

## AerialViewController

**Archivo:** `Assets/Scripts/AerialViewController.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Activa la cámara dron (Etapa 5). Al entrar a la etapa configurada, eleva suavemente la cámara y la hace orbitar el centro de la escena. Al salir devuelve el control a `ARCameraController`. No modifica `StageManager`.

### Inspector

| Campo | Tipo | Descripción |
|---|---|---|
| `stageConfigs[]` | AerialConfig[] | Una entrada por etapa que activa la vista aérea |
| `cameraController` | ARCameraController | Se auto-detecta si queda vacío |
| `pivotTarget` | Transform | Centro de órbita. Null = origen de escena |
| `height` | float | Altura sobre el pivote (default 40u) |
| `orbitRadius` | float | Radio horizontal de la órbita (default 25u) |
| `orbitSpeed` | float | Velocidad de órbita (grados/seg). 0 = estática |
| `ascendDuration` | float | Segundos para llegar a la posición aérea |
| `descendDuration` | float | Segundos para volver al suelo |

### Setup en escena

1. Crear GO vacío `AerialViewController` en la raíz.
2. Adjuntar este script.
3. En `stageConfigs`, añadir entrada con `stage = Etapa5`.
4. (Opcional) Crear un Transform vacío `SceneCenter` y asignarlo a `pivotTarget`.

---

## VisualEffectsStageController

**Archivo:** `Assets/Scripts/VisualEffectsStageController.cs`
**Tipo:** Singleton · DontDestroyOnLoad

Controla skybox, iluminación ambiental, niebla, partículas y post-processing por etapa. Mismo patrón de Singleton + `OnStageChanged` que `AudioStageManager`.

### Inspector — StageVisualConfig (por etapa)

| Campo | Descripción |
|---|---|
| `skyboxMaterial` | Material de skybox. Null = mantener el anterior |
| `ambientMode` | FlatColor / Skybox / Trilight |
| `ambientColor` | Color ambiente (modo FlatColor) |
| `ambientIntensity` | Intensidad de la luz ambiental |
| `fogEnabled` | Activar niebla en esta etapa |
| `fogColor` | Color de la niebla |
| `fogDensity` | Densidad (modo exponencial) |
| `rainSystem` | ParticleSystem de lluvia. Null = sin lluvia |
| `postProcessVolume` | Volume de post-processing. Se pone weight=1, los demás weight=0 |
| `transitionDuration` | Segundos de transición entre etapas |

### Setup en escena

1. GO vacío `VisualEffectsStageController` en la raíz.
2. Adjuntar script + asignar `stageConfigs` con 6 entradas.
3. (Opcional) Arrastrar `Directional Light` al campo `sunLight`.
4. Crear volúmenes de post-processing globales y asignarlos por etapa.

---

## GrassWindController

**Archivo:** `Assets/Scripts/GrassWindController.cs`
**Tipo:** Componente único (GO `GrassWindController`)

Anima el pasto buscando automáticamente todos los hijos de `terrainRoot` cuyo nombre comience con el prefijo configurado. No requiere referencias individuales; al cambiar de etapa interpola suavemente hacia el preset de viento correspondiente.

### Inspector

| Campo | Descripción |
|---|---|
| `terrainRoot` | Transform raíz del terreno (padre del pasto) |
| `grassNamePrefix` | Prefijo de nombre para filtrar matas (default `"trasparent grass4"`) |
| `transitionDuration` | Segundos de blend entre presets (default 1.5s) |

### Presets por etapa (integrados en código)

| Etapa | Ángulo máx. | Velocidad | Notas |
|---|---|---|---|
| Intro / Etapa1 | 2° | 0.4 Hz | Brisa suave |
| Etapa2 | 6° | 0.6 Hz | Lluvia leve |
| Etapa3 | 14° | 1.1 Hz | Viento fuerte |
| Etapa4 | 22° | 1.6 Hz | Vendaval |
| Etapa5 | 8° | 0.8 Hz | Calmando |

> El pivote de cada mata debe estar en la **base del mesh** (Y=0 local).

---

## TreeWindController

**Archivo:** `Assets/Scripts/TreeWindController.cs`
**Tipo:** Componente único (GO `TreeWindManager`)

Igual que `GrassWindController` pero para árboles individuales asignados a mano en el Inspector (los árboles no comparten prefijo de nombre). Usa desfases de fase por posición mundial para que cada árbol se mueva diferente.

### Inspector

| Campo | Descripción |
|---|---|
| `trees[]` | Array de Transforms de los árboles a animar |
| `transitionDuration` | Segundos de blend entre presets (default 2.0s — árboles más inerciales) |

> El pivote de cada árbol debe estar en la **base del tronco**.

---

## RiverDebrisController

**Archivo:** `Assets/Scripts/RiverDebrisController.cs`
**Tipo:** Componente único (GO `RiverDebrisController`)

Spawna escombros y objetos flotantes que siguen una ruta de waypoints simulando la corriente. Activo solo en las etapas configuradas.

### Inspector

| Campo | Descripción |
|---|---|
| `waypoints[]` | Ruta del río (Transforms vacíos en orden) |
| `debrisPrefabs[]` | Pool de prefabs a spawnear aleatoriamente |
| `activateFromStage / activateUntilStage` | Rango de etapas activo |
| `spawnInterval` | Segundos entre spawns (default 2s) |
| `maxDebrisCount` | Máximo simultáneo (default 8) |
| `minSpeed / maxSpeed` | Rango de velocidad de flujo (default 1.5–3.5 u/s) |
| `lateralMargin` | Desviación lateral aleatoria por waypoint (default 0.4) |
| `alignToFlow` | Rota los objetos en la dirección de la corriente |
| `initialTiltRange` | Inclinación aleatoria al spawnear (±12° XZ, default 12) |
| `wobbleAmplitude` | Amplitud del tambaleo Perlin (grados, default 9°) |
| `wobbleSpeed` | Velocidad del tambaleo (default 0.7 Hz) |

---

## WaterFlowController

**Archivo:** `Assets/Scripts/WaterFlowController.cs`
**Tipo:** `RequireComponent(MeshRenderer)`

Anima el desplazamiento de textura del río y cambia su color por etapa con transición suave. Compatible con el shader `Custom/WaterFlow` (modo completo) o con cualquier shader estándar (modo fallback: solo scrollea `_MainTex`).

### Inspector

| Campo | Descripción |
|---|---|
| `flowDirection` | Dirección del flujo (Vector2, se normaliza automáticamente) |
| `flowSpeed` | Velocidad de scroll (0–1, default 0.08) |
| `fallbackMode` | TRUE = scroll manual de `_MainTex`. FALSE = usa propiedades `Custom/WaterFlow` |
| `stageColors[]` | Color del agua por etapa (Intro=azul claro → Etapa4=marrón oscuro) |
| `colorTransitionDuration` | Segundos de transición de color (default 3s) |

### Colores por defecto

| Etapa | Color | Descripción |
|---|---|---|
| Intro / Etapa1 | `#2E7AB9` α=0.75 | Azul claro tranquilo |
| Etapa2 | `#336699` α=0.80 | Azul más oscuro |
| Etapa3 | `#6B4D1E` α=0.85 | Marrón intermedio |
| Etapa4 | `#61380F` α=0.90 | Marrón oscuro — crecida |
| Etapa5 | `#59421A` α=0.88 | Marrón residual |

---

## WaterLevelController

**Archivo:** `Assets/Scripts/WaterLevelController.cs`
**Tipo:** Componente (adjuntar al GO del agua o al río)

Sube y baja la malla del agua animando su `localPosition.Y` al cambiar de etapa. Usa `SmoothStep` para una transición orgánica.

### Inspector

| Campo | Descripción |
|---|---|
| `waterMesh` | Transform de la malla de agua a desplazar |
| `stageConfigs[]` | Posición Y objetivo y duración de transición por etapa |

---

## AlarmPoleController

**Archivo:** `Assets/Scripts/AlarmPoleController.cs`
**Tipo:** `RequireComponent(AudioSource)` · adjuntar a cada poste

Reproduce la alarma de emergencia con audio 3D espacial desde el poste. El volumen se atenúa automáticamente con la distancia (el jugador "escucha" la alarma desde lejos). Incluye efecto de sirena que modula el pitch con onda seno.

### Inspector

| Campo | Descripción |
|---|---|
| `alarmClip` | Clip de alarma (`security-alarm.mp3`) |
| `minDistance` | Distancia de volumen máximo (default 5m) |
| `maxDistance` | Distancia de silencio total (default 40m) |
| `activateFromStage` | Etapa desde la que suena (inclusive) |
| `activateUntilStage` | Etapa hasta la que suena (inclusive) |
| `fadeInDuration` | Segundos de fade-in (default 1.5s) |
| `fadeOutDuration` | Segundos de fade-out (default 2.5s) |
| `sirenEffect` | Activar modulación de pitch tipo sirena |
| `sirenPitchMin/Max` | Rango del pitch (default 0.88–1.12) |
| `sirenSpeed` | Velocidad del ulular (Hz, default 0.22) |

> Setup: adjuntar a `Poste_low.001` y `Poste_low.002`. El `AudioSource` se configura automáticamente en `Awake` con `spatialBlend=1` y rolloff logarítmico.

---

## PulsingPlane

**Archivo:** `Assets/Scripts/PulsingPlane.cs`
**Tipo:** `RequireComponent(SphereCollider)`

Anima suavemente la escala de un plano (charco, mancha de agua) cuando el jugador entra en su radio y el nivel de riesgo es N2, N3 o N4. Fuera de rango o en niveles bajos el plano permanece estático.

### Inspector

| Campo | Descripción |
|---|---|
| `pulseAmplitude` | Variación de escala en % (default 0.03 = ±3%) |
| `pulseSpeed` | Frecuencia del pulso en Hz (default 0.7) |
| `fadeTime` | Segundos para entrar/salir del pulso (default 0.8s) |

> El radio del SphereCollider se ajusta manualmente en el Inspector. El collider se fuerza a `isTrigger` en `Awake`.

---

## RainParticleController

**Archivo:** `Assets/Scripts/RainParticleController.cs`
**Tipo:** `RequireComponent(ParticleSystem)`

Controla la intensidad de la lluvia por etapa. Se suscribe a `StageManager.OnStageChanged` y ajusta la tasa de emisión del `ParticleSystem`. Crea automáticamente el sistema de impactos en el suelo (`RainGroundSplash`) como hijo.

---

## RainGroundSplash

**Archivo:** `Assets/Scripts/RainGroundSplash.cs`
**Tipo:** Componente de impacto · creado automáticamente por `RainParticleController`

Tres capas de efecto de impacto de gota: Ripple (anillo plano), Spray (gotas rebotando) y Sparks (microchispas). Radio de splash independiente del área de lluvia, concentrado alrededor del jugador.

---

## WaterSplashManager

**Archivo:** `Assets/Scripts/WaterSplashManager.cs`
**Tipo:** Componente único en escena

Gestiona todos los efectos de salpicadura del agua. Escala la intensidad con cada etapa vía `OnStageChanged`.

### Inspector

| Campo | Descripción |
|---|---|
| `stageIntensities[]` | Intensidad de salpicadura por etapa (Intro → Etapa5) |

---

## NPCWaypointWalker

**Archivo:** `Assets/Scripts/NPCWaypointWalker.cs`
**Tipo:** Componente en el GO del NPC · `RequireComponent(CharacterController, Animator)`

Mueve al NPC por una ruta de waypoints tras recibir la respuesta correcta en el diálogo. Al llegar al último waypoint hace fade-out y se destruye. El Animator se configura automáticamente (se desactiva Root Motion para evitar conflicto con el CharacterController).

### Inspector

| Campo | Descripción |
|---|---|
| `waypoints[]` | Transforms de la ruta (en orden) |
| `walkSpeed` | Velocidad de movimiento (u/s) |
| `stoppingDistance` | Distancia para considerar waypoint alcanzado |
| `fadeDuration` | Segundos del fade-out final |

### API pública

```csharp
NPCWaypointWalker.StartWalking(); // Llamado por NpcDialoguePanel al obtener respuesta correcta
```

---

## HotspotPromptButton

**Archivo:** `Assets/Scripts/HotspotPromptButton.cs`
**Tipo:** Componente en HUD · requiere `Button`

Botón de acción que aparece en pantalla cuando el jugador entra en el radio de un hotspot. Pulsa con animación seno para llamar la atención. Al presionarlo llama `DispatchAction()` sobre el hotspot activo más cercano.

### Inspector

| Campo | Descripción |
|---|---|
| `buttonRoot` | GameObject raíz del botón (se activa/desactiva) |
| `button` | Componente Button al que se añade el listener |
| `pulseScale` | Escala máxima del pulso (default 1.08) |
| `pulseSpeed` | Velocidad del pulso en Hz |

---

## InfoSlidePanel

**Archivo:** `Assets/Scripts/InfoSlidePanel.cs`
**Tipo:** Singleton UI · inicia inactivo

Panel de slides secuenciales para contenido educativo. Muestra una serie de imágenes/textos con botones "Anterior" / "Siguiente" / "Cerrar". Usado por los hotspots de tipo `InfoPanel` con múltiples slides.

### Inspector

| Campo | Descripción |
|---|---|
| `titleText` | TextMeshProUGUI del título del slide |
| `bodyText` | TextMeshProUGUI del texto informativo |
| `slideImage` | Image del slide (opcional) |
| `prevButton / nextButton` | Botones de navegación |
| `slideCounter` | TextMeshProUGUI "1 / 4" |

---

## SceneOverviewController

**Archivo:** `Assets/Scripts/SceneOverviewController.cs`
**Tipo:** Singleton · reutiliza `CinematicSequencer`

Hub de vista panorámica del barrio. Al activarse pausa al jugador, ejecuta un bucle de cámara via `CinematicSequencer` y muestra botones de etapa (permiten cambiar el skybox/luz/lluvia sin avanzar la narrativa). Integrado con el sistema de hotspots: `HotspotController.DispatchAction()` llama a `Enter()`.

### Inspector

| Campo | Descripción |
|---|---|
| `sequencer` | CinematicSequencer existente en escena |
| `stageButtons[]` | OverviewStageConfig por etapa: botón, skybox, luz, lluvia |
| `fadePanel` | Image negra para fundidos de entrada/salida |
| `fadeDuration` | Segundos del fundido |

---

## CinematicSequencer

**Archivo:** `Assets/Scripts/CinematicSequencer.cs`
**Tipo:** Componente de cámara cinematográfica

Ejecuta una secuencia de `CameraShot` (posición, rotación, duración, easing) animando la cámara suavemente entre planos. Usado por `SceneOverviewController` para el bucle panorámico y como utilidad general de cutscenes sin video.

### CameraShot (datos por plano)

| Campo | Descripción |
|---|---|
| `targetTransform` | Transform destino del plano. Null = usa position/rotation directos |
| `duration` | Segundos en este plano |
| `easing` | Tipo de interpolación (Linear, SmoothStep, EaseIn, EaseOut) |
| `loop` | Al llegar al último plano, vuelve al primero |
