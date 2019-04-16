using System;
using System.Collections.Generic;
using UnityEngine;

// Utility class for displaying a Spline2D in a scene as a component
// All the real work is done by Spline2D, this just makes is serialize and render
// editable gizmos (also see Spline2DInspector)
public class Spline2DComponent : MonoBehaviour {
    private Spline2D spline;

    [Tooltip("Display in the XZ plane in the editor instead of the default XY plane (spline is still in XY)")]
    public bool displayXZ;

    private void InitSpline() {
        if (spline == null) {
            spline = new Spline2D(points, closed, curvature, lengthSamplesPerSegment);
        }
    }

    // All state is duplicated so it can be correctly serialized, Unity never
    // calls getters/setters on load/save so they're useless, we have to store
    // actual fields here. In the editor, a custom inspector is used to ensure
    // the property setters are called to sync the underlying Spline2D

    // Points which the curve passes through.
    [SerializeField]
    private List<Vector2> points = new List<Vector2>();
    [SerializeField]
    private bool closed;
    /// Whether the spline is closed; if so, the first point is also the last
    public bool IsClosed {
        get { return closed; }
        set {
            closed = value;
            InitSpline();
            spline.IsClosed = closed;
        }
    }
    [SerializeField]
    private float curvature = 0.5f;
    /// The amount of curvature in the spline; 0.5 is Catmull-Rom
    public float Curvature {
        get { return curvature; }
        set {
            curvature = value;
            InitSpline();
            spline.Curvature = curvature;
        }
    }
    [SerializeField]
    private int lengthSamplesPerSegment = 5;
    /// Accuracy of sampling curve to traverse by distance
    public int LengthSamplesPerSegment {
        get { return lengthSamplesPerSegment; }
        set {
            lengthSamplesPerSegment = value;
            InitSpline();
            spline.LengthSamplesPerSegment = lengthSamplesPerSegment;
        }
    }

    // For gizmo drawing
	private const int stepsPerSegment = 20;
    public bool showNormals;
	public bool showDistance;
	public float distanceMarker = 1.0f;


    /// Get point count
    public int Count {
        get { return points.Count; }
    }

    /// Return the approximate length of the curve, as derived by sampling the
    /// curve at a resolution of LengthSamplesPerSegment
    public float Length {
        get {
            InitSpline();
            return spline.Length;
        }
    }

    /// Add a point to the curve
    public void AddPoint(Vector2 p) {
        // We share the same list so adding there adds here
        InitSpline();
        spline.AddPoint(p);
    }

    /// Change a point on the curve
    public void SetPoint(int index, Vector2 p) {
        // We share the same list so changing there adds here
        InitSpline();
        spline.SetPoint(index, p);
    }
    // Remove a point on the curve
    public void RemovePoint(int index) {
        // We share the same list so changing there adds here
        InitSpline();
        spline.RemovePoint(index);
    }

    // TODO add more efficient 'scrolling' curve of N length where we add one &
    // drop the earliest for effcient non-closed curves that continuously extend

    /// Reset &amp; start again
    public void Clear() {
        // We share the same list so changing there adds here
        InitSpline();
        spline.Clear();
    }
    /// Get a single point
    public Vector2 GetPoint(int index) {
        InitSpline();
        return spline.GetPoint(index);
    }



    /// Interpolate a position on the entire curve. Note that if the control
    /// points are not evenly spaced, this may result in varying speeds.
    public Vector2 Interpolate(float t) {
        InitSpline();
        return spline.Interpolate(t);
    }

    /// Interpolate a position between one point on the curve and the next
    /// Rather than interpolating over the entire curve, this simply interpolates
    /// between the point with fromIndex and the next point
    public Vector2 Interpolate(int fromIndex, float t) {
        InitSpline();
        return spline.Interpolate(fromIndex, t);
    }

    /// Get derivative of the curve at a point. Note that if the control
    /// points are not evenly spaced, this may result in varying speeds.
    /// This is not normalised by default in case you don't need that
    public Vector2 Derivative(float t) {
        InitSpline();
        return spline.Derivative(t);
    }

