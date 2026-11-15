Modelle für "Pose aus Foto"
============================

person_detection_mediapipe_2023mar.onnx   (Personen-Detektor, 224x224)
pose_estimation_mediapipe_2023mar.onnx    (MediaPipe BlazePose, 33 Körperpunkte inkl. 3D)

Quelle: OpenCV Model Zoo, https://github.com/opencv/opencv_zoo
        models/person_detection_mediapipe, models/pose_estimation_mediapipe
        (aus MediaPipe/TFLite nach ONNX konvertiert)
Lizenz: Apache License 2.0 (laut den README-Dateien der jeweiligen Verzeichnisse)

Die Vor- und Nachbearbeitung in Detection\PoseDetector.cs folgt den Beispielskripten
mp_persondet.py und mp_pose.py aus demselben Repository (Apache 2.0).
