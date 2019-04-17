using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

// Utility class for calculating a Cubic multi-segment (Hermite) spline in 2D
// Hermite splines are convenient because they only need 2 positions and 2
// tangents per segment, which can be automatically calculated from the surrounding
// points if desired
// The spline can be extended dynamically over time and must always consist of
// 3 or more points. If the spline is closed, the spline will loop back to the
// first point.
// It can provide positions, derivatives (slope of curve) either at a parametric
// 't' value over the whole curve, or as a function of distance along the curve
// for constant-speed traversal. The distance is calculated approximately via
// sampling (cheap integration), its accuracy is determined by LengthSamplesPerSegment
// which defaults to 5 (a decent trade-off for most cases).
// This object is not a MonoBehaviour to keep it flexible. If you want to
// save/display one in a scene, use the wrapper Spline2DComponent class.
public class Spline2D {
    private bool tangentsDirty = true;
    private bool lenSampleDirty = true;
    // Points which the curve passes through.
    private List<Vector2> points = new List<Vector2>();
    // Tangents at each point; automatically calculated
    private List<Vector2> tangents = new List<Vector2>();
    private bool closed;
    /// Whether the spline is closed; if so, the first point is also the last
    public bool IsClosed {
        get { return closed; }
        set {
            closed = value;
            tangentsDirty = true;
            lenSampleDirty = true;
        }
    }
    private float curvature = 0.5f;
    /// The amount of curvature in the spline; 0.5 is Catmull-Rom
    public float Curvature {
        get { return curvature; }
        set {
            curvature = value;
            tangentsDirty = true;
            lenSampleDirty = true;
        }
    }
    private int lengthSamplesPerSegment = 5;
    /// Accuracy of sampling curve to traverse by distance
    public int LengthSamplesPerSegment {
        get { return lengthSamplesPerSegment; }
        set {
            lengthSamplesPerSegment = value;
            lenSampleDirty = true;
        }
    }

    private struct DistanceToT {
        public float distance;
        public float t;
        public DistanceToT(float dist, float tm) {
            distance = dist;
            t = tm;
        }
    }
    private List<DistanceToT> distanceToTList = new List<DistanceToT>();

    /// Get point count
    public int Count {
        get { return points.Count; }
    }

    /// Return the approximate length of the curve, as derived by sampling the
    /// curve at a resolution of LengthSamplesPerSegment
    public float Length {
        get {
            Recalculate(true);

            if (distanceToTList.Count == 0)
                return 0.0f;

            return distanceToTList[distanceToTList.Count-1].distance;
        }
    }



    public Spline2D() {
    }

