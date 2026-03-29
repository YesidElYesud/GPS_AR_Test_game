# SATCS — Guía: Crear una Escena Nueva

Esta guía lleva paso a paso desde una escena vacía hasta una escena funcional con todos los sistemas activos.

> **Consejo:** En lugar de crear desde cero, duplica la escena `CASAS_OLAYA` y modifica su contenido. Es más rápido y evita errores de configuración.

---

## Paso 1 — Crear la escena

1. En la ventana **Project**, ir a `Assets/Scenes/`
2. Clic derecho → **Create > Scene**
3. Nombrarla según el barrio (ej: `LA_CANTERA`)
4. Doble clic para abrirla

---

## Paso 2 — GameObjects raíz (Managers)

Crea los siguientes GameObjects vacíos en la raíz de la escena (sin ser hijos de nada):

### 2.1 ARSceneSetup
- **GameObject > Create Empty** → nombre: `ARSceneSetup`
- Adjuntar script: `ARSceneSetup`

### 2.2 StageManager
- **GameObject > Create Empty** → nombre: `StageManager`
- Adjuntar script: `StageManager`
- En Inspector, el array `stageConfigs` tiene 6 entradas. Nombrarlas:
  - [0] Intro · [1] Etapa1 · [2] Etapa2 · [3] Etapa3 · [4] Etapa4 · [5] Etapa5
- Los campos `objectsToActivate` y `objectsToDeactivate` se llenan más adelante

### 2.3 GPSManager
- **GameObject > Create Empty** → nombre: `GPSManager`
- Adjuntar script: `GPSManager`

### 2.4 GyroscopeManager
- **GameObject > Create Empty** → nombre: `GyroscopeManager`
- Adjuntar script: `GyroscopeManager`

### 2.5 CameraFeedManager
- **GameObject > Create Empty** → nombre: `CameraFeedManager`
- Adjuntar script: `CameraFeedManager`

### 2.6 AudioStageManager
- **GameObject > Create Empty** → nombre: `AudioStageManager`
- Adjuntar script: `AudioStageManager`
- Los AudioSources se crean automáticamente. Solo asignar clips en `stageAudios[0..5]`

### 2.7 CinematicManager
- **GameObject > Create Empty** → nombre: `CinematicManager`
- Adjuntar script: `CinematicManager`
- Adjuntar componente: **Video Player**
- En Inspector de `CinematicManager`: campo `videoPlayer` → arrastra el mismo GameObject

---

## Paso 3 — Main Camera

1. Renombra la cámara principal a `Main Camera`
2. Verifica que tenga el **tag** `MainCamera`
3. Adjuntar script: `ARCameraController`
4. Adjuntar componente: **Character Controller**
5. Configurar el **Character Controller**:

   | Campo | Valor |
   |---|---|
   | Slope Limit | 45 |
   | Step Offset | 0.3 |
   | Skin Width | 0.08 |
   | Radius | 0.3 |
   | Height | 1.7 |
   | Center Y | -0.85 |

6. Posicionar la cámara en Y = 1.7 sobre el suelo del terreno
7. En `ARCameraController` Inspector:
   - `forceJoystick = true` (para probar en editor sin GPS)

---

## Paso 4 — Canvas Principal (AR_Canvas)

1. **GameObject > UI > Canvas** → nombre: `AR_Canvas`
2. En el componente **Canvas**:
   - Render Mode: `Screen Space - Overlay`
3. Adjuntar **Canvas Scaler**:
   - UI Scale Mode: `Scale With Screen Size`
   - Reference Resolution: `390 x 844`
   - Match: `0.5`
4. Verificar que haya un **EventSystem** en la escena (se crea automáticamente con el Canvas)

---

## Paso 5 — Paneles UI dentro de AR_Canvas

Crea los siguientes hijos dentro de `AR_Canvas`. Todos empiezan **inactivos** excepto WelcomePanel.

### 5.1 JoystickPanel
- **UI > Image** → nombre: `JoystickPanel`
- Adjuntar script: `JoystickController`
- Crear hijo **Image** → nombre: `Knob`
- En `JoystickController`: `knob` → arrastra `Knob`
- Posicionar en esquina inferior izquierda
- **Desactivar** (Inspector → desmarcar el checkbox del nombre)

### 5.2 HotspotPanel
- **UI > Panel** → nombre: `HotspotPanel`
- Adjuntar script: `HotspotUIPanel`
- Crear hijos: `TitleText` (TMP), `DescriptionText` (TMP), `Icon` (Image), `CloseButton` (Button)
- Asignar referencias en el Inspector de `HotspotUIPanel`
- **Desactivar**

### 5.3 WelcomePanel
- **UI > Panel** → nombre: `WelcomePanel`
- Adjuntar script: `WelcomePanel`
- Crear hijos: slides como GameObjects, botones Anterior/Siguiente/Comenzar/Saltar
- Asignar en Inspector: `slides[]`, `prevButton`, `nextButton`, `startButton`, `skipButton`
- **Dejar activo** (es el onboarding inicial)

