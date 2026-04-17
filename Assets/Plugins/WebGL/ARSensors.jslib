/**
 * ARSensors.jslib — Unity 2022.3
 * Estado en window.AR_STATE para sobrevivir la compilacion de Emscripten.
 */

mergeInto(LibraryManager.library, {

  // ── GPS ───────────────────────────────────────────────────────────────────

  GPS_IsAvailable: function() {
    return !!navigator.geolocation ? 1 : 0;
  },

  GPS_StartWatching: function() {
    if (!navigator.geolocation) {
      SendMessage('GPSManager', 'OnGPSError', 'NotSupported');
      return;
    }
    if (!window.AR_STATE) window.AR_STATE = {};
    window.AR_STATE.gpsWatchId = navigator.geolocation.watchPosition(
      function(pos) {
        var d = pos.coords.latitude.toFixed(8) + ',' +
                pos.coords.longitude.toFixed(8) + ',' +
                pos.coords.accuracy.toFixed(1);
        SendMessage('GPSManager', 'OnGPSUpdate', d);
      },
      function(err) {
        SendMessage('GPSManager', 'OnGPSError', 'Code:' + err.code + ' ' + err.message);
      },
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 }
    );
  },

  GPS_StopWatching: function() {
    if (window.AR_STATE && window.AR_STATE.gpsWatchId >= 0) {
      navigator.geolocation.clearWatch(window.AR_STATE.gpsWatchId);
      window.AR_STATE.gpsWatchId = -1;
    }
  },

  // ── GIROSCOPIO ────────────────────────────────────────────────────────────

  Gyro_IsAvailable: function() {
    return ('DeviceOrientationEvent' in window) ? 1 : 0;
  },

  Gyro_StartListening: function() {
    if (!window.AR_STATE) window.AR_STATE = {};

    // Limpiar handlers previos
    if (window.AR_STATE.gyroHandler) {
      window.removeEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
      window.AR_STATE.gyroHandler = null;
    }
    if (window.AR_STATE.orientationSensor) {
      try { window.AR_STATE.orientationSensor.stop(); } catch(e) {}
      window.AR_STATE.orientationSensor = null;
    }

    // ── Fallback final: deviceorientation ─────────────────────────────────────
    // Funciona en iOS Safari y en Android como ultimo recurso.
    // El pulso de 16ms garantiza actualizaciones continuas durante toques (iOS).
    function setupDeviceOrientation() {
      if (window.AR_STATE.gyroHandler) return; // ya configurado
      window.AR_STATE.gyroHandler = function(e) {
        if (e.alpha === null && e.beta === null && e.gamma === null) return;
        var a = (e.alpha !== null) ? e.alpha.toFixed(4) : '0';
        var b = (e.beta  !== null) ? e.beta.toFixed(4)  : '0';
        var g = (e.gamma !== null) ? e.gamma.toFixed(4) : '0';
        window.AR_STATE.lastGyro = a + ',' + b + ',' + g;
        SendMessage('GyroscopeManager', 'OnGyroUpdate', window.AR_STATE.lastGyro);
      };
      window.addEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
      if (!window.AR_STATE.gyroPulse) {
        window.AR_STATE.gyroPulse = setInterval(function() {
          if (window.AR_STATE.lastGyro) {
            SendMessage('GyroscopeManager', 'OnGyroUpdate', window.AR_STATE.lastGyro);
          }
        }, 16);
      }
      console.log('[AR] Usando DeviceOrientation (fallback)');
    }

    // ── Intenta crear un Generic Sensor con fallback automático ───────────────
    // SensorClass : RelativeOrientationSensor o AbsoluteOrientationSensor
    // label       : nombre para logs
    // onFail      : función a llamar si el sensor arranca pero luego falla
    function tryOrientationSensor(SensorClass, label, onFail) {
      if (!SensorClass) return false;
      try {
        var sensor = new SensorClass({ frequency: 60, referenceFrame: 'screen' });
        window.AR_STATE.lastGyroQ = null;

        sensor.addEventListener('reading', function() {
          var q = sensor.quaternion; // [x, y, z, w]
          // Filtro de ruido: ignora cambios < ~0.1° (cos(0.05°) ≈ 0.999999).
          // IMPORTANTE: umbral = 0.99999, NO 0.9998 — el anterior bloqueaba
          // movimientos lentos (< 120°/s), haciendo la cámara incontrolable.
          var prev = window.AR_STATE.lastGyroQ;
          if (prev) {
            var dot = Math.abs(prev[0]*q[0] + prev[1]*q[1] + prev[2]*q[2] + prev[3]*q[3]);
            if (dot > 0.99999) return;
          }
          window.AR_STATE.lastGyroQ = [q[0], q[1], q[2], q[3]];
          // Prefijo "Q:" → Ruta A en Gyroscopemanager.cs (conversión ENU→Unity sin Euler)
          var data = 'Q:' + q[0].toFixed(6) + ',' + q[1].toFixed(6) + ','
                          + q[2].toFixed(6) + ',' + q[3].toFixed(6);
          window.AR_STATE.lastGyro = data;
          SendMessage('GyroscopeManager', 'OnGyroUpdate', data);
        });

        sensor.addEventListener('error', function(e) {
          var name = (e.error && e.error.name) ? e.error.name : String(e);
          console.warn('[AR] ' + label + ' error: ' + name);
          if (window.AR_STATE.orientationSensor === sensor) {
            try { sensor.stop(); } catch(se) {}
            window.AR_STATE.orientationSensor = null;
          }
          onFail();
        });

        sensor.start();
        window.AR_STATE.orientationSensor = sensor;
        console.log('[AR] Usando ' + label);
        return true;
      } catch(e) {
        console.warn('[AR] ' + label + ' no disponible: ' + (e.message || e));
        return false;
      }
    }

    // ── Cadena de sensores (orden de preferencia) ─────────────────────────────
    //
    // 1. RelativeOrientationSensor — giroscopio + acelerómetro, SIN magnetómetro.
    //    Ventaja clave: no hay deriva de brújula en interiores (edificios, aulas).
    //    La calibración automática en la primera lectura cubre la orientación inicial.
    //
    // 2. AbsoluteOrientationSensor — incluye magnetómetro (puede derivar en interiores,
    //    pero funciona en espacios abiertos y proporciona norte real).
    //
    // 3. DeviceOrientation — fallback universal (iOS Safari, Android sin Sensor API,
    //    o cuando los permisos de sensor son denegados).
    var RelSensor = typeof RelativeOrientationSensor !== 'undefined' ? RelativeOrientationSensor : null;
    var AbsSensor = typeof AbsoluteOrientationSensor !== 'undefined' ? AbsoluteOrientationSensor : null;

    if (!tryOrientationSensor(RelSensor, 'RelativeOrientationSensor', function() {
          // Si RelativeOrientationSensor falla en tiempo de ejecución, intentar Absolute
          if (!tryOrientationSensor(AbsSensor, 'AbsoluteOrientationSensor', setupDeviceOrientation)) {
            setupDeviceOrientation();
          }
        })) {
      // RelativeOrientationSensor no disponible en tiempo de carga → intentar Absolute
      if (!tryOrientationSensor(AbsSensor, 'AbsoluteOrientationSensor', setupDeviceOrientation)) {
        setupDeviceOrientation();
      }
    }
  },

  Gyro_StopListening: function() {
    if (window.AR_STATE) {
      if (window.AR_STATE.gyroHandler) {
        window.removeEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
        window.AR_STATE.gyroHandler = null;
      }
      if (window.AR_STATE.orientationSensor) {
        window.AR_STATE.orientationSensor.stop();
        window.AR_STATE.orientationSensor = null;
      }
      if (window.AR_STATE.gyroPulse) {
        clearInterval(window.AR_STATE.gyroPulse);
        window.AR_STATE.gyroPulse = null;
      }
    }
  },

  RequestDeviceOrientationPermission: function() {
    if (typeof DeviceOrientationEvent !== 'undefined' &&
        typeof DeviceOrientationEvent.requestPermission === 'function') {
      DeviceOrientationEvent.requestPermission()
        .then(function(state) {
          if (state === 'granted') {
            if (!window.AR_STATE) window.AR_STATE = {};
            window.AR_STATE.gyroHandler = function(e) {
              if (e.alpha === null && e.beta === null && e.gamma === null) return;
              var a = (e.alpha !== null) ? e.alpha.toFixed(4) : '0';
              var b = (e.beta  !== null) ? e.beta.toFixed(4)  : '0';
              var g = (e.gamma !== null) ? e.gamma.toFixed(4) : '0';
              SendMessage('GyroscopeManager', 'OnGyroUpdate', a + ',' + b + ',' + g);
            };
            window.addEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionGranted');
          } else {
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionDenied');
          }
        })
        .catch(function(e) {
          SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionError:' + e.toString());
        });
    }
  },

  // ── CAMARA — textura WebGL directo, sin transparencia del canvas ──────────
  //
  // Estrategia: creamos un canvas 2D auxiliar oculto.
  // En cada llamada a CamFeed_UpdateTexture, dibujamos el <video> en ese canvas
  // y usamos gl.texImage2D para subir el frame directo a una textura WebGL.
  // Unity usa esa textura en una RawImage de fondo.

  CamFeed_Start: function(videoElementIdPtr) {
    var videoId = UTF8ToString(videoElementIdPtr);
    if (!window.AR_STATE) window.AR_STATE = {};

    var video = document.getElementById(videoId);
    if (!video) {
      SendMessage('CameraFeedManager', 'OnCameraError', 'VideoElementNotFound');
      return;
    }

    function notifyReady() {
      SendMessage('CameraFeedManager', 'OnCameraReady', 'OK');
    }

    // Si ya tiene stream activo, notificar inmediatamente
    if (video.srcObject) {
      if (video.readyState >= 2) {
        notifyReady();
      } else {
        video.addEventListener('loadeddata', notifyReady, { once: true });
        // Fallback: notificar igual despues de 2 segundos
        setTimeout(notifyReady, 2000);
      }
      return;
    }

    // Si no hay stream, iniciarlo
    navigator.mediaDevices.getUserMedia({
      video: { facingMode:{ideal:'environment'}, width:{ideal:1280}, height:{ideal:720} },
      audio: false
    }).then(function(stream) {
      video.srcObject = stream;
      video.onloadedmetadata = function() {
        video.play().then(notifyReady).catch(notifyReady);
      };
      // Fallback por si onloadedmetadata no dispara
      setTimeout(notifyReady, 3000);
    }).catch(function(err) {
      SendMessage('CameraFeedManager', 'OnCameraError', err.name + ':' + err.message);
    });
  },

  // Llamado cada frame desde Unity para subir el frame actual del video
  // a la textura WebGL cuyo ID pasa Unity.
  CamFeed_UpdateTexture: function(textureId) {
    if (!window.AR_STATE) return;
    var video = document.getElementById('ar-video-bg');
    if (!video || video.paused || video.videoWidth === 0) return;

    // Canvas 2D auxiliar para leer pixels del video
    if (!window.AR_STATE.auxCanvas) {
      window.AR_STATE.auxCanvas = document.createElement('canvas');
      window.AR_STATE.auxCtx    = window.AR_STATE.auxCanvas.getContext('2d');
    }
    var w = video.videoWidth;
    var h = video.videoHeight;
    if (window.AR_STATE.auxCanvas.width  !== w) window.AR_STATE.auxCanvas.width  = w;
    if (window.AR_STATE.auxCanvas.height !== h) window.AR_STATE.auxCanvas.height = h;

    window.AR_STATE.auxCtx.drawImage(video, 0, 0, w, h);

    // Subir al contexto WebGL de Unity
    var canvas  = document.getElementById('unity-canvas');
    var gl      = canvas.getContext('webgl2') || canvas.getContext('webgl');
    if (!gl) return;

    var tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, window.AR_STATE.auxCanvas);
    gl.generateMipmap(gl.TEXTURE_2D);
  },


  // Captura un frame del <video> y lo envía a Unity como PNG base64
  GrabVideoFrame: function() {
    var video = document.getElementById('ar-video-bg');
    if (!video || video.paused || video.videoWidth === 0) return;

    if (!window.AR_STATE) window.AR_STATE = {};
    if (!window.AR_STATE.grabCanvas) {
      window.AR_STATE.grabCanvas = document.createElement('canvas');
      window.AR_STATE.grabCtx    = window.AR_STATE.grabCanvas.getContext('2d');
    }

    var w = video.videoWidth;
    var h = video.videoHeight;
    // Escalar a max 640px para no saturar la transferencia
    var scale = Math.min(1.0, 640 / Math.max(w, h));
    var tw = Math.floor(w * scale);
    var th = Math.floor(h * scale);

    if (window.AR_STATE.grabCanvas.width  !== tw) window.AR_STATE.grabCanvas.width  = tw;
    if (window.AR_STATE.grabCanvas.height !== th) window.AR_STATE.grabCanvas.height = th;

    window.AR_STATE.grabCtx.drawImage(video, 0, 0, tw, th);
    var dataUrl = window.AR_STATE.grabCanvas.toDataURL('image/jpeg', 0.7);
    var base64  = dataUrl.split(',')[1];
    SendMessage('CameraFeedManager', 'OnVideoFrame', base64);
  },

  CamFeed_Stop: function() {
    var video = document.getElementById('ar-video-bg');
    if (video && video.srcObject) {
      video.srcObject.getTracks().forEach(function(t) { t.stop(); });
      video.srcObject = null;
    }
  },

  CamFeed_IsReady: function() {
    var video = document.getElementById('ar-video-bg');
    return (video && video.readyState >= 2) ? 1 : 0;
  }

});