    public Spline2D(List<Vector2> intersectionPoints, bool isClosed = false, float curve = 0.5f,
        int samplesPerSegment = 5) {
        points = intersectionPoints;
        closed = isClosed;
        curvature = curve;
        lengthSamplesPerSegment = samplesPerSegment;
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Add a point to the curve
    public void AddPoint(Vector2 p) {
        points.Add(p);
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Add a point to the curve by dropping the earliest point and scrolling
    /// all other points backwards
    /// This allows you to maintain a fixed-size spline which you extend to new
    /// points at the expense of dropping earliest points. This is efficient for
    /// unbounded paths you need to keep adding to but don't need the old history
    /// Note that when you do this the distances change to being measured from
    /// the new start point so you have to adjust your next interpolation request
    /// to take this into account. Subtract DistanceAtPoint(1) from distances
    /// before calling this method, for example (or for plain `t` interpolation,
    /// reduce `t` by 1f/Count)
    /// This method cannot be used on closed splines
    public void AddPointScroll(Vector2 p) {
        Assert.IsFalse(closed, "Cannot use AddPointScroll on closed splines!");

        if (points.Count == 0) {
            AddPoint(p);
        } else {
            for (int i = 0; i < points.Count - 1; ++i) {
                points[i] = points[i+1];
            }
            points[points.Count-1] = p;
        }
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Add a list of points to the end of the spline, in order
    public void AddPoints(IEnumerable<Vector2> plist) {
        points.AddRange(plist);
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Replace all the points in the spline from fromIndex onwards with a new set
    public void ReplacePoints(IEnumerable<Vector2> plist, int fromIndex = 0) {
        Assert.IsTrue(fromIndex < points.Count, "Spline2D: point index out of range");

        points.RemoveRange(fromIndex, points.Count-fromIndex);
        points.AddRange(plist);
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Change a point on the curve
    public void SetPoint(int index, Vector2 p) {
        Assert.IsTrue(index < points.Count, "Spline2D: point index out of range");

        points[index] = p;
        tangentsDirty = true;
        lenSampleDirty = true;
    }
    /// Remove a point on the curve
    public void RemovePoint(int index) {
        Assert.IsTrue(index < points.Count, "Spline2D: point index out of range");

        points.RemoveAt(index);
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    /// Insert a point on the curve before the given index
    public void InsertPoint(int index, Vector2 p) {
        Assert.IsTrue(index <= points.Count && index >= 0, "Spline2D: point index out of range");
        points.Insert(index, p);
        tangentsDirty = true;
        lenSampleDirty = true;
    }

    // TODO add more efficient 'scrolling' curve of N length where we add one &
    // drop the earliest for effcient non-closed curves that continuously extend

    /// Reset &amp; start again
    public void Clear() {
        points.Clear();
        tangentsDirty = true;
        lenSampleDirty = true;
    }
    /// Get a single point
    public Vector2 GetPoint(int index) {
        Assert.IsTrue(index < points.Count, "Spline2D: point index out of range");

        return points[index];
    }



    /// Interpolate a position on the entire curve. Note that if the control
    /// points are not evenly spaced, this may result in varying speeds.
    public Vector2 Interpolate(float t) {
        Recalculate(false);

        int segIdx;
        float tSeg;
        ToSegment(t, out segIdx, out tSeg);

        return Interpolate(segIdx, tSeg);
    }

    private void ToSegment(float t, out int iSeg, out float tSeg) {
        // Work out which segment this is in
        // Closed loops have 1 extra node at t=1.0 ie the first node
        float pointCount = closed ? points.Count : points.Count - 1;
        float fSeg = t * pointCount;
        iSeg = (int)fSeg;
        // Remainder t
        tSeg = fSeg - iSeg;
    }

    /// Interpolate a position between one point on the curve and the next
    /// Rather than interpolating over the entire curve, this simply interpolates
    /// between the point with fromIndex and the next point
    public Vector2 Interpolate(int fromIndex, float t) {
        Recalculate(false);

        int toIndex = fromIndex + 1;
        // At or beyond last index?
        if (toIndex >= points.Count) {
            if (closed) {
                // Wrap
                toIndex = toIndex % points.Count;
                fromIndex = fromIndex % points.Count;
            } else {
                // Clamp to end
                return points[points.Count-1];
            }
        }

        // Fast special cases
        if (Mathf.Approximately(t, 0.0f)) {
            return points[fromIndex];
        } else if (Mathf.Approximately(t, 1.0f)) {
            return points[toIndex];
        }

        // Now general case
        // Pre-calculate powers
        float t2 = t*t;
        float t3 = t2*t;
        // Calculate hermite basis parts
        float h1 =  2f*t3 - 3f*t2 + 1f;
        float h2 = -2f*t3 + 3f*t2;
        float h3 =     t3 - 2f*t2 + t;
        float h4 =     t3 -    t2;

        return h1 * points[fromIndex] +
               h2 * points[toIndex] +
               h3 * tangents[fromIndex] +
               h4 * tangents[toIndex];


    }

    /// Get derivative of the curve at a point. Note that if the control
    /// points are not evenly spaced, this may result in varying speeds.
    /// This is not normalised by default in case you don't need that
    public Vector2 Derivative(float t) {
        Recalculate(false);

        int segIdx;
        float tSeg;
        ToSegment(t, out segIdx, out tSeg);

        return Derivative(segIdx, tSeg);
    }

    /// Get derivative of curve between one point on the curve and the next
    /// Rather than interpolating over the entire curve, this simply interpolates
    /// between the point with fromIndex and the next segment
    /// This is not normalised by default in case you don't need that
    public Vector2 Derivative(int fromIndex, float t) {
        Recalculate(false);

        int toIndex = fromIndex + 1;
        // At or beyond last index?
        if (toIndex >= points.Count) {
            if (closed) {
                // Wrap
                toIndex = toIndex % points.Count;
                fromIndex = fromIndex % points.Count;
            } else {
                // Clamp to end
                toIndex = fromIndex;
            }
        }

        // Pre-calculate power
        float t2 = t*t;
        // Derivative of hermite basis parts
        float h1 =  6f*t2 - 6f*t;
        float h2 = -6f*t2 + 6f*t;
        float h3 =  3f*t2 - 4f*t + 1;
        float h4 =  3f*t2 - 2f*t;

        return h1 * points[fromIndex] +
               h2 * points[toIndex] +
               h3 * tangents[fromIndex] +
               h4 * tangents[toIndex];


    }

    /// Convert a physical distance to a t position on the curve. This is
    /// approximate, the accuracy of can be changed via LengthSamplesPerSegment
    public float DistanceToLinearT(float dist) {
        int i;
        return DistanceToLinearT(dist, out i);
    }

    /// Convert a physical distance to a t position on the curve. This is
    /// approximate, the accuracy of can be changed via LengthSamplesPerSegment
    /// Also returns an out param of the last point index passed
    public float DistanceToLinearT(float dist, out int lastIndex) {
        Recalculate(true);

        if (distanceToTList.Count == 0) {
            lastIndex = 0;
            return 0.0f;
        }

        // Check to see if distance > length
        float len = Length;
        if (dist >= len) {
            if (closed) {
                // wrap and continue as usual
                dist = dist % len;
            } else {
                // clamp to end
                lastIndex = points.Count - 1;
                return 1.0f;
            }
        }


        float prevDist = 0.0f;
        float prevT = 0.0f;
        for (int i = 0; i < distanceToTList.Count; ++i) {
            DistanceToT distToT = distanceToTList[i];
            if (dist < distToT.distance) {
                float distanceT = Mathf.InverseLerp(prevDist, distToT.distance, dist);
                lastIndex = i / lengthSamplesPerSegment; // not i-1 because distanceToTList starts at point index 1
                return Mathf.Lerp(prevT, distToT.t, distanceT);
            }
            prevDist = distToT.distance;
            prevT = distToT.t;
        }

        // If we got here then we ran off the end
        lastIndex = points.Count - 1;
        return 1.0f;
    }

    /// Interpolate a position on the entire curve based on distance. This is
    /// approximate, the accuracy of can be changed via LengthSamplesPerSegment
    public Vector2 InterpolateDistance(float dist) {
        float t = DistanceToLinearT(dist);
        return Interpolate(t);
    }

    /// Get derivative of the curve at a point long the curve at a distance. This
    /// is approximate, the accuracy of this can be changed via
    /// LengthSamplesPerSegment
    public Vector2 DerivativeDistance(float dist) {
        float t = DistanceToLinearT(dist);
        return Derivative(t);
    }

    /// Get the distance at a point index
    public float DistanceAtPoint(int index) {
        Assert.IsTrue(index < points.Count, "Spline2D: point index out of range");

        // Length samples are from first actual distance, with points at
        // LengthSamplesPerSegment intervals
        if (index == 0) {
            return 0.0f;
        }
        Recalculate(true);
        return distanceToTList[index*lengthSamplesPerSegment - 1].distance;
    }

    private void Recalculate(bool includingLength) {
        if (tangentsDirty) {
            recalcTangents();
            tangentsDirty = false;
        }
        // Need to check the length of distanceToTList because for some reason
        // when scripts are reloaded in the editor, tangents survives but
        // distanceToTList does not (and dirty flags remain false). Maybe because
        // it's a custom struct it can't be restored
        if (includingLength &&
            (lenSampleDirty || distanceToTList.Count == 0)) {
            recalcLength();
            lenSampleDirty = false;
        }
    }

    private void recalcTangents() {
        int numPoints = points.Count;
        if (numPoints < 2) {
            // Nothing to do here
            return;
        }
        tangents.Clear();
        tangents.Capacity = numPoints;

        for (int i = 0; i < numPoints; ++i) {
            Vector2 tangent;
            if (i == 0) {
                // Special case start
                if (closed) {
                    // Wrap around
                    tangent = makeTangent(points[numPoints-1], points[1]);
                } else {
                    // starting tangent is just from start to point 1
                    tangent = makeTangent(points[i], points[i+1]);
                }
            } else if (i == numPoints-1) {
                // Special case end
                if (closed) {
                    // Wrap around
                    tangent = makeTangent(points[i-1], points[0]);
                } else {
                    // end tangent just from prev point to end point
                    tangent = makeTangent(points[i-1], points[i]);
                }
            } else {
                // Mid point is average of previous point and next point
                tangent = makeTangent(points[i-1], points[i+1]);
            }
            tangents.Add(tangent);
        }
    }

    private Vector2 makeTangent(Vector2 p1, Vector2 p2) {
        return curvature * (p2 - p1);
    }

    private void recalcLength() {
        int numPoints = points.Count;
        if (numPoints < 2) {
            // Nothing to do here
            return;
        }
        // Sample along curve & build distance -> t lookup, can interpolate t
        // linearly between nearest points to approximate distance parametrisation
        // count is segments * lengthSamplesPerSegment
        // We sample from for st t > 0 all the way to t = 1
        // For a closed loop, t = 1 is the first point again, for open its the last point
        int samples = lengthSamplesPerSegment * (closed ? points.Count : points.Count-1);

        distanceToTList.Clear();
        distanceToTList.Capacity = samples;
        float distanceSoFar = 0.0f;
        float tinc = 1.0f / (float)samples;
        float t = tinc; // we don't start at 0 since that's easy
        Vector2 lastPos = points[0];
        for (int i = 1; i <= samples; ++i) {
            Vector2 pos = Interpolate(t);
            float distInc = Vector2.Distance(lastPos, pos);
            distanceSoFar += distInc;
            distanceToTList.Add(new DistanceToT(distanceSoFar, t));
            lastPos = pos;
            t += tinc;
        }
    }

}