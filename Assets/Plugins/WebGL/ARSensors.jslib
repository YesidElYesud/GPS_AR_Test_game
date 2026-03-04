/**
 * ARSensors.jslib — Unity 2022.3 / Emscripten compatible
 *
 * REGLA: Emscripten elimina las variables 'var' globales sueltas.
 * El estado debe guardarse en window.AR_STATE para sobrevivir la compilación.
 */

mergeInto(LibraryManager.library, {

  // ════════════════════════════════════════════════════════════════
  //  GPS
  // ════════════════════════════════════════════════════════════════

  GPS_IsAvailable: function() {
    return !!navigator.geolocation ? 1 : 0;
  },

  GPS_StartWatching: function() {
    if (!navigator.geolocation) {
      SendMessage('GPSManager', 'OnGPSError', 'GeolocationNotSupported');
      return;
    }
    if (!window.AR_STATE) window.AR_STATE = {};
    var options = { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 };
    window.AR_STATE.gpsWatchId = navigator.geolocation.watchPosition(
      function(pos) {
        var data = pos.coords.latitude.toFixed(8) + ',' +
                   pos.coords.longitude.toFixed(8) + ',' +
                   pos.coords.accuracy.toFixed(1);
        SendMessage('GPSManager', 'OnGPSUpdate', data);
      },
      function(err) {
        SendMessage('GPSManager', 'OnGPSError', 'Code:' + err.code + ' ' + err.message);
      },
      options
    );
  },

  GPS_StopWatching: function() {
    if (window.AR_STATE && window.AR_STATE.gpsWatchId >= 0) {
      navigator.geolocation.clearWatch(window.AR_STATE.gpsWatchId);
      window.AR_STATE.gpsWatchId = -1;
    }
  },

  // ════════════════════════════════════════════════════════════════
  //  Giroscopio / DeviceOrientation
  // ════════════════════════════════════════════════════════════════

  Gyro_IsAvailable: function() {
    return ('DeviceOrientationEvent' in window) ? 1 : 0;
  },

  Gyro_StartListening: function() {
    if (!window.AR_STATE) window.AR_STATE = {};

    // Limpiar listener anterior si existe
    if (window.AR_STATE.gyroHandler) {
      window.removeEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
      window.AR_STATE.gyroHandler = null;
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

  // ════════════════════════════════════════════════════════════════
  //  Permiso iOS 13+ (requiere gesto del usuario)
  // ════════════════════════════════════════════════════════════════

  RequestDeviceOrientationPermission: function() {
    if (typeof DeviceOrientationEvent !== 'undefined' &&
        typeof DeviceOrientationEvent.requestPermission === 'function') {

      DeviceOrientationEvent.requestPermission()
        .then(function(state) {
          if (state === 'granted') {
            if (!window.AR_STATE) window.AR_STATE = {};
            if (!window.AR_STATE.gyroHandler) {
              window.AR_STATE.gyroHandler = function(e) {
                if (e.alpha === null && e.beta === null && e.gamma === null) return;
                var a = (e.alpha !== null) ? e.alpha.toFixed(4) : '0';
                var b = (e.beta  !== null) ? e.beta.toFixed(4)  : '0';
                var g = (e.gamma !== null) ? e.gamma.toFixed(4) : '0';
                SendMessage('GyroscopeManager', 'OnGyroUpdate', a + ',' + b + ',' + g);
              };
              window.addEventListener('deviceorientation', window.AR_STATE.gyroHandler, true);
            }
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionGranted');
          } else {
            SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionDenied');
          }
        })
        .catch(function(err) {
          SendMessage('GyroscopeManager', 'OnGyroError', 'PermissionError:' + err.toString());
        });
    }
  },

  // ════════════════════════════════════════════════════════════════
  //  Camara — video HTML detras del canvas de Unity
  // ════════════════════════════════════════════════════════════════

  CamFeed_Start: function(videoElementIdPtr) {
    var videoElementId = UTF8ToString(videoElementIdPtr);
    var video = document.getElementById(videoElementId);

    if (!video) {
      SendMessage('CameraFeedManager', 'OnCameraError', 'VideoElementNotFound');
      return;
    }
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      SendMessage('CameraFeedManager', 'OnCameraError', 'getUserMediaNotSupported');
      return;
    }

    navigator.mediaDevices.getUserMedia({
      video: {
        facingMode: { ideal: 'environment' },
        width:  { ideal: 1280 },
        height: { ideal: 720 }
      },
      audio: false
    })
    .then(function(stream) {
      video.srcObject = stream;
      video.onloadedmetadata = function() {
        video.play();
        SendMessage('CameraFeedManager', 'OnCameraReady', 'OK');
      };
    })
    .catch(function(err) {
      SendMessage('CameraFeedManager', 'OnCameraError', err.name + ':' + err.message);
    });
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
