using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

namespace SW2URDF.Simulation
{
    public sealed class CollisionReference
    {
        public string pid { get; set; }
        public string component_pid { get; set; }
        public string label { get; set; }
    }
    public sealed class CollisionGeometry
    {
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string name { get; set; } = "collision";
        public string link { get; set; }
        public string type { get; set; } = "box";
        public string definition { get; set; } = "manual";
        public double[] xyz { get; set; } = new double[3];
        public double[] rpy { get; set; } = new double[3];
        public double[] size { get; set; } = new double[] { .05, .04, .03 };
        public double[] offset_xyz { get; set; } = new double[3];
        public double[] offset_rpy { get; set; } = new double[3];
        public double length_input { get; set; } = .03;
        public double thickness { get; set; } = .01;
        public int extrusion { get; set; }
        public int[] corner_signs { get; set; } = new[] { 1, 1, 1 };
        public int axis { get; set; } = 2;
        public int axis_sign { get; set; } = 1;
        public Dictionary<string, CollisionReference> dimension_references { get; set; } = new Dictionary<string, CollisionReference>();
        public List<CollisionReference> references { get; set; } = new List<CollisionReference>();
        public override string ToString() => name + " [" + type + "]";
        public void Validate()
        {
            int n = type == "box" ? 3 : type == "sphere" ? 1 : type == "cylinder" || type == "capsule" ? 2 : 0;
            if (n == 0 || size == null || size.Length != n || size.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v <= 0))
                throw new InvalidOperationException("尺寸必须为有限正数；胶囊圆柱段长度必须大于 0。");
            foreach (var v in new[] { xyz, rpy, offset_xyz, offset_rpy })
                if (v == null || v.Length != 3 || v.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new InvalidOperationException("位姿无效。");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(link)) throw new InvalidOperationException("名称和 link 不能为空。");
            if(corner_signs==null||corner_signs.Length!=3||corner_signs.Any(s=>s!=1&&s!=-1)||axis<0||axis>2||(axis_sign!=1&&axis_sign!=-1))
                throw new InvalidOperationException("延伸轴或方向无效。");
        }
    }
    public sealed class CollisionPair
    {
        public string link1 { get; set; }
        public string link2 { get; set; }
        public ConstraintSettings solver { get; set; }
        public override string ToString() => link1 + " ↔ " + link2;
    }
    public sealed class CollisionConfiguration
    {
        public bool disable_internal { get; set; } = true;
        public Dictionary<string, string> link_modes { get; set; } = new Dictionary<string, string>();
        public List<CollisionGeometry> geometries { get; set; } = new List<CollisionGeometry>();
        public List<CollisionPair> allowed_pairs { get; set; } = new List<CollisionPair>();
    }
}
