/**
 * ARSensors.jslib
 * Bridge entre las APIs del navegador (GPS, DeviceOrientation, Cámara)
 * y Unity WebGL via SendMessage.
 *
 * INSTALACIÓN: Colocar en Assets/Plugins/WebGL/ARSensors.jslib
 */

var ARSensorsPlugin = {

  // ══════════════════════════════════════════════════════════════
  //  GPS / Geolocation
  // ══════════════════════════════════════════════════════════════

  _gpsWatchId: -1,

  GPS_IsAvailable: function() {
    return !!navigator.geolocation;
  },

  GPS_StartWatching: function() {
    if (!navigator.geolocation) return;

    var self = ARSensorsPlugin;
    var options = {
      enableHighAccuracy: true,
      timeout: 10000,
      maximumAge: 0
    };

    self._gpsWatchId = navigator.geolocation.watchPosition(
      function(pos) {
        var lat = pos.coords.latitude;
        var lon = pos.coords.longitude;
        var acc = pos.coords.accuracy;
        var data = lat.toFixed(8) + ',' + lon.toFixed(8) + ',' + acc.toFixed(1);
        // Enviar a Unity → GPSManager.OnGPSUpdate(data)
        SendMessage('GPSManager', 'OnGPSUpdate', data);
      },
      function(err) {
        SendMessage('GPSManager', 'OnGPSError', 'Code:' + err.code + ' ' + err.message);
      },
      options
    );
  },

  GPS_StopWatching: function() {
    if (ARSensorsPlugin._gpsWatchId >= 0) {
      navigator.geolocation.clearWatch(ARSensorsPlugin._gpsWatchId);
      ARSensorsPlugin._gpsWatchId = -1;
    }
  },

  // ══════════════════════════════════════════════════════════════
  //  Giroscopio / DeviceOrientation
  // ══════════════════════════════════════════════════════════════

  _gyroHandler: null,

  Gyro_IsAvailable: function() {
    return 'DeviceOrientationEvent' in window;
  },

  Gyro_StartListening: function() {
    if (!('DeviceOrientationEvent' in window)) return;

    ARSensorsPlugin._gyroHandler = function(event) {
      if (event.alpha === null || event.beta === null || event.gamma === null) return;

      var alpha = event.alpha !== null ? event.alpha.toFixed(4) : '0';
      var beta  = event.beta  !== null ? event.beta.toFixed(4)  : '0';
      var gamma = event.gamma !== null ? event.gamma.toFixed(4) : '0';

      var data = alpha + ',' + beta + ',' + gamma;
      SendMessage('GyroscopeManager', 'OnGyroUpdate', data);
    };

    window.addEventListener('deviceorientation', ARSensorsPlugin._gyroHandler, true);
  },

  Gyro_StopListening: function() {
    if (ARSensorsPlugin._gyroHandler) {
      window.removeEventListener('deviceorientation', ARSensorsPlugin._gyroHandler, true);
      ARSensorsPlugin._gyroHandler = null;
    }
  },

  // ══════════════════════════════════════════════════════════════
  //  Permiso de giroscopio en iOS 13+
  //  Debe llamarse desde un gesto del usuario (tap)
  // ══════════════════════════════════════════════════════════════

  RequestDeviceOrientationPermission: function() {
    if (typeof DeviceOrientationEvent !== 'undefined' &&
        typeof DeviceOrientationEvent.requestPermission === 'function') {

      DeviceOrientationEvent.requestPermission()
        .then(function(state) {
          if (state === 'granted') {
            // Re-iniciar el listener ahora que tenemos permiso
            ARSensorsPlugin.Gyro_StartListening();
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionGranted');
          } else {
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionDenied');
          }
        })
        .catch(function(err) {
          SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionError: ' + err);
        });
    }
    // En Android/Chrome el permiso no es necesario; no hace nada.
  },

  // ══════════════════════════════════════════════════════════════
  //  Cámara / getUserMedia
  //  El video se renderiza en un <video> HTML detrás del canvas de Unity.
  //  Unity no necesita gestionar la textura del video directamente.
  // ══════════════════════════════════════════════════════════════

  CamFeed_Start: function(videoElementIdPtr) {
    var videoElementId = UTF8ToString(videoElementIdPtr);
    var video = document.getElementById(videoElementId);

    if (!video) {
      console.warn('[ARSensors] No se encontró el elemento <video id="' + videoElementId + '">');
      SendMessage('CameraFeedManager', 'OnCameraError', 'VideoElementNotFound');
      return;
    }

    var constraints = {
      video: {
        facingMode: { ideal: 'environment' }, // Cámara trasera preferida
        width:  { ideal: 1280 },
        height: { ideal: 720 }
      },
      audio: false
    };

    navigator.mediaDevices.getUserMedia(constraints)
      .then(function(stream) {
        video.srcObject = stream;
        video.onloadedmetadata = function() {
          video.play();
          SendMessage('CameraFeedManager', 'OnCameraReady', 'OK');
          console.log('[ARSensors] Cámara iniciada.');
        };
      })
      .catch(function(err) {
        console.error('[ARSensors] Error de cámara:', err);
        SendMessage('CameraFeedManager', 'OnCameraError', err.message);
      });
  },

  CamFeed_Stop: function() {
    var video = document.getElementById('ar-video-bg');
    if (video && video.srcObject) {
      video.srcObject.getTracks().forEach(function(t) { t.stop(); });
      video.srcObject = null;
      console.log('[ARSensors] Cámara detenida.');
    }
  },

  CamFeed_IsReady: function() {
    var video = document.getElementById('ar-video-bg');
    return video && video.readyState >= 2;
  }
};

// Registrar el plugin en el sistema de Emscripten
mergeInto(LibraryManager.library, ARSensorsPlugin);
