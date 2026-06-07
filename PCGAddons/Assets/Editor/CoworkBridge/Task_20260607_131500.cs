using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class Task_20260607_131500
{
	public static string Run()
	{
		var logEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
		var logEntryType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry");

		var startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static);
		var endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Public | BindingFlags.Static);
		var getEntryInternal = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.Static);
		var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Static);

		int count = (int)getCount.Invoke(null, null);
		Debug.Log("Console entries: " + count);

		startGettingEntries.Invoke(null, null);
		try
		{
			object entry = Activator.CreateInstance(logEntryType);
			var messageField = logEntryType.GetField("message");
			var modeField = logEntryType.GetField("mode");
			int shown = 0;
			int start = Mathf.Max(0, count - 200);
			for (int i = start; i < count; i++)
			{
				getEntryInternal.Invoke(null, new object[] { i, entry });
				string msg = (string)messageField.GetValue(entry);
				if (msg == null)
					continue;
				string firstLine = msg.Split('\n')[0];
				bool interesting = msg.Contains("FastGizmos") || msg.Contains("Exception") || msg.Contains("error") || msg.Contains("Error");
				if (interesting && shown < 40)
				{
					string head = msg.Length > 600 ? msg.Substring(0, 600) : msg;
					Debug.Log("[entry " + i + "] " + head);
					shown++;
				}
			}
			if (shown == 0)
			{
				Debug.Log("No interesting entries; last 10 first-lines follow:");
				for (int i = Mathf.Max(0, count - 10); i < count; i++)
				{
					getEntryInternal.Invoke(null, new object[] { i, entry });
					string msg = (string)messageField.GetValue(entry);
					Debug.Log("[entry " + i + "] " + (msg != null ? msg.Split('\n')[0] : "null"));
				}
			}
		}
		finally
		{
			endGettingEntries.Invoke(null, null);
		}

		return "Console scan complete";
	}
}
