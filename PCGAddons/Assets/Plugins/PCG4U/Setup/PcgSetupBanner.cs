using UnityEditor;
using UnityEngine;

namespace PCG.Setup
{
	public static class PcgSetupBanner
	{
		public const float Height = 120f;

		private const float StripHeight = 56f;
		private const float IconSize = 120f;
		private const float TextLeft = 130f;

		private const string IconPath = "Packages/com.elmortem.pcg4u/PCG/Icons/PcgIcon.png";
		private const string Title = "PCG4U";
		private const string Slogan = "Procedural. Controllable. Yours.";

		private static readonly Color _gradientLeft = new Color(0.22f, 0.22f, 0.22f, 1f);
		private static readonly Color _gradientRight = new Color(0.95f, 0.7f, 0.2f, 1f);

		private static Texture2D _gradient;
		private static Texture2D _icon;
		private static GUIStyle _titleStyle;
		private static GUIStyle _sloganStyle;

		public static void Draw()
		{
			EnsureResources();
			var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(Height), GUILayout.ExpandWidth(true));
			var stripRect = new Rect(rect.x, rect.y + (Height - StripHeight) * 0.5f, rect.width, StripHeight);
			GUI.DrawTexture(stripRect, _gradient, ScaleMode.StretchToFill);
			if (_icon != null)
				GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + (Height - IconSize) * 0.5f, IconSize, IconSize), _icon, ScaleMode.ScaleToFit);
			GUI.Label(new Rect(rect.x + TextLeft, rect.y + 41f, 220f, 24f), Title, _titleStyle);
			GUI.Label(new Rect(rect.x + TextLeft + 1f, rect.y + 66f, rect.width - TextLeft - 9f, 16f), Slogan, _sloganStyle);
		}

		private static void EnsureResources()
		{
			if (_gradient == null)
			{
				_gradient = new Texture2D(256, 1, TextureFormat.RGBA32, false)
				{
					hideFlags = HideFlags.HideAndDontSave,
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear
				};
				for (var x = 0; x < 256; x++)
				{
					_gradient.SetPixel(x, 0, Color.Lerp(_gradientLeft, _gradientRight, x / 255f));
				}
				_gradient.Apply();
			}
			if (_icon == null)
				_icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
			if (_titleStyle == null)
			{
				_titleStyle = new GUIStyle(EditorStyles.boldLabel)
				{
					fontSize = 18
				};
				_titleStyle.normal.textColor = Color.white;
			}
			if (_sloganStyle == null)
			{
				_sloganStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					fontSize = 11
				};
				_sloganStyle.normal.textColor = Color.white;
			}
		}
	}
}
