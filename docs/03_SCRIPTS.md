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
