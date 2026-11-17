using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PoseStudio.Model;

/// <summary>Sitzblock: ein schlichter Quader unter dem Gesäß, sobald die Figur sitzt (Oberschenkel etwa waagerecht).
/// Oberkante = tiefster Punkt des Beckens, Unterkante = Boden, Tiefe vom Gesäß bis kurz vor die Knie, Ausrichtung wie die Figur.
/// Röcke und Kleider liegen darauf auf. Nicht in den ControlNet-Bildern.</summary>
public sealed partial class Mannequin
{
    private readonly ShadeBrush _seatBrush = new(Color.FromRgb(0x9C, 0x96, 0x8E));
    private Part? _seatPart;

    /// <summary>Sitzblock automatisch einblenden, wenn die Figur sitzt.</summary>
    public bool SeatBlock { get; private set; } = true;

    /// <summary>Aktueller Sitzblock (Weltkoordinaten): Mitte unten, Achsen vorne/seitlich, halbe Maße, Höhe. null = keiner.</summary>
    private (Point3D Base, Vector3D Fwd, Vector3D Side, double Back, double Front, double HalfW, double Top)? _seat;

    public Color SeatColor { get => _seatBrush.Color; set => _seatBrush.Color = value; }

    /// <summary>Steht gerade ein Sitzblock unter der Figur?</summary>
    public bool HasSeat => _seat != null;

    public void SetSeatBlock(bool on)
    {
        if (on == SeatBlock) return;
        SeatBlock = on;
        BuildParts();
        Update();
    }

