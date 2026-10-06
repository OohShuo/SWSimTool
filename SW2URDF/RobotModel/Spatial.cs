using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SW2URDF.RobotModel
{
    // Double precision value types. Arrays are used only at serialization/API boundaries.
    public struct Vector3d
    {
        public readonly double X, Y, Z;
        public Vector3d(double x, double y, double z) { X=Finite(x); Y=Finite(y); Z=Finite(z); }
        public static double Finite(double x) { if(double.IsNaN(x)||double.IsInfinity(x)) throw new InvalidDataException("Non-finite model value"); return x; }
        public double Length { get { return Math.Sqrt(X*X+Y*Y+Z*Z); } }
        public Vector3d Normalized() { var n=Length; if(n<=1e-15)throw new InvalidDataException("Joint axis is zero"); return this*(1/n); }
        public static Vector3d operator +(Vector3d a,Vector3d b) { return new Vector3d(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static Vector3d operator -(Vector3d a,Vector3d b) { return new Vector3d(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static Vector3d operator *(Vector3d a,double b) { return new Vector3d(a.X*b,a.Y*b,a.Z*b); }
        public static Vector3d Cross(Vector3d a,Vector3d b) { return new Vector3d(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public double[] ToArray() { return new[]{X,Y,Z}; }
        public override string ToString() { return Numbers.Text(ToArray()); }
    }
    public struct Quaterniond
    {
        public readonly double W,X,Y,Z;
        public Quaterniond(double w,double x,double y,double z)
        {
            Vector3d.Finite(w);Vector3d.Finite(x);Vector3d.Finite(y);Vector3d.Finite(z);
            var n=Math.Sqrt(w*w+x*x+y*y+z*z);if(n<=1e-15)throw new InvalidDataException("Quaternion is zero");
            W=w/n;X=x/n;Y=y/n;Z=z/n;
        }
        public static Quaterniond Identity { get { return new Quaterniond(1,0,0,0); } }
        public static Quaterniond FromRpy(Vector3d rpy)
        {
            var cr=Math.Cos(rpy.X/2);var sr=Math.Sin(rpy.X/2);var cp=Math.Cos(rpy.Y/2);var sp=Math.Sin(rpy.Y/2);var cy=Math.Cos(rpy.Z/2);var sy=Math.Sin(rpy.Z/2);
            return new Quaterniond(cr*cp*cy+sr*sp*sy,sr*cp*cy-cr*sp*sy,cr*sp*cy+sr*cp*sy,cr*cp*sy-sr*sp*cy);
        }
        public static Quaterniond operator *(Quaterniond a,Quaterniond b)
        { return new Quaterniond(a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z,a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X,a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W); }
        public Vector3d Rotate(Vector3d v) { var q=new Vector3d(X,Y,Z);var t=Vector3d.Cross(q,v)*2;return v+t*W+Vector3d.Cross(q,t); }
        public override string ToString() { return Numbers.Text(new[]{W,X,Y,Z}); }
    }
    public struct RigidTransform
    {
        // AFromB maps coordinates in frame B into frame A: pA=R*pB+t.
        public readonly Vector3d Translation;
        public readonly Quaterniond Rotation;
        public RigidTransform(Vector3d translation,Quaterniond rotation) { Translation=translation;Rotation=rotation; }
        public static RigidTransform Identity { get { return new RigidTransform(new Vector3d(),Quaterniond.Identity); } }
        public Vector3d Apply(Vector3d value) { return Rotation.Rotate(value)+Translation; }
        public static RigidTransform operator *(RigidTransform a,RigidTransform b) { return new RigidTransform(a.Apply(b.Translation),a.Rotation*b.Rotation); }
    }
    public struct SymmetricInertia
    {
        public readonly double XX,YY,ZZ,XY,XZ,YZ;
        public SymmetricInertia(double xx,double yy,double zz,double xy,double xz,double yz) { XX=Vector3d.Finite(xx);YY=Vector3d.Finite(yy);ZZ=Vector3d.Finite(zz);XY=Vector3d.Finite(xy);XZ=Vector3d.Finite(xz);YZ=Vector3d.Finite(yz); }
        public SymmetricInertia Rotated(Quaterniond rotation)
        {
            var columns=new[]{rotation.Rotate(new Vector3d(1,0,0)),rotation.Rotate(new Vector3d(0,1,0)),rotation.Rotate(new Vector3d(0,0,1))};
            var r=new double[3,3];for(int j=0;j<3;j++){var c=columns[j].ToArray();for(int i=0;i<3;i++)r[i,j]=c[i];}
            var m=new[,]{{XX,XY,XZ},{XY,YY,YZ},{XZ,YZ,ZZ}};var result=new double[3,3];
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)for(int l=0;l<3;l++)result[i,j]+=r[i,k]*m[k,l]*r[j,l];
            return new SymmetricInertia(result[0,0],result[1,1],result[2,2],result[0,1],result[0,2],result[1,2]);
        }
        public void Validate(double mass)
        {
            // Positive semidefinite I and second-moment matrix S=trace(I)/2*identity-I.
            CheckPsd(XX,YY,ZZ,XY,XZ,YZ);
            var half=(XX+YY+ZZ)/2;CheckPsd(half-XX,half-YY,half-ZZ,-XY,-XZ,-YZ);
            if(mass>0&&(XX<=0||YY<=0||ZZ<=0||XX*YY-XY*XY<=0||Determinant(XX,YY,ZZ,XY,XZ,YZ)<=0))throw new InvalidDataException("Positive mass requires positive-definite inertia");
            if(mass==0&&ToArray().Any(v=>v!=0))throw new InvalidDataException("Zero mass cannot have nonzero inertia");
        }
        static double Determinant(double a,double b,double c,double d,double e,double f) { return a*b*c+2*d*e*f-a*f*f-b*e*e-c*d*d; }
        static void CheckPsd(double a,double b,double c,double d,double e,double f)
        {
            var s=Math.Max(Math.Max(Math.Abs(a),Math.Abs(b)),Math.Max(Math.Abs(c),Math.Max(Math.Abs(d),Math.Max(Math.Abs(e),Math.Abs(f)))));
            if(s==0)return;a/=s;b/=s;c/=s;d/=s;e/=s;f/=s;const double tol=1e-12;
            if(a < -tol||b < -tol||c < -tol||a*b-d*d < -tol||a*c-e*e < -tol||b*c-f*f < -tol||Determinant(a,b,c,d,e,f) < -tol)throw new InvalidDataException("Inertia is not physically realizable");
        }
        public double[] ToArray() { return new[]{XX,YY,ZZ,XY,XZ,YZ}; }
        public override string ToString() { return Numbers.Text(ToArray()); }
    }
    internal static class Numbers
    {
        public static string Format(double value) { return Vector3d.Finite(value).ToString("R",CultureInfo.InvariantCulture); }
        public static string Text(System.Collections.Generic.IEnumerable<double> values) { return string.Join(" ",values.Select(Format)); }
        public static double Parse(string value) { return Vector3d.Finite(double.Parse(value,CultureInfo.InvariantCulture)); }
    }
}
