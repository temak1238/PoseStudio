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

rtmw3d-x.onnx            (RTMW3D-x, 133 Ganzkörperpunkte inkl. je 21 Handpunkte, 3D; 288x384)
Quelle: https://huggingface.co/Soykaf/RTMW3D-x (ONNX-Umwandlung von OpenMMLab mmpose,
        projects/rtmpose3d), Datei rtmw3d-x_8xb64_cocktail14-384x288-b0a0eab7_20240626.onnx
Lizenz: Code Apache 2.0; die Gewichte sind u. a. auf COCO-WholeBody und Human3.6M trainiert,
        deren Nutzungsbedingungen nur nicht-kommerzielle Nutzung erlauben.

yolox-m-humanart.onnx    (YOLOX-m, Personen-Rahmen, 640x640, NMS im Modell)
Quelle: https://download.openmmlab.com/mmpose/v1/projects/rtmposev1/onnx_sdk/yolox_m_8xb8-300e_humanart-c2c7a14a.zip
        (end2end.onnx, umbenannt)
Lizenz: mmpose/mmdet Apache 2.0; trainiert auf Human-Art (Datensatz für Forschungszwecke).

Vor-/Nachbearbeitung nach rtmlib (https://github.com/Tau-J/rtmlib, Apache 2.0). Abweichung:
Tiefe wird wie in mmpose mit input_size[2]/2 = 144 normiert (rtmlib nutzt 192).
Beide Dateien sind wegen ihrer Größe nicht im Git-Repository (.gitignore); bei einem neuen
Checkout von den Quellen oben laden und hier ablegen. Fehlen sie, arbeitet die App nur mit MediaPipe.