    private void BuildSeat()
    {
        _seatPart = null;
        if (!SeatBlock) return;
        var mesh = new MeshGeometry3D();
        for (int f = 0; f < 6; f++)
        {
            for (int k = 0; k < 4; k++) { mesh.Positions.Add(new Point3D()); mesh.Normals.Add(new Vector3D(0, 1, 0)); }
            int b = f * 4;
            mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
            mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 2); mesh.TriangleIndices.Add(b + 3);
        }
        AddPart("pelvis", mesh, MaterialKind.Seat, true, dynamic: true);
        _seatPart = Root.Parts[^1];
    }

    /// <summary>Sitzblock an die aktuelle Pose anpassen (vor Rock und Haaren, die darauf aufliegen).</summary>
    private void UpdateSeat()
    {
        _seat = null;
        if (_seatPart == null) return;
        var pel = PelvisWorld;
        var fwd = Root.World.Transform(new Vector3D(0, 0, 1)); fwd.Y = 0;
        if (fwd.LengthSquared < 1e-9) fwd = new Vector3D(0, 0, 1);
        fwd.Normalize();
        var side = Vector3D.CrossProduct(new Vector3D(0, 1, 0), fwd);

        // Sitzt die Figur? Knie kaum tiefer als die Hüften, Becken deutlich über dem Boden
        var hipL = this["hip_l"].World.Transform(new Point3D()); var hipR = this["hip_r"].World.Transform(new Point3D());
        var kneeL = this["knee_l"].World.Transform(new Point3D()); var kneeR = this["knee_r"].World.Transform(new Point3D());
        double thigh = ((kneeL - hipL).Length + (kneeR - hipR).Length) / 2;
        double drop = ((hipL.Y - kneeL.Y) + (hipR.Y - kneeR.Y)) / 2;
        // Sitzhöhe: tiefster Punkt des Rumpfes (Gesäß) – nur Körper, ohne Kleidung
        double seatY = double.MaxValue;
        foreach (var p in Root.Parts)
        {
            if (p.Kind != MaterialKind.Body) continue;
            var m = ((MatrixTransform3D)p.Model.Transform).Matrix;
            var mesh = (MeshGeometry3D)p.Model.Geometry;
            for (int i = 0; i < mesh.Positions.Count; i += 3) seatY = Math.Min(seatY, m.Transform(mesh.Positions[i]).Y);
        }
        // Nicht beim Sitzen am Boden, Knien, Schneidersitz oder in der Hocke: Gesäß deutlich über dem tiefsten Körperpunkt,
        // Knie nicht am Boden, Knie nicht tief gebeugt (Stuhl etwa 90°)
        double minY = ComputeMinY();
        double kneeBend = (this["knee_l"].BendValue + this["knee_r"].BendValue) / 2;
        double kneeLow = Math.Min(kneeL.Y, kneeR.Y) - minY;
        bool sitting = drop < thigh * 0.55 && seatY - minY > 0.18 * thigh / 0.45 && kneeLow > 0.25 * thigh / 0.45 && kneeBend < 125;
        if (sitting)
        {
            double knee = (Vector3D.DotProduct(kneeL - pel, fwd) + Vector3D.DotProduct(kneeR - pel, fwd)) / 2;
            double front = Math.Max(0.12, knee - 0.07 * thigh / 0.45), back = 0.16 * thigh / 0.45;
            double halfW = Math.Max(Math.Abs(Vector3D.DotProduct(hipL - hipR, side)) / 2 + 0.11, 0.2);
            var center = new Point3D(pel.X, Math.Min(0, minY), pel.Z);
            _seat = (center, fwd, side, back, front, halfW, seatY - 0.002 - center.Y);
        }

        var pos = new Point3DCollection(24 + DummyVertices); var nrm = new Vector3DCollection(24 + DummyVertices);
        if (_seat is { } s)
        {
            var up = new Vector3D(0, 1, 0);
            Point3D C(double f, double w, double y) => s.Base + s.Fwd * f + s.Side * w + up * y;
            void Face(Vector3D n, Point3D a, Point3D b, Point3D c, Point3D d)
            {
                // Vorderseite nach außen: Reihenfolge prüfen
                var fn = Vector3D.CrossProduct(b - a, c - a);
                if (Vector3D.DotProduct(fn, n) < 0) (b, d) = (d, b);
                pos.Add(a); pos.Add(b); pos.Add(c); pos.Add(d);
                for (int k = 0; k < 4; k++) nrm.Add(n);
            }
            double f0 = -s.Back, f1 = s.Front, w = s.HalfW, t = s.Top;
            Face(up, C(f0, -w, t), C(f1, -w, t), C(f1, w, t), C(f0, w, t));
            Face(-up, C(f0, -w, 0), C(f0, w, 0), C(f1, w, 0), C(f1, -w, 0));
            Face(s.Fwd, C(f1, -w, 0), C(f1, w, 0), C(f1, w, t), C(f1, -w, t));
            Face(-s.Fwd, C(f0, -w, 0), C(f0, -w, t), C(f0, w, t), C(f0, w, 0));
            Face(s.Side, C(f0, w, 0), C(f0, w, t), C(f1, w, t), C(f1, w, 0));
            Face(-s.Side, C(f0, -w, 0), C(f1, -w, 0), C(f1, -w, t), C(f0, -w, t));
        }
        else
            for (int i = 0; i < 24; i++) { pos.Add(pel); nrm.Add(new Vector3D(0, 1, 0)); } // ausgeblendet: auf einen Punkt zusammengezogen
        for (int i = 0; i < DummyVertices; i++) { pos.Add(pos[0]); nrm.Add(nrm[0]); }
        var gm = (MeshGeometry3D)_seatPart.Model.Geometry;
        gm.Positions = pos;
        gm.Normals = nrm;
    }

    /// <summary>Punkt aus dem Sitzblock schieben (nach oben, wenn er nah an der Oberkante ist, sonst zur nächsten Seite).</summary>
    private Point3D CollideSeat(Point3D p, double margin)
    {
        if (_seat is not { } s) return p;
        var v = p - s.Base;
        double f = Vector3D.DotProduct(v, s.Fwd), w = Vector3D.DotProduct(v, s.Side), y = v.Y;
        double top = s.Top + margin;
        if (y >= top || y < 0 || f <= -s.Back - margin || f >= s.Front + margin || Math.Abs(w) >= s.HalfW + margin) return p;
        double dTop = top - y, dFront = s.Front + margin - f, dBack = f + s.Back + margin, dSide = s.HalfW + margin - Math.Abs(w);
        double m = Math.Min(Math.Min(dTop, dFront), Math.Min(dBack, dSide));
        if (m == dTop) return p + new Vector3D(0, dTop, 0);
        if (m == dFront) return p + s.Fwd * dFront;
        if (m == dBack) return p - s.Fwd * dBack;
        return p + s.Side * (Math.Sign(w == 0 ? 1 : w) * dSide);
    }
}
