using System.IO;
using UnityEditor;
using UnityEngine;

public class SceneViewShotWindow : EditorWindow
{
	public SceneViewShotSettings Settings;

	private Vector2 _scroll;

	[MenuItem("Window/Scene View Shot")]
	public static void Open()
	{
		SceneViewShotWindow window = GetWindow<SceneViewShotWindow>();
		window.titleContent = new GUIContent("Scene View Shot");
		window.Show();
	}

	private void OnEnable()
	{
		Settings = SceneViewShotSettingsIO.Load();
	}

	private void OnDisable()
	{
		SceneViewShotSettingsIO.Save(Settings);
	}

	private void OnGUI()
	{
		DrawFolderBlock();
		EditorGUILayout.Space();
		DrawOffsetsBlock();
		EditorGUILayout.Space();
		DrawItems();
		EditorGUILayout.Space();
		DrawAddButton();
	}

	private void DrawOffsetsBlock()
	{
		EditorGUILayout.LabelField("Crop Offsets (px)", EditorStyles.boldLabel);

		EditorGUI.BeginChangeCheck();

		EditorGUILayout.BeginHorizontal();

		GUILayout.Label("Left", GUILayout.Width(45f));
		Settings.OffsetLeft = EditorGUILayout.IntField(Settings.OffsetLeft, GUILayout.Width(50f));

		GUILayout.Label("Right", GUILayout.Width(45f));
		Settings.OffsetRight = EditorGUILayout.IntField(Settings.OffsetRight, GUILayout.Width(50f));

		GUILayout.Label("Top", GUILayout.Width(45f));
		Settings.OffsetTop = EditorGUILayout.IntField(Settings.OffsetTop, GUILayout.Width(50f));

		GUILayout.Label("Bottom", GUILayout.Width(50f));
		Settings.OffsetBottom = EditorGUILayout.IntField(Settings.OffsetBottom, GUILayout.Width(50f));

		GUILayout.FlexibleSpace();

		EditorGUILayout.EndHorizontal();

		if (EditorGUI.EndChangeCheck())
		{
			SceneViewShotSettingsIO.Save(Settings);
		}
	}

	private void DrawFolderBlock()
	{
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.PrefixLabel("Output Folder");
		EditorGUILayout.SelectableLabel(Settings.OutputFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

		if (GUILayout.Button("Select…", GUILayout.Width(70f)))
		{
			string startPath = Directory.Exists(Settings.OutputFolder) ? Settings.OutputFolder : Application.dataPath;
			string selected = EditorUtility.OpenFolderPanel("Select output folder", startPath, "");
			if (!string.IsNullOrEmpty(selected))
			{
				Settings.OutputFolder = selected;
				SceneViewShotSettingsIO.Save(Settings);
			}
		}

		using (new EditorGUI.DisabledScope(!Directory.Exists(Settings.OutputFolder)))
		{
			if (GUILayout.Button("Open", GUILayout.Width(50f)))
			{
				EditorUtility.RevealInFinder(Settings.OutputFolder);
			}
		}

		EditorGUILayout.EndHorizontal();
	}

	private void DrawItems()
	{
		_scroll = EditorGUILayout.BeginScrollView(_scroll);

		int removeIndex = -1;
		for (int i = 0; i < Settings.Items.Count; i++)
		{
			SceneViewShotItem item = Settings.Items[i];

			EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

			EditorGUI.BeginChangeCheck();
			item.Name = EditorGUILayout.TextField(item.Name, GUILayout.Width(140f));
			item.Width = EditorGUILayout.IntField(item.Width, GUILayout.Width(60f));
			item.Height = EditorGUILayout.IntField(item.Height, GUILayout.Width(60f));
			if (EditorGUI.EndChangeCheck())
			{
				SceneViewShotSettingsIO.Save(Settings);
			}

			GUILayout.FlexibleSpace();

			SceneView active = SceneView.lastActiveSceneView;
			using (new EditorGUI.DisabledScope(active == null))
			{
				if (GUILayout.Button("Apply", GUILayout.Width(55f)))
				{
					SceneViewPoseUtility.Apply(active, item.Pose);
				}

				if (GUILayout.Button("Update", GUILayout.Width(60f)))
				{
					item.Pose = SceneViewPoseUtility.Capture(active);
					SceneViewShotSettingsIO.Save(Settings);
				}

				using (new EditorGUI.DisabledScope(!Directory.Exists(Settings.OutputFolder)))
				{
					if (GUILayout.Button("Save", GUILayout.Width(55f)))
					{
						SceneViewCaptureService.Capture(item, Settings);
					}
				}
			}

			if (GUILayout.Button("X", GUILayout.Width(22f)))
			{
				removeIndex = i;
			}

			EditorGUILayout.EndHorizontal();
		}

		if (removeIndex >= 0)
		{
			Settings.Items.RemoveAt(removeIndex);
			SceneViewShotSettingsIO.Save(Settings);
		}

		EditorGUILayout.EndScrollView();
	}

	private void DrawAddButton()
	{
		using (new EditorGUI.DisabledScope(SceneView.lastActiveSceneView == null))
		{
			if (GUILayout.Button("Add"))
			{
				SceneViewShotItem item = new SceneViewShotItem();
				item.Name = "Shot";
				item.Width = 1920;
				item.Height = 1080;
				item.Pose = SceneViewPoseUtility.Capture(SceneView.lastActiveSceneView);
				Settings.Items.Add(item);
				SceneViewShotSettingsIO.Save(Settings);
			}
		}
	}
}