    /// Get derivative of curve between one point on the curve and the next
    /// Rather than interpolating over the entire curve, this simply interpolates
    /// between the point with fromIndex and the next segment
    /// This is not normalised by default in case you don't need that
    public Vector2 Derivative(int fromIndex, float t) {
        InitSpline();
        return spline.Derivative(fromIndex, t);
    }

    /// Convert a physical distance to a t position on the curve. This is
    /// approximate, the accuracy of can be changed via LengthSamplesPerSegment
    public float DistanceToLinearT(float dist) {
        InitSpline();
        return spline.DistanceToLinearT(dist);
    }

    /// Interpolate a position on the entire curve based on distance. This is
    /// approximate, the accuracy of can be changed via LengthSamplesPerSegment
    public Vector2 InterpolateDistance(float dist) {
        InitSpline();
        return spline.InterpolateDistance(dist);
    }

    /// Get derivative of the curve at a point long the curve at a distance. This
    /// is approximate, the accuracy of this can be changed via
    /// LengthSamplesPerSegment
    public Vector2 DerivativeDistance(float dist) {
        InitSpline();
        return spline.DerivativeDistance(dist);
    }

    // Editor functions
    void OnDrawGizmos() {
        DrawCurveGizmo();
    }

    void OnDrawGizmosSelected() {
		DrawNormalsGizmo();
		DrawDistancesGizmo();
    }

    private void DrawCurveGizmo() {
		if (Count == 0) {
			return;
		}
        float sampleWidth = 1.0f / ((float)Count * stepsPerSegment);
        Gizmos.color = Color.yellow;
        Vector3 plast = GetPoint(0);
        if (displayXZ) {
            plast = FlipXYtoXZ(plast);
        }
        plast = transform.TransformPoint(plast);
        for (float t = sampleWidth; t <= 1.0f; t += sampleWidth) {
            Vector3 p = Interpolate(t);
            if (displayXZ)
                p = FlipXYtoXZ(p);
            p = transform.TransformPoint(p);

            Gizmos.DrawLine(plast, p);
            plast = p;
        }
    }

	private void DrawNormalsGizmo() {
		if (Count == 0 || !showNormals) {
			return;
		}
        float sampleWidth = 1.0f / ((float)Count * stepsPerSegment);
        Gizmos.color = Color.magenta;
        for (float t = 0.0f; t <= 1.0f; t += sampleWidth) {
            Vector3 p = Interpolate(t);
            if (displayXZ)
                p = FlipXYtoXZ(p);
            transform.TransformPoint(p);
			Vector3 tangent = Derivative(t);
            if (displayXZ)
                tangent = FlipXYtoXZ(tangent);
            Gizmos.DrawLine(p, p + tangent);
        }
	}

	private void DrawDistancesGizmo() {
		if (Count == 0 || !showDistance || distanceMarker <= 0.0f) {
			return;
		}
        float len = Length;
        Gizmos.color = Color.green;
		Quaternion rot90 = Quaternion.AngleAxis(90, displayXZ ? Vector3.up : Vector3.forward);

        for (float dist = 0.0f; dist <= len; dist += distanceMarker) {
			// Just so we only have to perform the dist->t calculation once
			// for both position & tangent
			float t = DistanceToLinearT(dist);
            Vector3 p = Interpolate(t);
            if (displayXZ)
                p = FlipXYtoXZ(p);
            p = transform.TransformPoint(p);
			Vector3 tangent = Derivative(t);
			// Rotate tangent 90 degrees so we can render marker
			tangent.Normalize();
            if (displayXZ)
                tangent = FlipXYtoXZ(tangent);
			tangent = rot90 * tangent;
			Vector3 t1 = p + tangent;
			Vector3 t2 = p - tangent;
            Gizmos.DrawLine(t1, t2);
        }

	}

    public static Vector3 FlipXYtoXZ(Vector3 inp) {
        return new Vector3(inp.x, 0, inp.y);
    }
    public static Vector3 FlipXZtoXY(Vector3 inp) {
        return new Vector3(inp.x, inp.z, 0);
    }

}