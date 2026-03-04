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
    if (window.AR_STATE.gyroHandler) {
      window.removeEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
    }
    window.AR_STATE.gyroHandler = function(e) {
      if (e.alpha === null && e.beta === null && e.gamma === null) return;
      var a = (e.alpha !== null) ? e.alpha.toFixed(4) : '0';
      var b = (e.beta  !== null) ? e.beta.toFixed(4)  : '0';
      var g = (e.gamma !== null) ? e.gamma.toFixed(4) : '0';
      SendMessage('GyroscopeManager', 'OnGyroUpdate', a + ',' + b + ',' + g);
    };
    window.addEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
  },

  Gyro_StopListening: function() {
    if (window.AR_STATE && window.AR_STATE.gyroHandler) {
      window.removeEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
      window.AR_STATE.gyroHandler = null;
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

    // Si ya tiene stream (iniciado desde HTML), notificar listo directamente
    if (video.srcObject && !video.paused) {
      SendMessage('CameraFeedManager', 'OnCameraReady', 'OK');
      return;
    }

    // Iniciar stream si no existe
    navigator.mediaDevices.getUserMedia({
      video: { facingMode:{ideal:'environment'}, width:{ideal:1280}, height:{ideal:720} },
      audio: false
    }).then(function(stream) {
      video.srcObject = stream;
      video.onloadedmetadata = function() {
        video.play();
        SendMessage('CameraFeedManager', 'OnCameraReady', 'OK');
      };
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
