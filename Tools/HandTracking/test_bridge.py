import unittest
from types import SimpleNamespace as N
from webcam_bridge import assemble


def landmark(x=0, y=0, z=0, visibility=1):
    return N(x=x, y=y, z=z, visibility=visibility)


class BridgeTests(unittest.TestCase):
    def fixture(self):
        world = [landmark() for _ in range(33)]
        image = [landmark() for _ in range(33)]
        image[15], image[16] = landmark(.8,.4), landmark(.2,.4)
        world[15], world[13] = landmark(.3,-.5,-.2), landmark(.2,-.2,0)
        fingers = [landmark(.1,.1,.1) for _ in range(21)]
        fingers[8] = landmark(.12,.16,.08)
        hands = N(hand_landmarks=[[landmark(.8,.4) for _ in range(21)]],
                  hand_world_landmarks=[fingers], handedness=[[N(score=.99,category_name="Right")]])
        return hands, N(pose_world_landmarks=[world],pose_landmarks=[image])

    def test_anatomical_side_uses_body_wrist_even_if_hand_label_disagrees(self):
        hands, pose = self.fixture()
        frame = assemble(hands, pose)
        self.assertEqual(len(frame["hands"]), 1)
        hand = frame["hands"][0]
        self.assertTrue(hand["left"])
        self.assertEqual(hand["joints"][0], {"x":-.3,"y":.5,"z":.2})
        self.assertAlmostEqual(hand["joints"][8]["x"],-.32)
        self.assertEqual(hand["elbow"], {"x":-.2,"y":.2,"z":0.0})

    def test_occlusion_and_absent_pose_remove_hand(self):
        hands, pose = self.fixture()
        pose.pose_world_landmarks[0][15].visibility = .1
        self.assertEqual(assemble(hands,pose)["hands"], [])
        pose.pose_world_landmarks = []
        self.assertEqual(assemble(hands,pose)["hands"], [])

    def test_duplicate_detections_do_not_drive_same_arm_twice(self):
        hands, pose = self.fixture()
        hands.hand_landmarks *= 2
        hands.hand_world_landmarks *= 2
        hands.handedness *= 2
        self.assertEqual(len(assemble(hands,pose)["hands"]), 1)


if __name__ == "__main__":
    unittest.main()
