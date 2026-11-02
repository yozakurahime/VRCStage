"""Local MediaPipe hand + upper-arm bridge. No recording or network model download at runtime."""
import argparse
import json
import math
import pathlib
import socket
import sys
import time


def point(p):
    return {"x": -float(p.x), "y": -float(p.y), "z": -float(p.z)}


def assemble(hand_result, pose_result, threshold=.5):
    """Join hand-local metric landmarks to pose-world wrists; image coordinates match sides."""
    output = {"version": 1, "source": "webcam", "hands": []}
    if not pose_result.pose_world_landmarks or not pose_result.pose_landmarks:
        return output
    world = pose_result.pose_world_landmarks[0]
    image = pose_result.pose_landmarks[0]
    candidates = []
    for index, normalized in enumerate(hand_result.hand_landmarks):
        if index >= len(hand_result.hand_world_landmarks):
            continue
        wrist = normalized[0]
        for left, pi, ei in ((True, 15, 13), (False, 16, 14)):
            if min(world[pi].visibility, world[ei].visibility) < threshold:
                continue
            distance = (wrist.x-image[pi].x)**2 + (wrist.y-image[pi].y)**2
            if distance < .04:
                candidates.append((distance, index, left, pi, ei))
    used_hand, used_side = set(), set()
    for _, index, left, pi, ei in sorted(candidates):
        if index in used_hand or left in used_side:
            continue
        used_hand.add(index)
        used_side.add(left)
        landmarks = hand_result.hand_world_landmarks[index]
        anchor, origin = point(world[pi]), point(landmarks[0])
        joints = [{axis: point(p)[axis]-origin[axis]+anchor[axis] for axis in "xyz"} for p in landmarks]
        if not all(math.isfinite(p[a]) and abs(p[a]) < 5 for p in joints for a in "xyz"):
            continue
        score = min(world[pi].visibility, world[ei].visibility)
        if index < len(hand_result.handedness) and hand_result.handedness[index]:
            score = min(score, hand_result.handedness[index][0].score)
        output["hands"].append({"left": left, "confidence": float(score), "elbowValid": True,
                                "elbow": point(world[ei]), "joints": joints})
    return output


def run(args):
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    root = pathlib.Path(__file__).resolve().parent
    models = root / "models"
    if getattr(sys, "frozen", False):
        models = pathlib.Path(sys._MEIPASS) / "models"
    hoptions = vision.HandLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(models/"hand_landmarker.task")),
        num_hands=2, running_mode=vision.RunningMode.VIDEO,
        min_hand_detection_confidence=.5, min_hand_presence_confidence=.5, min_tracking_confidence=.5)
    poptions = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(models/"pose_landmarker_lite.task")),
        num_poses=1, running_mode=vision.RunningMode.VIDEO,
        min_pose_detection_confidence=.5, min_pose_presence_confidence=.5, min_tracking_confidence=.5)
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    capture = None
    try:
        with vision.HandLandmarker.create_from_options(hoptions) as hand, vision.PoseLandmarker.create_from_options(poptions) as pose:
            if args.self_test:
                import numpy as np
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.zeros((480, 640, 3), dtype=np.uint8))
                result = assemble(hand.detect_for_video(image, 1), pose.detect_for_video(image, 1))
                assert result["hands"] == []
                print("PASS: both models initialized; real inference on blank image produced no invented hands.")
                return
            capture = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW)
            capture.set(cv2.CAP_PROP_FRAME_WIDTH, 960)
            capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 540)
            if not capture.isOpened():
                raise RuntimeError(f"Webcam {args.camera} indisponível. Verifique índice, privacidade e outro aplicativo usando a câmera.")
            start, last = time.monotonic(), -1
            print(f"Webcam {args.camera} -> 127.0.0.1:{args.port}. Local only; no recording.", flush=True)
            while True:
                ok, frame = capture.read()
                if not ok:
                    raise RuntimeError("A webcam parou de fornecer frames.")
                timestamp = max(last+1, int((time.monotonic()-start)*1000))
                last = timestamp
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                packet = assemble(hand.detect_for_video(image, timestamp), pose.detect_for_video(image, timestamp))
                udp.sendto(json.dumps(packet, separators=(",", ":"), allow_nan=False).encode(), ("127.0.0.1", args.port))
                if args.preview:
                    cv2.imshow("AvatarLab webcam local - Esc fecha", frame)
                    if cv2.waitKey(1) == 27:
                        return
    finally:
        if capture is not None:
            capture.release()
        udp.close()
        cv2.destroyAllWindows()


def ifacial_v3(args):
    from face_motion_v3 import V3Client
    directory = pathlib.Path.home()/"AppData/LocalLow/AvatarLab/AvatarLab Offline"
    directory.mkdir(parents=True, exist_ok=True)
    status_file = directory/"ifacial-v3-status.txt"
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    client = V3Client(args.phone or None, transport=args.transport, mode="fixed52" if args.fixed52 else "variable",
                      listen_only=not args.phone, ios_port=args.ios_port, pc_port=args.pc_port)
    def status(state):
        status_file.write_text(state + (": "+client.last_error if client.last_error else ""), encoding="utf-8")
    def frame_received(frame):
        text = "|".join(f"{key}&{value if frame.tracking else 0}" for key,value in zip(frame.schema.blend_names,frame.blend_values))
        if frame.tracking:
            text += "|=head#" + ",".join(str(v) for v in frame.head)
            text += "|rightEye#" + ",".join(str(v) for v in frame.right_eye)
            text += "|leftEye#" + ",".join(str(v) for v in frame.left_eye)
        udp.sendto((text+"|").encode("utf-8"), ("127.0.0.1",args.output_port))
    try:
        client.run(frame_received, on_state=status, duration=args.duration)
    except Exception as error:
        status_file.write_text("ERROR: "+str(error), encoding="utf-8")
        raise
    finally:
        udp.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--port", type=int, default=39541)
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--ifacial-v3", action="store_true")
    parser.add_argument("--phone", default="")
    parser.add_argument("--transport", choices=("udp","tcp"), default="udp")
    parser.add_argument("--fixed52", action="store_true")
    parser.add_argument("--output-port", type=int, default=49985)
    parser.add_argument("--ios-port", type=int)
    parser.add_argument("--pc-port", type=int)
    parser.add_argument("--duration", type=float)
    args = parser.parse_args()
    try:
        ifacial_v3(args) if args.ifacial_v3 else run(args)
    except Exception as error:
        directory = pathlib.Path.home()/"AppData/LocalLow/AvatarLab/AvatarLab Offline"
        directory.mkdir(parents=True, exist_ok=True)
        (directory/"webcam-error.txt").write_text(str(error), encoding="utf-8")
        print(error, file=sys.stderr)
        sys.exit(1)