### 5.4 NpcDialoguePanel
- **UI > Panel** → nombre: `NpcDialoguePanel`
- Adjuntar script: `NpcDialoguePanel`
- Crear hijos:
  - `NpcPhoto` (Image)
  - `NpcName` (TextMeshProUGUI)
  - `DialogueText` (TextMeshProUGUI)
  - `MultipleChoicePanelGO` → adjuntar script `MultipleChoicePanel`
    - Dentro: `OptionsContainer` (Vertical Layout), `FeedbackSection` con `FeedbackText` y `RetryButton`
- Asignar en Inspector de `NpcDialoguePanel`: `npcPhoto`, `npcNameText`, `dialogueText`, `choicePanel`
- **Desactivar**

### 5.5 SiataCallPanel
- **UI > Panel** → nombre: `SiataCallPanel`
- Adjuntar script: `SiataCallPanel`
- Crear hijos:
  - `CallerPhoto` (Image), `CallerName` (TMP), `CallStatus` (TMP)
  - `DialogueText` (TMP)
  - `MultipleChoicePanelGO` → script `MultipleChoicePanel`
  - `HangUpButton` (Button)
- Asignar en Inspector: todos los campos
- **Desactivar**

### 5.6 SensorCalibPanel
- **UI > Panel** → nombre: `SensorCalibPanel`
- Adjuntar script: `SensorCalibrationPanel`
- Crear hijos: `PitchSlider`, `RollSlider`, `YawSlider`, `ResetButton`, `RecalibrateButton`, `CloseButton`
- **Desactivar**

### 5.7 CinematicPanel
- **UI > Panel** → nombre: `CinematicPanel`
- Crear hijos:
  - `VideoRawImage` (RawImage) — stretch fullscreen
  - `LoadingText` (TMP) — "Cargando video…"
  - `SkipButton` (Button) — esquina inferior derecha
- Asignar en Inspector de `CinematicManager` (que está en la raíz): `cinematicPanel`, `videoDisplay`, `loadingText`, `skipButton`
- **Desactivar**

### 5.8 CriticalModePanel
- **UI > Panel** → nombre: `CriticalModePanel`
- Adjuntar script: `CriticalModePanel`
- Crear hijos:
  - `Overlay` (Image negro semitransparente, fullscreen)
  - `ContentBox` (Image panel centrado) con:
    - `AlertIcon` (Image)
    - `TitleText` (TMP)
    - `DescriptionText` (TMP)
    - `ContinueButton` (Button "Entendido")
- Asignar en Inspector: `alertIcon`, `titleText`, `descriptionText`, `continueButton`
- **Desactivar**

### 5.9 HUD
- **UI > Panel** (sin imagen de fondo) → nombre: `HUD`
- Adjuntar script: `UIManager`
- Crear textos e botones según necesidad
- Asignar todos los campos en Inspector de `UIManager`

---

## Paso 6 — Conexiones de Inspector globales

Una vez creados todos los objetos, conectar:

| Componente | Campo | Conectar con |
|---|---|---|
| `StageManager` | `cameraController` | Main Camera |
| `ARCameraController` | `joystickController` | JoystickPanel |
| `UIManager` | `cameraController` | Main Camera |
| `UIManager` | `joystickPanel` | JoystickPanel |
| `NpcDialoguePanel` | `choicePanel` | MultipleChoicePanelGO (hijo de NpcDialoguePanel) |
| `SiataCallPanel` | `choicePanel` | MultipleChoicePanelGO (hijo de SiataCallPanel) |
| `SiataCallPanel` | `hangUpButton` | HangUpButton |

---

## Paso 7 — Terreno

El **Character Controller** necesita colisiones para que el jugador no caiga.

- Si usas el componente **Terrain** de Unity → ya tiene collider integrado. No hacer nada.
- Si usas meshes importados (FBX) → seleccionarlos en Project → en Import Settings activar **Generate Mesh Colliders**, o agregar **Mesh Collider** manualmente en la escena.

---

## Paso 8 — Agregar hotspots

Ver la guía completa en **05_FLUJO_TRABAJO.md — Sección: Agregar un hotspot**.

---

## Paso 9 — Probar en editor

1. Activar `forceJoystick = true` en `ARCameraController` (GPS no funciona en editor)
2. Presionar **Play**
3. Usar **WASD** para moverse, **botón derecho del mouse** para rotar la cámara
4. Verificar que el WelcomePanel aparece y el botón "Comenzar" funciona
5. Acercarse a los hotspots para verificar que se activan

---

## Paso 10 — Build WebGL

1. **File > Build Settings**
2. Platform: **WebGL** → Switch Platform
3. Verificar que la escena esté en la lista
4. **Player Settings**:
   - Company/Product Name: SATCS
   - WebGL Template: Default o personalizado
5. **Build And Run**
6. Para GPS y giroscopio: el servidor debe servir bajo **HTTPS**
