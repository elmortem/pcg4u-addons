using UnityEditor;

public static class SceneViewPoseUtility
{
	public static SceneViewPose Capture(SceneView sceneView)
	{
		SceneViewPose pose = new SceneViewPose();
		pose.Pivot = sceneView.pivot;
		pose.Rotation = sceneView.rotation;
		pose.Size = sceneView.size;
		pose.Orthographic = sceneView.orthographic;
		return pose;
	}

	public static void Apply(SceneView sceneView, SceneViewPose pose)
	{
		sceneView.LookAt(pose.Pivot, pose.Rotation, pose.Size, pose.Orthographic, true);
		sceneView.Repaint();
	}
}
