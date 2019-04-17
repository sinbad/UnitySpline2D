using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Spline2DComponent))]
public class Spline2DInspector : Editor {

    private Spline2DComponent spline;
	private const float handleSize = 0.04f;
	private const float pickSize = 0.06f;
	private int selectedIndex = -1;

	public override void OnInspectorGUI () {
		spline = target as Spline2DComponent;

		EditorGUILayout.Space();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Add Point")) {
			Undo.RecordObject(spline, "Add Point");
			AddNewPoint();
		}

		if (selectedIndex == -1) {
			GUI.enabled = false;
		}
		if (GUILayout.Button("Insert Point")) {
			Undo.RecordObject(spline, "Insert Point");
			InsertNewPoint();
		}
		if (GUILayout.Button("Remove Point")) {
			Undo.RecordObject(spline, "Remove Point");
			RemovePoint();
		}
		GUILayout.EndHorizontal();
		GUI.enabled = true;

		EditorGUILayout.Space();
		EditorGUI.indentLevel += 2;
		DrawSelectedPointInspector();
		EditorGUI.indentLevel -= 2;

		EditorGUILayout.Space();
		// DON'T use the default inspector, it will bypass setters and we need
		// those to be called to properly dirty the state
		EditorGUI.BeginChangeCheck();
		bool closed = EditorGUILayout.Toggle("Closed", spline.IsClosed);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Toggle Closed");
			spline.IsClosed = closed;
		}
		EditorGUI.BeginChangeCheck();
		bool xz = EditorGUILayout.Toggle("X/Z Mode", spline.displayXZ);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Toggle XZ Mode");
			spline.displayXZ = xz;
		}
		EditorGUI.BeginChangeCheck();
		float curve = EditorGUILayout.FloatField("Curvature", spline.Curvature);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Set Curvature");
			spline.Curvature = curve;
		}
		EditorGUI.BeginChangeCheck();
		int lenSamples = EditorGUILayout.IntSlider("Length Sampling", spline.LengthSamplesPerSegment, 1, 20);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Set Length Sampling");
			spline.LengthSamplesPerSegment = lenSamples;
		}


		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		EditorGUI.BeginChangeCheck();
		bool showDistance = EditorGUILayout.Toggle("Show Distance", spline.showDistance);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Toggle Show Distance");
			spline.showDistance = showDistance;
		}
		EditorGUI.BeginChangeCheck();
		float dist = EditorGUILayout.FloatField("Interval", spline.distanceMarker);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Set Distance Interval");
			spline.distanceMarker = dist;
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		EditorGUI.BeginChangeCheck();
		bool showNormals = EditorGUILayout.Toggle("Show Normals", spline.showNormals);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Toggle Show Normals");
			spline.showNormals = showNormals;
		}

		EditorGUI.BeginChangeCheck();
		float nmlen = EditorGUILayout.FloatField("Length", spline.normalDisplayLength);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(spline, "Set Normal Display Length");
			spline.normalDisplayLength = nmlen;
		}
		EditorGUILayout.EndHorizontal();

	}

	private void RemovePoint() {
		spline.RemovePoint(selectedIndex);
		if (selectedIndex > 0) {
			--selectedIndex;
		} else if (spline.Count == 0) {
			selectedIndex = -1;
		}

	}

	private void AddNewPoint() {
		// First point at zero
		Vector2 pos = Vector2.zero;
		if (spline.Count == 1) {
			// Second point up & right 2 units
			pos = spline.GetPoint(0) + Vector2.up * 2.0f + Vector2.right * 2.0f;
		} else if (spline.Count > 1) {
			// Third+ point extended from  previous & varied a little
			// rotate left/right alternately for interest
			Vector2 endpos = spline.GetPoint(spline.Count-1);
			Vector2 diff = endpos - spline.GetPoint(spline.Count-2);
			float angle = spline.Count % 2 > 0 ? 30.0f : -30.0f;
			diff = Quaternion.AngleAxis(angle, Vector3.forward) * diff;
			pos = endpos + diff;
		}
		spline.AddPoint(pos);

	}

	private void InsertNewPoint() {
		if (selectedIndex < 0 || selectedIndex >= spline.Count)
			return;

		if (spline.Count == 0) {
			AddNewPoint();
		} else {
			Vector2 pos = Vector2.zero;
			if (selectedIndex == 0) {
				// Insert at start
				// If nothing to project, just down & left
				if (spline.Count == 1)
					pos = spline.GetPoint(0) - Vector2.up * 2.0f - Vector2.right * 2.0f;
				else {
					// Extended from  previous & varied a little
					// rotate left/right alternately for interest
					Vector2 tangent = spline.Derivative(0);
					pos = spline.GetPoint(0) - tangent * 2f;
				}

			} else {
				// split half way between previous and selected
				pos = spline.Interpolate(selectedIndex-1, 0.5f);
			}

			spline.InsertPoint(selectedIndex, pos);
		}
	}

	

	private void DrawSelectedPointInspector() {
		if (selectedIndex == -1) {
			EditorGUILayout.LabelField("Selected Point: None");
		} else {
			EditorGUI.BeginChangeCheck();
			Vector2 point = EditorGUILayout.Vector2Field("Selected Point", spline.GetPoint(selectedIndex));
			if (EditorGUI.EndChangeCheck()) {
				Undo.RecordObject(spline, "Move Point");
				spline.SetPoint(selectedIndex, point);
			}
		}
	}

	private void OnSceneGUI () {
		spline = target as Spline2DComponent;

        DrawPoints();
	}

    private void DrawPoints() {
		if (spline.Count == 0) {
			return;
		}
		for (int i = 0; i < spline.Count; ++i) {
			ShowPoint(i);
		}
    }

	private void ShowPoint (int index) {
		Vector3 point = spline.GetPointWorldSpace(index);
		float size = HandleUtility.GetHandleSize(point);
		if (index == 0) {
			Handles.color = Color.green;
		} else if (index == spline.Count - 1 && !spline.IsClosed) {
			Handles.color = Color.red;
		} else {
			Handles.color = Color.cyan;
		}

		if (Handles.Button(point, Quaternion.identity, size * handleSize, size * pickSize, Handles.DotCap)) {
			selectedIndex = index;
			Repaint();
		}
		if (selectedIndex == index) {
			EditorGUI.BeginChangeCheck();
			point = Handles.FreeMoveHandle(point, Quaternion.identity, size * 1.5f * handleSize, Vector3.zero, Handles.CircleCap);
			if (EditorGUI.EndChangeCheck()) {
				Undo.RecordObject(spline, "Move Point");
				EditorUtility.SetDirty(spline);
				spline.SetPointWorldSpace(index, point);
			}
		}
	}

}

